using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_2021_1_OR_NEWER
using UnityEngine;
#else
using System.Runtime.Serialization.Json;
#endif

namespace LifeCraftRP.Core
{
    public sealed class SaveSystem
    {
        private readonly string path;
        private readonly string backup;

        public SaveSystem(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
                throw new ArgumentException("A save path is required.", nameof(savePath));

            path = Path.GetFullPath(savePath);
            backup = path + ".bak";
        }

        public PlayerState Load()
        {
            if (TryRead(path, out PlayerState saved) || TryRead(backup, out saved))
                return saved;

            return new PlayerState();
        }

        public GameResult Write(PlayerState state)
        {
            if (!IsValid(state))
                return GameResult.Fail("Состояние персонажа не прошло проверку сохранения.");

            string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
#if UNITY_2021_1_OR_NEWER
                    using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 1024, true))
                        writer.Write(JsonUtility.ToJson(state, true));
#else
                    new DataContractJsonSerializer(typeof(PlayerState)).WriteObject(stream, state);
#endif
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(pending, path, backup);
                else
                    File.Move(pending, path);

                return GameResult.Ok("Прогресс сохранён.");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                                          error is NotSupportedException)
            {
                return GameResult.Fail("Сохранение недоступно. Изменения не применены.");
            }
            finally
            {
                try { File.Delete(pending); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool TryRead(string file, out PlayerState state)
        {
            state = null;
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length == 0 || stream.Length > 262144)
                        return false;
#if UNITY_2021_1_OR_NEWER
                    using (var reader = new StreamReader(stream))
                        state = JsonUtility.FromJson<PlayerState>(reader.ReadToEnd());
#else
                    state = (PlayerState)new DataContractJsonSerializer(typeof(PlayerState)).ReadObject(stream);
#endif
                }

                return IsValid(state);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                                          error is ArgumentException || error is FormatException ||
                                          error is System.Runtime.Serialization.SerializationException)
            {
                return false;
            }
        }

        private static bool IsValid(PlayerState state)
        {
            if (state == null || state.Version != 1 || state.Backpack == null ||
                state.Cash < 0 || state.Bank < 0 || state.Coins < 0 ||
                state.Cash > GameSession.MoneyLimit || state.Bank > GameSession.MoneyLimit ||
                state.Coins > GameSession.MoneyLimit || state.Experience < 0 ||
                state.CompletedOrders < 0 || state.FuelLitres < 0 || state.FuelLitres > EconomyManager.CarTankLitres ||
                state.ApartmentUntilUtcTicks < 0 || state.ApartmentUntilUtcTicks > DateTime.MaxValue.Ticks ||
                state.KioskStock < 0 || state.KioskStock > 1000 ||
                state.KioskUnitPrice < 1 || state.KioskUnitPrice > 2000 || state.KioskSales < 0 ||
                state.Backpack.Count > InventorySystem.Capacity)
                return false;

            if (state.HasPosition &&
                (!GameSession.ValidCoordinate(state.PositionX) ||
                 !GameSession.ValidCoordinate(state.PositionY) ||
                 !GameSession.ValidCoordinate(state.PositionZ)))
                return false;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemStack stack in state.Backpack)
                if (stack == null || !InventorySystem.IsKnown(stack.Id) ||
                    !ids.Add(stack.Id) || stack.Count < 1 || stack.Count > InventorySystem.StackLimit)
                    return false;

            return true;
        }
    }
}
