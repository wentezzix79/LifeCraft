using System;

namespace LifeCraftRP.Core
{
    public enum WarehouseStation
    {
        Employment, Locker, Terminal, Shelf, Packing, Conveyor
    }

    public enum WarehouseStage
    {
        NotHired, NeedUniform, Ready, FindItem, Scanned, Carrying, Packed
    }

    public sealed class JobManager
    {
        public const int ShelfCount = 3;
        public const int BasePayout = 1450;
        public const int ExperiencePerOrder = 35;
        private readonly GameSession session;
        private readonly Random random = new Random();
        private WarehouseStage stage;
        private int targetShelf = -1;

        public WarehouseStage Stage
        {
            get { lock (session.Sync) return stage; }
        }
        public int TargetShelf
        {
            get { lock (session.Sync) return targetShelf; }
        }
        public bool IsCarrying => Stage == WarehouseStage.Carrying || Stage == WarehouseStage.Packed;

        public string Instructions
        {
            get
            {
                lock (session.Sync)
                {
                    switch (stage)
                    {
                        case WarehouseStage.NeedUniform: return "Получите форму в раздевалке.";
                        case WarehouseStage.Ready: return "Возьмите заказ у терминала.";
                        case WarehouseStage.FindItem: return $"Найдите стеллаж №{targetShelf + 1} и отсканируйте коробку.";
                        case WarehouseStage.Scanned: return $"Поднимите коробку со стеллажа №{targetShelf + 1}.";
                        case WarehouseStage.Carrying: return "Отнесите коробку на упаковочный стол.";
                        case WarehouseStage.Packed: return "Поставьте упакованный заказ на конвейер.";
                        default: return "Устройтесь на склад в зоне приёма сотрудников.";
                    }
                }
            }
        }

        internal JobManager(GameSession game) { session = game; }

        public GameResult Use(WarehouseStation station, int shelfIndex = -1)
        {
            lock (session.Sync)
            {
                switch (station)
                {
                    case WarehouseStation.Employment:
                        if (stage != WarehouseStage.NotHired)
                            break;
                        stage = WarehouseStage.NeedUniform;
                        return GameResult.Ok("Смена началась. Получите форму в раздевалке.");
                    case WarehouseStation.Locker:
                        if (stage != WarehouseStage.NeedUniform)
                            break;
                        stage = WarehouseStage.Ready;
                        return GameResult.Ok("Форма и сканер получены.");
                    case WarehouseStation.Terminal:
                        if (stage != WarehouseStage.Ready)
                            break;
                        targetShelf = random.Next(ShelfCount);
                        stage = WarehouseStage.FindItem;
                        return GameResult.Ok($"Заказ получен. Товар лежит на стеллаже №{targetShelf + 1}.");
                    case WarehouseStation.Shelf:
                        if (stage != WarehouseStage.FindItem && stage != WarehouseStage.Scanned)
                            break;
                        if (shelfIndex != targetShelf)
                            return GameResult.Fail($"Нужна коробка со стеллажа №{targetShelf + 1}.");
                        if (stage == WarehouseStage.FindItem)
                        {
                            stage = WarehouseStage.Scanned;
                            return GameResult.Ok("Штрихкод совпал. Поднимите коробку.");
                        }
                        stage = WarehouseStage.Carrying;
                        return GameResult.Ok("Коробка у вас. Отнесите её к упаковочному столу.");
                    case WarehouseStation.Packing:
                        if (stage != WarehouseStage.Carrying)
                            break;
                        stage = WarehouseStage.Packed;
                        return GameResult.Ok("Заказ упакован. Отнесите его на конвейер.");
                    case WarehouseStation.Conveyor:
                        if (stage != WarehouseStage.Packed)
                            break;
                        return PayOrder();
                }

                return GameResult.Fail(Instructions);
            }
        }

        public GameResult EndShift()
        {
            lock (session.Sync)
            {
                if (stage == WarehouseStage.NotHired)
                    return GameResult.Fail("Вы ещё не начали смену.");
                stage = WarehouseStage.NotHired;
                targetShelf = -1;
                return GameResult.Ok("Смена завершена. Незаконченный заказ возвращён на склад.");
            }
        }

        private GameResult PayOrder()
        {
            GameResult result = session.Change(state =>
            {
                int amount = BasePayout + Math.Min(state.CompletedOrders / 5, 6) * 140;
                if (state.Experience > int.MaxValue - ExperiencePerOrder ||
                    state.CompletedOrders == int.MaxValue || !EconomyManager.CreditCash(state, amount))
                    return GameResult.Fail("Начисление пока невозможно.");
                state.Experience += ExperiencePerOrder;
                state.CompletedOrders++;
                return GameResult.Ok($"Заказ отправлен. +{amount:N0} ₽ и +{ExperiencePerOrder} опыта.");
            });
            if (result.Success)
            {
                stage = WarehouseStage.Ready;
                targetShelf = -1;
            }
            return result;
        }
    }
}
