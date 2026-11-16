import hashlib
import hmac
import json
import time
from urllib.parse import parse_qsl

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

from config import BOT_TOKEN
import database

app = FastAPI(title="LifeCraft API")

app.add_middleware(
CORSMiddleware,
allow_origins=["https://wentezzix79.github.io"],
allow_credentials=False,
allow_methods=["GET", "POST"],
allow_headers=["Content-Type"]
)

class ProfileRequest(BaseModel):
    initData: str

def verify_telegram_data(init_data):
    data = dict(parse_qsl(init_data))
    received_hash = data.pop("hash", None)

    if not received_hash:
        return None

    data_check_string = "\n".join(
        f"{key}={value}" for key, value in sorted(data.items())
    )

    secret_key = hmac.new(
        b"WebAppData",
        BOT_TOKEN.encode(),
        hashlib.sha256
    ).digest()

    calculated_hash = hmac.new(
        secret_key,
        data_check_string.encode(),
        hashlib.sha256
    ).hexdigest()

    if not hmac.compare_digest(calculated_hash, received_hash):
        return None

    auth_date = int(data.get("auth_date", 0))

    if time.time() - auth_date > 86400:
        return None

    return json.loads(data.get("user", "{}"))


@app.post("/api/profile")
async def get_profile(request: ProfileRequest):
    user = verify_telegram_data(request.initData)

    if not user or not user.get("id"):
        raise HTTPException(status_code=401, detail="Ошибка проверки Telegram")

    user_id = user["id"]
    username = user.get("username") or user.get("first_name", "Игрок")

    database.create_player(user_id, username)
    player = database.get_player(user_id)

    return {
        "user_id": player[0],
        "username": player[1],
        "money": player[2],
        "job": player[3],
        "xp": player[4],
        "level": player[5]
    }