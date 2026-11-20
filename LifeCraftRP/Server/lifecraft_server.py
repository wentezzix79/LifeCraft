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
import struct
import threading
import time
from bisect import bisect_right
from collections import deque
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

START_CASH = 25000
SHIFT_PAY = 1450
SPAWN = (-5.0, 0.0, -33.0, 0.0)
SESSION_IDLE = 15 * 60
PEER_IDLE = 6.0
MAX_SPEED = 10.5
WORK_RADIUS = 4.0
BUS_FARE = 40
BUS_BOARD_RADIUS = 4.5
BUS_DATA = Path(__file__).resolve().parents[1] / "Assets/Resources/Omsk/bus-routes.json"
BUS_SEATS = (
    (.61, .36, .65), (.61, .36, -.80), (.61, .36, -2.25), (.61, .36, -4.05),
    (-.67, .36, 2.45), (-.67, .36, .74), (-.67, .36, -.97), (-.67, .36, -2.94),
)
WORLD_X = (-15750, 16250)
WORLD_Z = (-17250, 22250)
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
BUS_REQUEST_ID = re.compile(r"[0-9a-fA-F]{32}")


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
    last_bus_action: float = -100.0
    stage: int = 0
    shelf: int = -1
    bus_id: str = ""
    bus_seat: int = -1
    bus_exit_pending: bool = False
    bus_exit_time: float = 0.0
    bus_exit_stop: int = 0
    bus_needs_resync: bool = False
    bus_receipts: dict = field(default_factory=dict)

    def public(self):
        state = {"name": self.name, "x": self.x, "y": self.y, "z": self.z, "yaw": self.yaw}
        if self.bus_id:
            state["busId"] = self.bus_id
            state["busSeat"] = self.bus_seat
        return state


def position(values):
    if any(type(v) not in (int, float) or abs(v) > 25000 or not math.isfinite(v)
           for v in values):
        raise ApiError(400, "Некорректная позиция.")
    return [float(v) for v in values]


@dataclass(frozen=True)
class BusPose:
    x: float
    z: float
    yaw: float

    def offset(self, x, y, z):
        angle = math.radians(self.yaw)
        sin, cos = math.sin(angle), math.cos(angle)
        return self.x + cos*x + sin*z, .03 + y, self.z - sin*x + cos*z, self.yaw

    def curb(self):
        return self.offset(1.95, .08, 2)

    def seat(self, index):
        return self.offset(*BUS_SEATS[index])


@dataclass(frozen=True)
class BusStop:
    name: str
    index: int
    arrival: float
    departure: float
    pose: BusPose


@dataclass(frozen=True)
class BusSample:
    trip_start: float
    stop: int
    doors_open: bool
    pose: BusPose


