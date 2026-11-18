using System;
using System.Collections.Generic;

namespace LifeCraftRP.Core
{
    public sealed class InventoryItem
    {
        public string Id { get; }
        public string Name { get; }
        public int Price { get; }
        public int Energy { get; }

        internal InventoryItem(string id, string name, int price, int energy)
        {
            Id = id;
            Name = name;
            Price = price;
            Energy = energy;
        }
    }

    public sealed class InventorySystem
    {
        public const int Capacity = 16;
        public const int StackLimit = 20;
        private static readonly IReadOnlyList<InventoryItem> catalog = Array.AsReadOnly(new[]
        {
            new InventoryItem("water", "Вода", 55, 20),
            new InventoryItem("snack", "Батончик", 95, 32),
            new InventoryItem("coffee", "Кофе", 140, 42),
            new InventoryItem("jacket", "Тёмная куртка", 1450, 0),
        });

        private readonly GameSession session;
        public static IReadOnlyList<InventoryItem> Catalog => catalog;
        public IReadOnlyList<ItemStack> Items => session.State.Backpack.AsReadOnly();

        internal InventorySystem(GameSession game) { session = game; }

        public static InventoryItem Find(string id)
        {
            foreach (InventoryItem item in catalog)
                if (string.Equals(item.Id, id, StringComparison.Ordinal))
                    return item;
            return null;
        }

        internal static bool IsKnown(string id) => Find(id) != null;

        public GameResult Buy(string id)
        {
            InventoryItem item = Find(id);
            if (item == null)
                return GameResult.Fail("Товар отсутствует в каталоге.");

            return session.Change(state =>
            {
                ItemStack stack = state.Backpack.Find(entry => entry.Id == id);
                if ((stack == null && state.Backpack.Count >= Capacity) ||
                    (stack != null && stack.Count >= StackLimit))
                    return GameResult.Fail("В инвентаре нет места для этого товара.");

                if (!EconomyManager.Debit(state, item.Price))
                    return GameResult.Fail("Недостаточно средств.");

                if (stack == null)
                    state.Backpack.Add(new ItemStack { Id = id, Count = 1 });
                else
                    stack.Count++;
                return GameResult.Ok($"{item.Name} добавлено в инвентарь.");
            });
        }

        public GameResult Use(string id)
        {
            InventoryItem item = Find(id);
            if (item == null || item.Energy <= 0)
                return GameResult.Fail("Этот предмет нельзя употребить.");

            return session.Change(state =>
            {
                ItemStack stack = state.Backpack.Find(entry => entry.Id == id);
                if (stack == null)
                    return GameResult.Fail("Этого предмета нет в инвентаре.");

                stack.Count--;
                if (stack.Count == 0)
                    state.Backpack.Remove(stack);
                return GameResult.Ok($"Использовано: {item.Name}.");
            });
        }
    }
}
