#!/usr/bin/env python3
"""Run a small, persistent LIFE CRAFT server on a trusted local network."""

import argparse
import contextlib
import hashlib
import hmac
import json
import math
import re
import secrets
import sqlite3
import threading
import time
from collections import deque
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

START_CASH = 25000
SHIFT_PAY = 1450
SPAWN = (-5.0, 0.0, -33.0, 0.0)
SESSION_IDLE = 15 * 60
PEER_IDLE = 6.0
MAX_SPEED = 10.5
WORK_RADIUS = 4.0
STATIONS = {
    "employ": ((-39, 10),),
    "locker": ((-46, 17),),
    "terminal": ((-40, 24),),
    "shelf": ((-36, 27), (-29, 27), (-22, 27)),
    "pack": ((-12, 24),),
    "belt": ((-9, 35),),
}
WORK_ORDER = tuple(STATIONS)
WORK_STATUS = {
    "employ": "Смена начата. Переоденьтесь в раздевалке.",
    "locker": "Форма надета. Возьмите заказ на терминале.",
    "terminal": "Заказ выдан. Найдите нужный стеллаж.",
    "shelf": "Товар взят. Отнесите его на упаковку.",
    "pack": "Заказ упакован. Отнесите его на конвейер.",
    "belt": "Заказ сдан. Выплата начислена.",
}
USERNAME = re.compile(r"[a-zA-Z0-9_]{3,24}")


class ApiError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


@dataclass
class Session:
    name: str
    x: float
    y: float
    z: float
    yaw: float
    last_seen: float
    last_move: float
    update_budget: float = 1.2
    last_chat: float = -100.0
    last_work: float = -100.0
    last_save: float = -100.0
    stage: int = 0
    shelf: int = -1

    def public(self):
        return {"name": self.name, "x": self.x, "y": self.y, "z": self.z, "yaw": self.yaw}


def position(values):
    if any(type(v) not in (int, float) or abs(v) > 10000 or not math.isfinite(v)
           for v in values):
        raise ApiError(400, "Некорректная позиция.")
    return [float(v) for v in values]


