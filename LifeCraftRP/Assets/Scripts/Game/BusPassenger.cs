using LifeCraft.World;
using LifeCraftRP.Core;
using UnityEngine;

namespace LifeCraftRP
{
    internal sealed class BusPassenger
    {
        private const float BoardDistance = 3.1f;
        private readonly GameManager game;
        private OmskLiveBus atDoor;
        private bool exitRequested;
        private Vector3 boardingPoint;

        internal BusPassenger(GameManager game)
        {
            this.game = game;
            game.Network.BusExited += OnNetworkExit;
        }

        internal string Prompt
        {
            get
            {
                var network = game.Network;
                if (network.BusCommandPending)
                    return "Автобус · проверка билета сервером";
                var bus = game.Player.CurrentBus;
                if (bus != null)
                {
                    if (exitRequested || network.BusExitPending)
                        return $"Автобус № {bus.RouteNumber} · выход на остановке";
                    return $"Автобус № {bus.RouteNumber} · {(bus.DoorsOpen ? "Выйти" : "Запросить остановку")}";
                }
                return atDoor == null ? null : $"Сесть в автобус № {atDoor.RouteNumber} · {EconomyManager.BusFare} ₽";
            }
        }

        internal void Tick()
        {
            var actor = game.Player;
            var network = game.Network;
            if (game.Online && network.BusId.Length > 0 &&
                game.Buses.TryGetBus(network.BusId,out var onlineBus))
                actor.BoardBus(onlineBus,network.BusSeat);

            var bus = actor.CurrentBus;
            if (bus == null)
                exitRequested = false;
            else if (!game.Online && (!bus.Active || bus.TerminalStop || exitRequested && bus.DoorsOpen))
                TryLeave(bus);

            atDoor = null;
            if (!actor.IsDriving && !actor.IsRidingBus &&
                !network.IsConnecting && !network.BusCommandPending)
                game.Buses.TryGetBoardableBus(actor.Position,BoardDistance,out atDoor);
        }

        internal bool Use()
        {
            var player = game.Player;
            var network = game.Network;
            if (network.BusCommandPending)
                return true;
            var bus = player.CurrentBus;
            if (bus != null)
            {
                if (game.Online)
                {
                    if (!network.BusExitPending)
                        network.SendBusCommand("exit",bus.Id);
                }
                else
                {
                    bool justRequested = !exitRequested;
                    exitRequested = true;
                    if (bus.DoorsOpen)
                        TryLeave(bus);
                    else if (justRequested)
                        game.Notify("Автобус остановится на следующей остановке.");
                }
                return true;
            }
            if (atDoor == null)
                return false;
            if (player.Animation.Carrying)
                game.Notify("Завершите перенос груза перед посадкой.");
            else if (game.Online)
            {
                if (network.SupportsBuses)
                    network.SendBusCommand("board",atDoor.Id);
                else
                    game.Notify("Для посадки обновите сервер LIFE CRAFT.");
            }
            else
            {
                boardingPoint = player.Position;
                game.Session.SetPosition(boardingPoint.x,boardingPoint.y,boardingPoint.z);
                GameResult result = game.Session.Economy.PayBusFare(atDoor.RouteNumber);
                if (result.Success)
                    player.BoardBus(atDoor,0);
                game.Show(result);
            }
            return true;
        }

        private void TryLeave(OmskLiveBus bus)
        {
            if (!game.Player.TryGetBusExit(bus,out Vector3 position))
            {
                if (bus.Active)
                    return;
                position = boardingPoint;
            }
            game.Player.LeaveBus(position,bus.Rotation);
            exitRequested = false;
            game.SaveProgress();
            game.Notify("Вы вышли из автобуса.");
        }

        private void OnNetworkExit(Vector3 position, Quaternion rotation)
        {
            var player = game.Player;
            if (player.CurrentBus != null && player.TryGetBusExit(player.CurrentBus,out Vector3 curb) &&
                (curb-position).sqrMagnitude < 4f)
                position = curb;
            player.LeaveBus(position,rotation);
            exitRequested = false;
            game.Notify("Вы вышли из автобуса.");
        }
    }
}
