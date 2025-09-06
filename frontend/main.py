from fastapi import FastAPI
import fastapi
import httpx
from urllib.parse import quote

app = FastAPI()


@app.get("/{message}")
async def ping(message: str):
    async with httpx.AsyncClient() as client:
        res: httpx.Response = await client.get(f"http://service:8080/{quote(message)}")
        return fastapi.Response(
            await res.aread(), res.status_code, media_type="text/plain"
        )
