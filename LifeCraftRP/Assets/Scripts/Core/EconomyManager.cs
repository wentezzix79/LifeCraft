using System;

namespace LifeCraftRP.Core
{
    public sealed class EconomyManager
    {
        public const int StarterCarPrice = 19000;
        public const int FuelPricePerLitre = 58;
        public const int CarTankLitres = 44;
        public const int BusFare = 40;
        private readonly GameSession session;

        internal EconomyManager(GameSession game) { session = game; }

        public GameResult Deposit(int amount)
        {
            return session.Change(state =>
            {
                if (amount <= 0 || state.Cash < amount || state.Bank > GameSession.MoneyLimit - amount)
                    return GameResult.Fail("Проверьте сумму и остаток наличных.");
                state.Cash -= amount;
                state.Bank += amount;
                return GameResult.Ok($"Внесено на счёт: {amount:N0} ₽.");
            });
        }

        public GameResult Withdraw(int amount)
        {
            return session.Change(state =>
            {
                if (amount <= 0 || state.Bank < amount || state.Cash > GameSession.MoneyLimit - amount)
                    return GameResult.Fail("Проверьте сумму и остаток на счёте.");
                state.Bank -= amount;
                state.Cash += amount;
                return GameResult.Ok($"Снято со счёта: {amount:N0} ₽.");
            });
        }

        public GameResult BuyStarterCar()
        {
            return session.Change(state =>
            {
                if (state.CarOwned)
                    return GameResult.Fail("Ключи от этой машины уже у вас.");
                if (!Debit(state, StarterCarPrice))
                    return GameResult.Fail($"Для покупки нужно {StarterCarPrice:N0} ₽.");
                state.CarOwned = true;
                state.FuelLitres = 30;
                return GameResult.Ok("Автомобиль куплен. Ключи добавлены в профиль.");
            });
        }

        internal GameResult PayBusFare(string routeNumber)
        {
            return session.Change(state => Debit(state, BusFare)
                ? GameResult.Ok($"Билет на автобус № {routeNumber} · {BusFare} ₽.")
                : GameResult.Fail($"Для проезда нужно {BusFare} ₽."));
        }

        public GameResult Refuel(int requestedLitres)
        {
            return session.Change(state =>
            {
                if (!state.CarOwned || requestedLitres <= 0 || requestedLitres > CarTankLitres)
                    return GameResult.Fail("Для заправки нужен автомобиль и объём от 1 до 50 л.");
                int litres = Math.Min(requestedLitres, CarTankLitres - state.FuelLitres);
                if (litres == 0)
                    return GameResult.Fail("Бак уже полон.");
                int price = litres * FuelPricePerLitre;
                if (!Debit(state, price))
                    return GameResult.Fail($"Для заправки нужно {price:N0} ₽.");
                state.FuelLitres += litres;
                return GameResult.Ok($"Заправлено {litres} л за {price:N0} ₽.");
            });
        }

        public GameResult RecordFuelUse(int litres)
        {
            return session.Change(state =>
            {
                if (!state.CarOwned || litres <= 0 || litres > state.FuelLitres)
                    return GameResult.Fail("В баке недостаточно топлива.");
                state.FuelLitres -= litres;
                return GameResult.Ok($"Остаток топлива: {state.FuelLitres} л.");
            });
        }

        internal static bool Debit(PlayerState state, int amount)
        {
            if (amount <= 0 || (long)state.Cash + state.Bank < amount)
                return false;
            int cashPaid = Math.Min(state.Cash, amount);
            state.Cash -= cashPaid;
            state.Bank -= amount - cashPaid;
            return true;
        }

        internal static bool CreditCash(PlayerState state, int amount)
        {
            if (amount <= 0 || state.Cash > GameSession.MoneyLimit - amount)
                return false;
            state.Cash += amount;
            return true;
        }
    }
}
