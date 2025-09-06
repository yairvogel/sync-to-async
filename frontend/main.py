from asyncio import sleep
from typing import Literal
from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, StreamingResponse
from fastapi.templating import Jinja2Templates
import httpx
from urllib.parse import quote
import pika
import os

from pika.adapters.blocking_connection import BlockingChannel
from pydantic import BaseModel

RABBITMQ_CONN = os.environ.get("Rmq", None)
connection: pika.BlockingConnection | None = None
if RABBITMQ_CONN is not None:
    connection = pika.BlockingConnection(pika.URLParameters(RABBITMQ_CONN))

app = FastAPI()
templates = Jinja2Templates("templates")


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
    if connection is None:
        return
    with connection.channel() as channel:
        channel: BlockingChannel
        result = channel.queue_declare("", exclusive=True)
        queue: str = result.method.queue

        channel.queue_bind(exchange="ping", queue=result.method.queue)  # pyright: ignore[reportUnknownMemberType, reportUnknownArgumentType]
        channel.queue_bind(exchange="pong", queue=result.method.queue)  # pyright: ignore[reportUnknownMemberType, reportUnknownArgumentType]

        while True:
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


@app.get("/log", response_class=StreamingResponse)
def log(request: Request):
    print("ping")
    return StreamingResponse(log_generator(request), media_type="text/event-stream")
