import asyncio
import random
from aiogram import Bot, Dispatcher, F
from aiogram.filters import Command
from aiogram.types import (
Message,
CallbackQuery,
ReplyKeyboardMarkup,
KeyboardButton,
InlineKeyboardMarkup,
InlineKeyboardButton,
WebAppInfo
)
from config import BOT_TOKEN
import database

WEBAPP_URL = "https://wentezzix79.github.io/LifeCraft/"

bot = Bot(token=BOT_TOKEN)
dp = Dispatcher()
JOBS = {
"Строитель": (100, 200),
"Таксист": (120, 250),
"Шахтёр": (150, 300),
"Курьер": (100, 180),
"Фермер": (110, 220),
"Рыбак": (130, 240)
}
def main_keyboard():
    return ReplyKeyboardMarkup(
        keyboard=[
            [
                KeyboardButton(
                    text="🎮 Открыть LifeCraft",
                    web_app=WebAppInfo(url=WEBAPP_URL)
                )
            ],
            [
                KeyboardButton(text="👤 Профиль"),
                KeyboardButton(text="💼 Работа")
            ],
            [
                KeyboardButton(text="💰 Заработать"),
                KeyboardButton(text="❓ Помощь")
            ]
        ],
        resize_keyboard=True
    )

def jobs_keyboard():
    buttons = []

    for job in JOBS:
        buttons.append([
            InlineKeyboardButton(
                text=job,
                callback_data=f"job:{job}"
            )
        ])

    return InlineKeyboardMarkup(
        inline_keyboard=buttons
    )


@dp.message(Command("start"))
async def start(message: Message):
    user = message.from_user
    database.create_player(
        user.id,
        user.username or user.first_name
    )

    await message.answer(
        "🌆 Добро пожаловать в LifeCraft!\n\n"
        "Здесь ты можешь построить собственную жизнь:\n"
        "💼 Выбирать профессию\n"
        "💰 Зарабатывать деньги\n"
        "⭐ Получать опыт и уровни\n"
        "🚗 Покупать транспорт и недвижимость\n\n"
        "Начни свою историю!",
        reply_markup=main_keyboard()
    )


@dp.message(Command("help"))
@dp.message(F.text == "❓ Помощь")
async def help_command(message: Message):
    await message.answer(
        "❓ Помощь LifeCraft\n\n"
        "/start — главное меню\n"
        "/help — помощь\n\n"
        "Выбери профессию и начинай зарабатывать!"
    )


@dp.message(F.text == "👤 Профиль")
async def profile(message: Message):
    player = database.get_player(message.from_user.id)
    if player is None:
        database.create_player(
            message.from_user.id,
            message.from_user.username or message.from_user.first_name
        )
        player = database.get_player(message.from_user.id)

    user_id, username, money, job, xp, level = player

    await message.answer(
        "👤 ТВОЙ ПРОФИЛЬ\n\n"
        f"🆔 ID: {user_id}\n"
        f"👤 Имя: {username}\n"
        f"💰 Баланс: {money} ₽\n"
        f"💼 Профессия: {job or 'Не выбрана'}\n"
        f"⭐ Опыт: {xp}\n"
        f"🏆 Уровень: {level}"
    )


@dp.message(F.text == "💼 Работа")
async def work_menu(message: Message):
    await message.answer(
        "💼 Выбери свою профессию:",
        reply_markup=jobs_keyboard()
    )


@dp.callback_query(F.data.startswith("job:"))
async def choose_job(callback: CallbackQuery):
    job = callback.data.split(":", 1)[1]
    if job not in JOBS:
        await callback.answer("Такой профессии нет", show_alert=True)
        return

    database.create_player(
        callback.from_user.id,
        callback.from_user.username or callback.from_user.first_name
    )

    database.set_job(callback.from_user.id, job)

    await callback.message.answer(
        f"✅ Ты выбрал профессию: {job}!\n\n"
        "Теперь можешь зарабатывать деньги."
    )

    await callback.answer()


@dp.message(F.text == "💰 Заработать")
async def earn_money(message: Message):
    player = database.get_player(message.from_user.id)

    if player is None:
        database.create_player(
            message.from_user.id,
            message.from_user.username or message.from_user.first_name
        )
        player = database.get_player(message.from_user.id)

    job = player[3]

    if not job:
        await message.answer(
            "❌ Сначала выбери профессию в разделе «Работа»."
        )
        return

    if job not in JOBS:
        await message.answer("❌ Неизвестная профессия.")
        return

    min_reward, max_reward = JOBS[job]

    reward = random.randint(min_reward, max_reward)
    xp = random.randint(10, 25)

    database.add_reward(
        message.from_user.id,
        reward,
        xp
    )

    await message.answer(
        "💰 Работа выполнена!\n\n"
        f"💵 Заработано: +{reward} ₽\n"
        f"⭐ Получено опыта: +{xp} XP"
    )


async def main():
    database.init_db()

    print("LifeCraft запущен!")

    await dp.start_polling(bot)


if __name__ == "__main__":
    asyncio.run(main())