class BusRoute:
    """Shared timetable rules with the Unity OmskBusService."""

    def __init__(self, raw, names):
        self.id, self.number = raw["id"], raw["number"]
        self.points = tuple(struct.unpack("<ff", struct.pack("<ff", p["x"], p["z"]))
                            for p in raw["waypoints"])
        if len(self.points) < 2 or not all(
                math.isfinite(x) and math.isfinite(z) for x, z in self.points):
            raise ValueError("Invalid bus waypoints")
        self.distances = [0.0]
        for (x0, z0), (x1, z1) in zip(self.points, self.points[1:]):
            dx, dz = x1 - x0, z1 - z0
            self.distances.append(self.distances[-1] + math.sqrt(dx*dx + dz*dz))
        raw_stops = raw["stops"]
        if len(raw_stops) < 2:
            raise ValueError("A bus route must contain two stops")
        first = raw_stops[0]["pathIndex"]
        if first == raw_stops[-1]["pathIndex"]:
            raise ValueError("Bus route must advance along the path")
        previous = first
        arrival = 0.0
        self.stops = []
        for n, raw_stop in enumerate(raw_stops):
            index = raw_stop["pathIndex"]
            if not 0 <= previous <= index < len(self.points):
                raise ValueError("Bus stop order is invalid")
            nominal = (self.distances[index] - self.distances[previous]) / 10
            arrival += nominal + min(4, nominal)
            departure = arrival + (24 if n == len(raw_stops) - 1 else 12)
            pose = self.stop_pose(index, first)
            self.stops.append(BusStop(names[raw_stop["id"]], index, arrival, departure, pose))
            arrival, previous = departure, index
        self.arrivals = tuple(stop.arrival for stop in self.stops)
        self.duration = self.stops[-1].departure
        self.cycle = self.duration + 48
        self.fleet = max(1, math.ceil(self.cycle / 360))
        self.offset = 17
        for ch in self.id:
            self.offset = (self.offset * 31 + ord(ch)) % 86400

    @staticmethod
    def pose(x, z, dx, dz):
        length = math.hypot(dx, dz)
        if length == 0:
            return BusPose(x + 1.25, z, 0)
        yaw = math.degrees(math.atan2(dx, dz)) % 360
        return BusPose(x + 1.25*dz/length, z - 1.25*dx/length, yaw)

    def stop_pose(self, index, first):
        x, z = self.points[index]
        if index != first:
            for i in range(index - 1, -1, -1):
                px, pz = self.points[i]
                if (px, pz) != (x, z):
                    return self.pose(x, z, x - px, z - pz)
        for i in range(index + 1, len(self.points)):
            px, pz = self.points[i]
            if (px, pz) != (x, z):
                return self.pose(x, z, px - x, pz - z)
        return self.pose(x, z, 0, 0)

    def point_at(self, distance):
        i = bisect_right(self.distances, distance) - 1
        ax, az = self.points[i]
        if i == len(self.points) - 1:
            return ax, az
        bx, bz = self.points[i + 1]
        blend = (distance - self.distances[i]) / (self.distances[i + 1] - self.distances[i])
        return ax + (bx - ax)*blend, az + (bz - az)*blend

    def sample(self, slot, now):
        phase = (now + self.offset + slot * self.cycle / self.fleet) % self.cycle
        if phase >= self.duration:
            return None
        i = bisect_right(self.arrivals, phase) - 1
        here = self.stops[i]
        if phase < here.departure:
            return BusSample(now - phase, i, True, here.pose)
        there = self.stops[i + 1]
        t = phase - here.departure
        travel = there.arrival - here.departure
        ramp = min(4, travel / 2)
        area = travel - ramp
        if t <= ramp:
            easing = .5*t*t/ramp
        elif t <= travel - ramp:
            easing = t - ramp/2
        else:
            easing = area - .5*(travel - t)**2/ramp
        start, end = self.distances[here.index], self.distances[there.index]
        distance = start + (end - start)*easing/area
        x, z = self.point_at(distance)
        ax, az = self.point_at(max(start, distance - 2))
        bx, bz = self.point_at(min(end, distance + 2))
        dx, dz = bx - ax, bz - az
        if dx*dx + dz*dz <= .000001:
            yaw = math.radians(here.pose.yaw)
            dx, dz = math.sin(yaw), math.cos(yaw)
        return BusSample(now - phase, i, False, self.pose(x, z, dx, dz))


class BusNetwork:
    def __init__(self, path):
        self.routes = {}
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
            names = {stop["id"]: stop["name"] for stop in raw["stops"]}
            routes = (BusRoute(r, names) for r in raw["routes"])
            self.routes = {route.id: route for route in routes}
        except (OSError, ValueError, KeyError, TypeError, OverflowError, struct.error):
            # Keep the existing online services usable with an older source bundle.
            pass
        self.clock_origin = time.time() - time.monotonic()

    def sample(self, bus_id, unix):
        route_id, sep, index = bus_id.rpartition(":")
        route = self.routes.get(route_id)
        if not sep or route is None or not index.isascii() or not index.isdecimal():
            return None, None
        i = int(index)
        if str(i) != index or i >= route.fleet:
            return None, None
        return route, route.sample(i, unix)


