namespace LifeCraftRP.Core
{
    public sealed class CourierManager
    {
        public const int Payout = 900;
        private readonly GameSession session;
        private int stage;

        internal CourierManager(GameSession session)
        {
            this.session = session;
        }

        public int Stage
        {
            get { lock (session.Sync) return stage; }
        }

        public string Instructions
        {
            get
            {
                switch (Stage)
                {
                    case 1: return "Заберите посылку внутри магазина.";
                    case 2: return "Доставьте посылку в постамат рядом с банком LC.";
                    default: return "Терминал курьеров находится у магазина.";
                }
            }
        }

        public GameResult Visit(int station)
        {
            lock (session.Sync)
            {
                if (station != stage)
                    return GameResult.Fail(Instructions);
                if (stage < 2)
                {
                    stage++;
                    return GameResult.Ok(Instructions);
                }
                GameResult result = session.Change(state =>
                {
                    const int experience = 20;
                    if (state.Experience > int.MaxValue - experience ||
                        !EconomyManager.CreditCash(state, Payout))
                        return GameResult.Fail("Выплату не удалось провести.");
                    state.Experience += experience;
                    return GameResult.Ok("Доставка завершена. +900 ₽, +20 XP.");
                });
                if (result.Success)
                    stage = 0;
                return result;
            }
        }
    }
}
