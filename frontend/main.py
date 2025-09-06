from asyncio import sleep
from fastapi import FastAPI, Request
import fastapi
from fastapi.responses import HTMLResponse, StreamingResponse
from fastapi.templating import Jinja2Templates
import httpx
from urllib.parse import quote

app = FastAPI()
templates = Jinja2Templates("templates")


@app.get("/", response_class=HTMLResponse)
def index(request: Request):
    return templates.TemplateResponse(request, "index.html")


@app.get("/message")
async def ping(request: Request, message: str):
    async with httpx.AsyncClient() as client:
        res: httpx.Response = await client.get(f"http://service:8080/{quote(message)}")
        return templates.TemplateResponse(
            request, "response.html", {"message": res.text}
        )


async def log_generator(request: Request):
    while True:
        print("ping")
        r = templates.TemplateResponse(
            request, "log.html", {"log_level": "info", "log_content": "ping"}
        )
        text = bytes(r.body).decode("utf-8").strip()
        yield f"event:log\ndata:{text}\n\n"
        await sleep(1)


@app.get("/log", response_class=StreamingResponse)
def log(request: Request):
    print("ping")
    return StreamingResponse(log_generator(request), media_type="text/event-stream")
