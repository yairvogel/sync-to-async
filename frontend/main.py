from asyncio import sleep
from typing import Literal
from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, StreamingResponse
from fastapi.templating import Jinja2Templates
from fastapi.staticfiles import StaticFiles
import httpx
from urllib.parse import quote
import pika
import os
from contextlib import asynccontextmanager
import logging

from pika.adapters.blocking_connection import BlockingChannel
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

RABBITMQ_CONN = os.environ.get("Rmq", None)

signal = {"cancelled": False}


@asynccontextmanager
async def lifespan(_: FastAPI):
    yield
    signal["cancelled"] = True


def create_connection(connstr: str) -> pika.BlockingConnection:
    return pika.BlockingConnection(pika.URLParameters(connstr))


connection: pika.BlockingConnection | None = (
    create_connection(RABBITMQ_CONN) if RABBITMQ_CONN is not None else None
)

app = FastAPI(lifespan=lifespan)
templates = Jinja2Templates("templates")
app.mount("/static", StaticFiles(directory="static"), name="static")


@app.get("/", response_class=HTMLResponse)
def index(request: Request):
    return templates.TemplateResponse(request, "index.html")


@app.get("/message")
async def ping(request: Request, message: str, delay: int | None):
    async with httpx.AsyncClient() as client:
        res: httpx.Response = await client.get(
            f"http://service:8080/{quote(message)}?delay={delay if delay else 0}",
        )
        return templates.TemplateResponse(
            request, "response.html", {"message": res.text}
        )


LogLevel = Literal["info", "debug", "warn", "err"]


class LogMessage(BaseModel):
    log_level: LogLevel
    log_content: str


async def log_generator(request: Request):
    global connection
    if connection is None:
        return

    if connection.is_closed:
        assert RABBITMQ_CONN is not None
        connection = create_connection(RABBITMQ_CONN)

    try:
        with connection.channel() as channel:
            channel: BlockingChannel
            result = channel.queue_declare("", exclusive=True)
            queue: str = result.method.queue

            channel.queue_bind(exchange="ping", queue=result.method.queue)  # pyright: ignore[reportUnknownMemberType, reportUnknownArgumentType]
            channel.queue_bind(exchange="pong", queue=result.method.queue)  # pyright: ignore[reportUnknownMemberType, reportUnknownArgumentType]

            logger.info(signal["cancelled"])
            while not signal["cancelled"]:
                ok, props, body = channel.basic_get(queue, True)
                if ok:
                    content = body.decode("utf-8")
                    text = f"message {ok.exchange}, routing-key {ok.routing_key}: {content}"
                    print(f"{ok}, {props}, {content}")
                    r = templates.TemplateResponse(
                        request, "log.html", {"log_level": "info", "log_content": text}
                    )
                    body = bytes(r.body).decode("utf-8").strip()
                    yield f"event:log\ndata:{body}\n\n"
                else:
                    await sleep(0.1)
    except Exception as e:
        logger.error(e)
        raise


@app.get("/log", response_class=StreamingResponse)
def log(request: Request):
    return StreamingResponse(log_generator(request), media_type="text/event-stream")
