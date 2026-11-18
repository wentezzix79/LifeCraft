using System;

namespace LifeCraftRP.Core
{
    public sealed class BusinessManager
    {
        public const int KioskPrice = 34000;
        public const int WholesalePrice = 32;
        private const int Capacity = 1000;
        private readonly GameSession session;

        internal BusinessManager(GameSession game) { session = game; }

        public GameResult BuyKiosk()
        {
            return session.Change(state =>
            {
                if (state.KioskOwned)
                    return GameResult.Fail("Киоск уже принадлежит вам.");
                if (!EconomyManager.Debit(state, KioskPrice))
                    return GameResult.Fail($"Для покупки киоска нужно {KioskPrice:N0} ₽.");
                state.KioskOwned = true;
                return GameResult.Ok("Киоск куплен. Закажите товар для начала торговли.");
            });
        }

        public GameResult Restock(int count)
        {
            return session.Change(state =>
            {
                if (!state.KioskOwned || count <= 0 || count > Capacity - state.KioskStock)
                    return GameResult.Fail("Проверьте право собственности и свободное место на складе.");
                int total = count * WholesalePrice;
                if (!EconomyManager.Debit(state, total))
                    return GameResult.Fail($"Для закупки нужно {total:N0} ₽.");
                state.KioskStock += count;
                return GameResult.Ok($"В киоск доставлено {count} единиц товара.");
            });
        }

        public GameResult DispatchOrder()
        {
            return session.Change(state =>
            {
                if (!state.KioskOwned || state.KioskStock < 1)
                    return GameResult.Fail("Для выдачи нужен действующий киоск и товар в наличии.");
                if (state.KioskUnitPrice > 165)
                    return GameResult.Fail("Покупатель отказался из-за цены выше 165 ₽.");
                int tax = Math.Max(1, state.KioskUnitPrice * 6 / 100);
                if (state.KioskSales == int.MaxValue ||
                    !EconomyManager.CreditCash(state, state.KioskUnitPrice - tax))
                    return GameResult.Fail("Продажу не удалось провести.");
                state.KioskStock--;
                state.KioskSales++;
                return GameResult.Ok($"Продано за {state.KioskUnitPrice:N0} ₽. Налог {tax:N0} ₽.");
            });
        }

        public GameResult SetUnitPrice(int price)
        {
            return session.Change(state =>
            {
                if (!state.KioskOwned || price < WholesalePrice || price > 2000)
                    return GameResult.Fail($"Допустимая цена от {WholesalePrice} до 2 000 ₽.");
                state.KioskUnitPrice = price;
                return GameResult.Ok($"Установлена цена {price:N0} ₽.");
            });
        }
    }
}