class World:
    def __init__(self, db_path):
        self.db_path = db_path
        self.lock = threading.RLock()
        self.sessions = {}
        self.chat = deque(maxlen=40)
        self.chat_id = 0
        self.login_limits = {}
        with self.db() as db:
            db.execute("""CREATE TABLE IF NOT EXISTS accounts (
                name TEXT PRIMARY KEY COLLATE NOCASE, salt BLOB NOT NULL, password_hash BLOB NOT NULL,
                cash INTEGER NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, yaw REAL NOT NULL
            )""")

    @contextlib.contextmanager
    def db(self):
        db = sqlite3.connect(self.db_path, timeout=5)
        try:
            with db:
                yield db
        finally:
            db.close()

    @staticmethod
    def password_hash(password, salt):
        return hashlib.scrypt(password.encode("utf-8"), salt=salt, n=16384, r=8, p=1)

    def authenticate(self, route, body, ip):
        name, password = body.get("name"), body.get("password")
        if not isinstance(name, str) or not USERNAME.fullmatch(name):
            raise ApiError(400, "Имя должно содержать 3–24 латинские буквы, цифры или _.")
        if not isinstance(password, str) or not (8 <= len(password) <= 128):
            raise ApiError(400, "Пароль должен содержать от 8 до 128 символов.")
        now = time.monotonic()
        with self.lock:
            self.login_limits = dict(
                (key, stamps) for key, stamps in self.login_limits.items() if now - stamps[-1] < 60)
            attempts = self.login_limits.setdefault(ip, deque(maxlen=18))
            if len(attempts) == attempts.maxlen and now - attempts[0] < 60:
                raise ApiError(429, "Слишком много попыток. Повторите через минуту.")
            attempts.append(now)
        if route == "/register":
            salt = secrets.token_bytes(16)
            password_hash = self.password_hash(password, salt)
            try:
                with self.db() as db:
                    db.execute("INSERT INTO accounts VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                               (name, salt, password_hash, START_CASH, *SPAWN))
            except sqlite3.IntegrityError:
                raise ApiError(409, "Это имя уже занято.") from None
        with self.db() as db:
            row = db.execute("SELECT * FROM accounts WHERE name = ?", (name,)).fetchone()
        salt = row[1] if row else bytes(16)
        supplied_hash = self.password_hash(password, salt)
        if row is None or not hmac.compare_digest(supplied_hash, row[2]):
            raise ApiError(401, "Неверное имя или пароль.")
        with self.lock:
            self.prune(now)
            for token, session in tuple(self.sessions.items()):
                if session.name == row[0]:
                    self.persist_position(session)
                    del self.sessions[token]
            with self.db() as db:
                cash, x, y, z, yaw = db.execute(
                    "SELECT cash,x,y,z,yaw FROM accounts WHERE name=?", (row[0],)).fetchone()
            token = secrets.token_urlsafe(32)
            session = Session(row[0], x, y, z, yaw, now, now)
            self.sessions[token] = session
            return {
                "token": token, "cash": cash, "self": session.public(), "stage": 0, "shelf": -1,
                "message": f"Вы вошли как {session.name}.",
            }

    def persist_position(self, session):
        with self.db() as db:
            db.execute("UPDATE accounts SET x=?,y=?,z=?,yaw=? WHERE name=?",
                       (session.x, session.y, session.z, session.yaw, session.name))

    def prune(self, now):
        for token, session in tuple(self.sessions.items()):
            if now - session.last_seen > SESSION_IDLE:
                self.persist_position(session)
                del self.sessions[token]

    def dispatch(self, route, body, token, ip):
        if route in ("/register", "/login"):
            return self.authenticate(route, body, ip)
        if route not in ("/sync", "/work", "/chat", "/logout"):
            raise ApiError(404, "Неизвестный запрос.")
        now = time.monotonic()
        with self.lock:
            self.prune(now)
            session = self.sessions.get(token)
            if session is None:
                raise ApiError(401, "Сессия завершена. Войдите снова.")
            session.last_seen = now
            if route == "/logout":
                self.persist_position(session)
                del self.sessions[token]
                return {"message": "Вы вышли из сети."}
            if route == "/chat":
                return self.send_chat(session, body, now)
            if route == "/work":
                return self.work(session, body.get("action"), now)
            corrected = self.sync_position(session, body, now)
            with self.db() as db:
                cash, = db.execute("SELECT cash FROM accounts WHERE name=?", (session.name,)).fetchone()
            return {
                "cash": cash, "self": session.public(), "corrected": corrected,
                "peers": [other.public() for other in self.sessions.values()
                          if other is not session and now - other.last_seen <= PEER_IDLE],
                "chat": list(self.chat), "stage": session.stage, "shelf": session.shelf,
            }

    def sync_position(self, session, body, now):
        if body.get("resync") is True:
            return True
        x, y, z, yaw = position([body.get(k) for k in ("x", "y", "z", "yaw")])
        session.update_budget = min(2.4, session.update_budget + max(0.0, now - session.last_move))
        session.last_move = now
        distance = math.hypot(x - session.x, z - session.z)
        if not (-260 <= x <= 260 and -0.8 <= y <= 20 and -260 <= z <= 260):
            return True
        if distance > MAX_SPEED * session.update_budget or abs(y - session.y) > 8:
            return True
        session.update_budget -= distance / MAX_SPEED
        session.x, session.y, session.z, session.yaw = x, y, z, yaw % 360
        if now - session.last_save > 8:
            self.persist_position(session)
            session.last_save = now
        return False

    def send_chat(self, session, body, now):
        message = body.get("message")
        if not isinstance(message, str):
            raise ApiError(400, "Введите сообщение.")
        message = " ".join(message.split())
        if not 1 <= len(message) <= 200 or any(ord(ch) < 32 for ch in message):
            raise ApiError(400, "Сообщение должно содержать от 1 до 200 символов.")
        if now - session.last_chat < .75:
            raise ApiError(429, "Отправляйте сообщения реже.")
        session.last_chat = now
        self.chat_id += 1
        self.chat.append({"id": self.chat_id, "name": session.name, "message": message})
        return {"message": "Сообщение отправлено."}

    def work(self, session, action, now):
        if not isinstance(action, str) or action not in STATIONS:
            raise ApiError(400, "Эта операция доступна в автономной игре.")
        if now - session.last_work < .45:
            raise ApiError(429, "Дождитесь завершения действия.")
        if action != WORK_ORDER[session.stage]:
            raise ApiError(409, "Следующее действие: " + self.next_task(session))
        spots = STATIONS[action] if action != "shelf" else (STATIONS[action][session.shelf],)
        if all(math.hypot(session.x - x, session.z - z) > WORK_RADIUS for x, z in spots):
            raise ApiError(409, "Подойдите к нужной рабочей точке: " + self.next_task(session))
        session.last_work = now
        if action == "terminal":
            session.shelf = secrets.randbelow(3)
        with self.db() as db:
            if action == "belt":
                db.execute("UPDATE accounts SET cash = cash + ? WHERE name = ?",
                           (SHIFT_PAY, session.name))
            cash, = db.execute("SELECT cash FROM accounts WHERE name = ?", (session.name,)).fetchone()
        session.stage = (session.stage + 1) % len(WORK_ORDER)
        if action == "belt":
            session.shelf = -1
        message = WORK_STATUS[action]
        if action == "terminal":
            message += " Стеллаж №" + str(session.shelf + 1) + "."
        return {"cash": cash, "message": message, "stage": session.stage, "shelf": session.shelf}

    @staticmethod
    def next_task(session):
        labels = ("приём на работу", "раздевалка", "терминал",
                  f"стеллаж №{session.shelf + 1}", "упаковка", "конвейер")
        return labels[session.stage]


def handler_for(world):
    class Handler(BaseHTTPRequestHandler):
        def setup(self):
            self.request.settimeout(8)
            super().setup()

        def do_GET(self):
            if self.path == "/health":
                self.reply(200, {"message": "LIFE CRAFT RP", "protocol": 1})
            else:
                self.reply(404, {"message": "Неизвестный запрос."})

        def do_POST(self):
            try:
                length = int(self.headers.get("Content-Length", "-1"))
                if not 1 <= length <= 8192:
                    raise ApiError(400, "Некорректный размер запроса.")
                body = json.loads(self.rfile.read(length))
                if not isinstance(body, dict):
                    raise ApiError(400, "Некорректный запрос.")
                token = self.headers.get("Authorization", "").removeprefix("Bearer ")
                result = world.dispatch(self.path, body, token, self.client_address[0])
                self.reply(200, result)
            except ApiError as error:
                self.reply(error.code, {"message": str(error)})
            except (ValueError, UnicodeError, RecursionError):
                self.reply(400, {"message": "Некорректный запрос."})
            except (BrokenPipeError, ConnectionResetError, TimeoutError):
                return

        def reply(self, status, data):
            payload = json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(payload)

        def log_message(self, fmt, *args):
            if self.path != "/sync":
                super().log_message(fmt, *args)

    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", default=8080, type=int)
    parser.add_argument("--db", type=Path, default=Path(__file__).with_name("accounts.sqlite3"))
    args = parser.parse_args()
    world = World(args.db)
    with ThreadingHTTPServer((args.host, args.port), handler_for(world)) as server:
        print(f"LIFE CRAFT RP · http://{args.host}:{server.server_port}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
