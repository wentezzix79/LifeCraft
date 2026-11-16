import sqlite3

DB_NAME = "lifecraft.db"


def connect():
    return sqlite3.connect(DB_NAME)


def init_db():
    with connect() as db:
        db.execute("""
            CREATE TABLE IF NOT EXISTS players (
                user_id INTEGER PRIMARY KEY,
                username TEXT,
                money INTEGER DEFAULT 500,
                job TEXT DEFAULT '',
                xp INTEGER DEFAULT 0,
                level INTEGER DEFAULT 1
            )
        """)


def create_player(user_id, username):
    with connect() as db:
        db.execute("""
            INSERT OR IGNORE INTO players
            (user_id, username)
            VALUES (?, ?)
        """, (user_id, username))


def get_player(user_id):
    with connect() as db:
        return db.execute(
            "SELECT * FROM players WHERE user_id = ?",
            (user_id,)
        ).fetchone()


def set_job(user_id, job):
    with connect() as db:
        db.execute(
            "UPDATE players SET job = ? WHERE user_id = ?",
            (job, user_id)
        )


def add_reward(user_id, money, xp):
    with connect() as db:
        db.execute("""
            UPDATE players
            SET money = money + ?,
                xp = xp + ?,
                level = 1 + (xp + ?) / 100
            WHERE user_id = ?
        """, (money, xp, xp, user_id))
import sqlite3

DB_NAME = "lifecraft.db"


def connect():
    return sqlite3.connect(DB_NAME)


def init_db():
    with connect() as db:
        db.execute("""
            CREATE TABLE IF NOT EXISTS players (
                user_id INTEGER PRIMARY KEY,
                username TEXT,
                money INTEGER DEFAULT 500,
                job TEXT DEFAULT '',
                xp INTEGER DEFAULT 0,
                level INTEGER DEFAULT 1
            )
        """)


def create_player(user_id, username):
    with connect() as db:
        db.execute("""
            INSERT OR IGNORE INTO players
            (user_id, username)
            VALUES (?, ?)
        """, (user_id, username))


def get_player(user_id):
    with connect() as db:
        return db.execute(
            "SELECT * FROM players WHERE user_id = ?",
            (user_id,)
        ).fetchone()


def set_job(user_id, job):
    with connect() as db:
        db.execute(
            "UPDATE players SET job = ? WHERE user_id = ?",
            (job, user_id)
        )


def add_reward(user_id, money, xp):
    with connect() as db:
        db.execute("""
            UPDATE players
            SET money = money + ?,
                xp = xp + ?,
                level = 1 + (xp + ?) / 100
            WHERE user_id = ?
        """, (money, xp, xp, user_id))