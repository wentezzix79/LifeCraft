using LifeCraftRP.Core;
using UnityEngine;

namespace LifeCraftRP
{
    public sealed class VehicleManager
    {
        private readonly PlayerController player;
        private readonly GameSession session;
        private readonly VehicleController car;


        public VehicleManager(PlayerController player, GameSession session, VehicleController car)
        {
            this.player = player;
            this.session = session;
            this.car = car;
            car.SetFuel(session.State.FuelLitres);
        }

        public bool IsDriving => player.CurrentVehicle == car;
        public Vector3 Position => car.transform.position;
        public float Speed => car.SpeedKmh;
        public float Fuel => car.FuelLiters;
        public bool IsNear => (player.transform.position - car.transform.position).sqrMagnitude < 4.25f * 4.25f;

        public GameResult Toggle(bool online)
        {
            if (IsDriving)
            {
                if (!player.ExitVehicle())
                    return GameResult.Fail("Остановите машину и проверьте место возле двери.");
                return GameResult.Ok("Вы вышли из машины.");
            }

            if (!IsNear)
                return GameResult.Fail("Подойдите к автомобилю у салона.");
            if (online)
                return GameResult.Fail("Транспорт доступен в одиночной игре.");
            PlayerState state = session.State;
            if (!state.CarOwned)
                return GameResult.Fail("Купите автомобиль у стойки автосалона.");
            if (car.FuelLiters <= 0f)
                return GameResult.Fail("Бак пуст. Заправьте автомобиль на АЗС.");

            car.Drivable = true;
            return player.TryEnter(car) ?
                GameResult.Ok("Волна 1.5 · Space — ручной тормоз.") :
                GameResult.Fail("Подойдите к водительской двери.");
        }

        public GameResult Refuel(int litres)
        {
            int previous = session.State.FuelLitres;
            GameResult result = session.Economy.Refuel(litres);
            if (result.Success)
                car.Refuel(session.State.FuelLitres - previous);
            return result;
        }

        public void Tick()
        {
            PlayerState state = session.State;
            car.Drivable = state.CarOwned;
            int used = state.FuelLitres - Mathf.CeilToInt(car.FuelLiters);
            if (used > 0)
                session.Economy.RecordFuelUse(used);
        }
    }
}
