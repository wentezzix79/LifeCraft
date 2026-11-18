using System;

namespace LifeCraftRP.Core
{
    public sealed class PropertyManager
    {
        public const int StarterApartmentRent = 4200;
        public const int RentalDays = 7;
        private readonly GameSession session;

        internal PropertyManager(GameSession game) { session = game; }

        public bool HasActiveRental => session.Read(state => state.HasActiveRental);

        public GameResult RentStarterApartment()
        {
            return session.Change(state =>
            {
                if (state.HasActiveRental)
                    return GameResult.Fail("У вас уже есть действующая аренда.");
                if (!EconomyManager.Debit(state, StarterApartmentRent))
                    return GameResult.Fail($"Для аренды нужно {StarterApartmentRent:N0} ₽.");
                state.ApartmentUntilUtcTicks = DateTime.UtcNow.AddDays(RentalDays).Ticks;
                return GameResult.Ok($"Квартира арендована на {RentalDays} дней.");
            });
        }
    }
}
