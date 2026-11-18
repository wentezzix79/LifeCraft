using System;
using System.Collections.Generic;

namespace LifeCraftRP.Core
{
    [Serializable]
    public sealed class ItemStack
    {
        public string Id;
        public int Count;
    }

    [Serializable]
    public sealed class PlayerState
    {
        public int Version = 1;
        public int Cash = 25000;
        public int Bank;
        public int Coins;
        public int Experience;
        public int CompletedOrders;
        public bool CarOwned;
        public int FuelLitres = 30;
        public long ApartmentUntilUtcTicks;
        public bool KioskOwned;
        public int KioskStock;
        public int KioskUnitPrice = 80;
        public int KioskSales;
        public bool HasPosition;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public List<ItemStack> Backpack = new List<ItemStack>();

        public int Level => 1 + Experience / 100;
        public bool HasActiveRental => ApartmentUntilUtcTicks > DateTime.UtcNow.Ticks;

        internal PlayerState Copy()
        {
            var clone = (PlayerState)MemberwiseClone();
            clone.Backpack = new List<ItemStack>(Backpack.Count);
            foreach (ItemStack stack in Backpack)
                clone.Backpack.Add(new ItemStack { Id = stack.Id, Count = stack.Count });
            return clone;
        }
    }

    public readonly struct GameResult
    {
        public bool Success { get; }
        public string Message { get; }

        private GameResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static GameResult Ok(string message) => new GameResult(true, message);
        public static GameResult Fail(string message) => new GameResult(false, message);
    }

    public sealed class GameSession
    {
        internal const int MoneyLimit = 1_000_000_000;
        internal readonly object Sync = new object();
        private readonly SaveSystem saveSystem;
        private PlayerState state;
        public PlayerState State
        {
            get { lock (Sync) return state.Copy(); }
        }

        public EconomyManager Economy { get; }
        public InventorySystem Inventory { get; }
        public JobManager Jobs { get; }
        public CourierManager Courier { get; }
        public PropertyManager Property { get; }
        public BusinessManager Businesses { get; }

        public static GameSession Open(string savePath)
        {
            var store = new SaveSystem(savePath);
            return new GameSession(store, store.Load());
        }

        public static GameSession New()
        {
            return new GameSession(null, new PlayerState());
        }

        private GameSession(SaveSystem store, PlayerState player)
        {
            saveSystem = store;
            state = player;
            Economy = new EconomyManager(this);
            Inventory = new InventorySystem(this);
            Jobs = new JobManager(this);
            Courier = new CourierManager(this);
            Property = new PropertyManager(this);
            Businesses = new BusinessManager(this);
        }

        // A gameplay mutation only becomes visible after its snapshot is saved.
        internal GameResult Change(Func<PlayerState, GameResult> action)
        {
            lock (Sync)
            {
                PlayerState next = state.Copy();
                GameResult result = action(next);
                if (!result.Success)
                    return result;
                GameResult saved = SaveSnapshot(next);
                if (saved.Success)
                    state = next;
                return saved.Success ? result : saved;
            }
        }

        internal T Read<T>(Func<PlayerState, T> read)
        {
            lock (Sync)
                return read(state);
        }

        public GameResult SetPosition(float x, float y, float z)
        {
            if (!ValidCoordinate(x) || !ValidCoordinate(y) || !ValidCoordinate(z))
                return GameResult.Fail("Эту позицию нельзя сохранить.");

            lock (Sync)
            {
                state.HasPosition = true;
                state.PositionX = x;
                state.PositionY = y;
                state.PositionZ = z;
                return GameResult.Ok("Позиция обновлена.");
            }
        }

        internal static bool ValidCoordinate(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < 1_000_000f;

        public GameResult Save()
        {
            lock (Sync)
                return SaveSnapshot(state);
        }

        private GameResult SaveSnapshot(PlayerState value)
        {
            if (saveSystem == null)
                return GameResult.Ok("Прогресс сохранён.");

            return saveSystem.Write(value);
        }
    }
}