class World:
    def __init__(self, db_path, bus_data=BUS_DATA):
        self.db_path = db_path
        self.bus_network = BusNetwork(bus_data)
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
                    self.save_departing(session, now)
                    del self.sessions[token]
            with self.db() as db:
                cash, x, y, z, yaw = db.execute(
                    "SELECT cash,x,y,z,yaw FROM accounts WHERE name=?", (row[0],)).fetchone()
            token = secrets.token_urlsafe(32)
            session = Session(row[0], x, y, z, yaw, now, now)
            self.sessions[token] = session
            return {
                "token": token, "cash": cash, "self": session.public(), "stage": 0, "shelf": -1,
                **self.bus_status(session, time.monotonic() + self.bus_network.clock_origin),
                "message": f"Вы вошли как {session.name}.",
            }

    def persist_position(self, session):
        with self.db() as db:
            db.execute("UPDATE accounts SET x=?,y=?,z=?,yaw=? WHERE name=?",
                       (session.x, session.y, session.z, session.yaw, session.name))

    def prune(self, now):
        for token, session in tuple(self.sessions.items()):
            idle = now - session.last_seen
            if idle > SESSION_IDLE:
                self.save_departing(session, now)
                del self.sessions[token]
            elif session.bus_id and idle > PEER_IDLE:
                self.save_departing(session, now)
                session.bus_needs_resync = True

    def dispatch(self, route, body, token, ip):
        if route in ("/register", "/login"):
            return self.authenticate(route, body, ip)
        if route not in ("/sync", "/work", "/chat", "/logout", "/bus"):
            raise ApiError(404, "Неизвестный запрос.")
        with self.lock:
            now = time.monotonic()
            unix = now + self.bus_network.clock_origin
            self.prune(now)
            session = self.sessions.get(token)
            if session is None:
                raise ApiError(401, "Сессия завершена. Войдите снова.")
            session.last_seen = now
            if route == "/logout":
                self.save_departing(session, now)
                del self.sessions[token]
                return {"message": "Вы вышли из сети."}
            self.advance_bus(session, now, unix)
            if route == "/chat":
                return self.send_chat(session, body, now)
            if route == "/work":
                if session.bus_id:
                    raise ApiError(409, "Выйдите из автобуса на остановке.")
                return self.work(session, body.get("action"), now)
            if route == "/bus":
                return self.use_bus(session, body, now, unix)
            corrected = session.bus_needs_resync
            session.bus_needs_resync = False
            if not corrected and not session.bus_id:
                corrected = self.sync_position(session, body, now)
            peers = []
            for other in self.sessions.values():
                if other is not session and now - other.last_seen <= PEER_IDLE:
                    self.advance_bus(other, now, unix)
                    peers.append(other.public())
            with self.db() as db:
                cash, = db.execute("SELECT cash FROM accounts WHERE name=?", (session.name,)).fetchone()
            return {
                "cash": cash, "self": session.public(), "corrected": corrected,
                "peers": peers,
                "chat": list(self.chat), "stage": session.stage, "shelf": session.shelf,
                **self.bus_status(session, unix),
            }

    def sync_position(self, session, body, now):
        if body.get("resync") is True:
            return True
        x, y, z, yaw = position([body.get(k) for k in ("x", "y", "z", "yaw")])
        session.update_budget = min(2.4, session.update_budget + max(0.0, now - session.last_move))
        session.last_move = now
        distance = math.hypot(x - session.x, z - session.z)
        x_bound, z_bound = (WORLD_X, WORLD_Z) if self.bus_network.routes else ((-260, 260), (-260, 260))
        if not (x_bound[0] <= x <= x_bound[1] and -0.8 <= y <= 20 and z_bound[0] <= z <= z_bound[1]):
            return True
        if distance > MAX_SPEED * session.update_budget or abs(y - session.y) > 8:
            return True
        session.update_budget -= distance / MAX_SPEED
        session.x, session.y, session.z, session.yaw = x, y, z, yaw % 360
        if now - session.last_save > 8:
            self.persist_position(session)
            session.last_save = now
        return False

    def bus_status(self, session, unix):
        return {
            "busTime": unix if self.bus_network.routes else 0,
            "busId": session.bus_id, "busSeat": session.bus_seat,
            "busExitPending": session.bus_exit_pending,
        }

    def save_departing(self, session, now):
        if session.bus_id:
            unix = now + self.bus_network.clock_origin
            route, bus = self.bus_network.sample(session.bus_id, unix)
            stop = session.bus_exit_stop
            if bus is not None and unix < session.bus_exit_time:
                stop = bus.stop
            self.leave_bus(session, route.stops[stop], now)
        self.persist_position(session)

    @staticmethod
    def leave_bus(session, stop, now):
        session.x, session.y, session.z, session.yaw = stop.pose.curb()
        session.bus_id = ""
        session.bus_seat = -1
        session.bus_exit_pending = False
        session.bus_exit_time = 0.0
        session.bus_exit_stop = 0
        session.last_move = now
        session.update_budget = 1.2

    def advance_bus(self, session, now, unix):
        if not session.bus_id:
            return
        route, bus = self.bus_network.sample(session.bus_id, unix)
        if unix >= session.bus_exit_time or bus is None:
            self.leave_bus(session, route.stops[session.bus_exit_stop], now)
            self.persist_position(session)
            session.bus_needs_resync = True
        else:
            session.x, session.y, session.z, session.yaw = bus.pose.seat(session.bus_seat)

    def use_bus(self, session, body, now, unix):
        if not self.bus_network.routes:
            raise ApiError(503, "Данные маршрутов автобусов недоступны.")
        action, bus_id, request_id = (body.get(k) for k in ("action", "busId", "requestId"))
        if action not in ("board", "exit") or not isinstance(bus_id, str) or not 1 <= len(bus_id) <= 100:
            raise ApiError(400, "Выберите автобус.")
        if not isinstance(request_id, str) or not BUS_REQUEST_ID.fullmatch(request_id):
            raise ApiError(400, "Некорректный номер операции.")
        request_id = request_id.lower()
        previous = session.bus_receipts.get(request_id)
        if previous:
            if previous[:2] != (action, bus_id):
                raise ApiError(409, "Номер операции уже использован.")
            message = previous[2]
            corrected = not session.bus_id
        else:
            if now - session.last_bus_action < .35:
                raise ApiError(429, "Дождитесь завершения действия.")
            session.last_bus_action = now
            if action == "board":
                message = self.board_bus(session, bus_id, now, unix)
                corrected = False
            else:
                message, corrected = self.request_bus_exit(session, bus_id, now, unix)
            session.bus_receipts[request_id] = (action, bus_id, message)
            if len(session.bus_receipts) > 64:
                del session.bus_receipts[next(iter(session.bus_receipts))]
        with self.db() as db:
            cash, = db.execute("SELECT cash FROM accounts WHERE name=?", (session.name,)).fetchone()
        return {
            "cash": cash, "self": session.public(), "corrected": corrected, "message": message,
            "stage": session.stage, "shelf": session.shelf,
            **self.bus_status(session, unix),
        }

    def board_bus(self, session, bus_id, now, unix):
        if session.bus_id:
            raise ApiError(409, "Вы уже едете в автобусе.")
        if session.stage in (4, 5):
            raise ApiError(409, "Завершите перенос заказа перед посадкой.")
        route, bus = self.bus_network.sample(bus_id, unix)
        if bus is None or not bus.doors_open or bus.stop == len(route.stops) - 1:
            raise ApiError(409, "Дождитесь автобуса с открытыми дверями.")
        x, y, z, yaw = bus.pose.curb()
        if math.hypot(x - session.x, z - session.z) > BUS_BOARD_RADIUS or abs(y - session.y) > 1.8:
            raise ApiError(409, "Подойдите к двери автобуса.")
        occupied = set()
        for peer in self.sessions.values():
            if peer is not session and peer.bus_id == bus_id:
                self.advance_bus(peer, now, unix)
                if peer.bus_id == bus_id:
                    occupied.add(peer.bus_seat)
        seat = next((i for i in range(len(BUS_SEATS)) if i not in occupied), None)
        if seat is None:
            raise ApiError(409, "В автобусе нет свободных мест.")
        with self.db() as db:
            debit = db.execute(
                "UPDATE accounts SET cash=cash-?,x=?,y=?,z=?,yaw=? WHERE name=? AND cash>=?",
                (BUS_FARE, x, y, z, yaw, session.name, BUS_FARE))
            if debit.rowcount != 1:
                raise ApiError(409, f"Для проезда нужно {BUS_FARE} ₽ наличными.")
        session.bus_id = bus_id
        session.bus_seat = seat
        session.bus_exit_stop = len(route.stops) - 1
        session.bus_exit_time = bus.trip_start + route.stops[-1].arrival
        session.bus_needs_resync = False
        session.x, session.y, session.z, session.yaw = bus.pose.seat(seat)
        return f"Автобус № {route.number} · проезд оплачен, {BUS_FARE} ₽."

    def request_bus_exit(self, session, bus_id, now, unix):
        if not session.bus_id or session.bus_id != bus_id:
            raise ApiError(409, "Вы не едете в этом автобусе.")
        route, bus = self.bus_network.sample(bus_id, unix)
        if bus.doors_open:
            stop = route.stops[bus.stop]
            self.leave_bus(session, stop, now)
            self.persist_position(session)
            return f"Вы вышли на остановке «{stop.name}».", True
        next_index = bus.stop + 1
        stop = route.stops[next_index]
        session.bus_exit_pending = True
        session.bus_exit_time = bus.trip_start + stop.arrival
        session.bus_exit_stop = next_index
        return f"Выход на остановке «{stop.name}».", False

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
