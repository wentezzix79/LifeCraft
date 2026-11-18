using System;
using System.IO;
using LifeCraft.World;
using LifeCraftRP.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraftRP
{
    [DefaultExecutionOrder(-120)]
    public sealed class GameManager : MonoBehaviour
    {
        public static readonly Vector3 Spawn = new Vector3(-5f,.25f,-33f);
        public static readonly Vector3 ShowroomParking = new Vector3(42f,.65f,-49f);
        public GameSession Session { get; private set; }
        public InteractionSystem Interactions { get; } = new InteractionSystem();
        public PlayerController Player { get; private set; }
        public VehicleManager Vehicles { get; private set; }
        public OnlineSession Network { get; private set; }
        public OmskWorld World { get; private set; }
        public bool Online => Network != null && Network.IsOnline;
        public int Hour => (int)(clockMinutes / 60f) % 24;
        public int Minute => (int)clockMinutes % 60;
        public int LastWorkShelf => Online ? Network.WorkShelf : Session.Jobs.TargetShelf;
        public string CurrentTask => Online ? OnlineTask(Network.WorkStage, Network.WorkShelf) :
            Session.Courier.Stage != 0 ? Session.Courier.Instructions : Session.Jobs.Instructions;
        private UIManager ui;
        private Camera worldCamera;
        private Light sun;
        private float clockMinutes = 9 * 60 + 42;
        private float saveTimer;
        private Vector3 beforeOnline;
        private int quality;
        private WeatherController weather;

        private void Awake()
        {
            Application.targetFrameRate = Application.isMobilePlatform ? 30 : 60;
            Session = GameSession.Open(Path.Combine(Application.persistentDataPath,"lifecraft-profile.json"));
            Vector3 spawn = Spawn;
            PlayerState state = Session.State;
            if (state.HasPosition && Mathf.Abs(state.PositionX)<20000 &&
                Mathf.Abs(state.PositionZ)<22000 && state.PositionY<30)
                spawn = new Vector3(state.PositionX,Mathf.Max(.25f,state.PositionY),state.PositionZ);

            Player = PlayerController.Create(spawn);
            CameraController camera = CameraController.Create(Player);
            worldCamera = camera.SceneCamera;
            World = new GameObject("Омск · потоковая карта").AddComponent<OmskWorld>();
            World.target = Player.transform;

            var site = new SiteBuilder(this);
            site.Build();
            VehicleController car = VehicleController.Create(ShowroomParking, Quaternion.Euler(0f,90f,0f));
            Vehicles = new VehicleManager(Player,Session,car);
            Network = new GameObject("Сеть LIFE CRAFT").AddComponent<OnlineSession>();
            Network.Configure(Player.transform);
            Network.Connected += OnConnected;
            Network.Disconnected += OnDisconnected;
            SetupLighting();
            weather = WeatherController.Create(Player.transform);
            ui = new UIManager(this);
            Network.StatusChanged += Notify;
            Network.ChatChanged += ui.RefreshChat;
            ApplyQuality(PlayerPrefs.GetInt("LifeCraft.Graphics",Application.isMobilePlatform ? 1 : 2));
            Notify("Добро пожаловать в Омск. Найдите склад LC Логистика.");
        }

        private void OnConnected()
        {
            Player.ApplyWorkUniform(Network.WorkStage >= 2);
            ui.RefreshModal();
        }

        private void OnDisconnected()
        {
            Player.ApplyWorkUniform(Session.Jobs.Stage >= WarehouseStage.Ready);
            Player.Animation.Carry(Session.Jobs.IsCarrying || Session.Courier.Stage == 2);
            Player.Teleport(beforeOnline);
            ui.RefreshModal();
        }

        public void Connect(string host, string name, string password, bool register)
        {
            if (Vehicles.IsDriving)
            {
                Notify("Выйдите из машины перед подключением.");
                return;
            }
            if (Online || Network.IsConnecting)
                return;
            beforeOnline = Player.transform.position;
            SaveProgress();
            if (register)
                Network.Register(host,name,password);
            else
                Network.LogIn(host,name,password);
        }

        private void SetupLighting()
        {
            var obj = new GameObject("Солнце Омска",typeof(Light));
            sun = obj.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .74f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0011f;
            RenderSettings.fogColor = new Color(.58f,.68f,.72f);
            SetLight();
        }

        public void ApplyQuality(int preset)
        {
            quality = Mathf.Clamp(preset,0,4);
            PlayerPrefs.SetInt("LifeCraft.Graphics", quality);
            World.SetViewDistance(quality <= 1);
            if (worldCamera != null)
                worldCamera.farClipPlane = new [] { 340f,490f,710f,970f,1350f }[quality];
            sun.shadows = quality == 0 ? LightShadows.None :
                quality < 3 ? LightShadows.Hard : LightShadows.Soft;
            QualitySettings.shadowDistance = new [] { 0f,34f,55f,100f,155f }[quality];
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
                pipeline.GetType().GetProperty("renderScale")?.SetValue(pipeline,
                    new [] { .63f,.79f,.92f,1f,1f }[quality]);
        }

        public int Quality => quality;
        public string Weather => weather != null ? weather.Name : "Ясно";

        public void ChangeWeather() => Notify("Погода: "+weather.Next()+".");

        private void SetLight()
        {
            float phase = (clockMinutes - 6*60) / 1440f * Mathf.PI * 2;
            float height = Mathf.Sin(phase);
            float daylight = Mathf.SmoothStep(0f,1f,Mathf.Clamp01((height+.12f)*2.2f));
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(-9f,height*56f),132f - height*31f,0f);
            sun.intensity = Mathf.Lerp(.13f,1.18f,daylight);
            sun.color = Color.Lerp(new Color(.57f,.68f,.96f),new Color(1f,.90f,.77f),daylight);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(.16f,.21f,.31f),
                new Color(.56f,.64f,.71f),daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(.14f,.15f,.19f),
                new Color(.45f,.48f,.45f),daylight);
            RenderSettings.ambientGroundColor = new Color(.22f,.24f,.23f);
            if(worldCamera != null)
                worldCamera.backgroundColor = Color.Lerp(new Color(.055f,.079f,.147f),
                    new Color(.51f,.67f,.75f),daylight);
        }

        private void LateUpdate()
        {
            if (ui == null)
                return;
            ActorInput input = Player.Input;
            if (input.PausePressed)
            {
                if (ui.IsOpen)
                    ui.Close();
                else
                    Open("pause");
            }
            else if (!ui.IsTyping && input.MenuPressed)
                Toggle("menu");
            else if (!ui.IsTyping && input.MapPressed)
                Toggle("map");
            else if (!ui.IsTyping && input.InventoryPressed)
                Toggle("inventory");
            else if (!ui.IsTyping && input.PhonePressed)
                Toggle("phone");

            Vector3 position = Player.transform.position;
            InteractionSystem.Site near = Vehicles.IsDriving ? null :
                Interactions.Find(position, Player.transform.forward);
            if (!ui.IsOpen)
            {
                if (input.VehiclePressed)
                    Show(Vehicles.Toggle(Online));
                else if (input.InteractPressed && near != null)
                    near.Use();
            }

            clockMinutes = (clockMinutes+Time.deltaTime*3f)%1440f;
            SetLight();
            Vehicles.Tick();
            if (Online)
            {
                Player.ApplyWorkUniform(Network.WorkStage >= 2);
                Player.Animation.Carry(Network.WorkStage == 4 || Network.WorkStage == 5);
            }
            ui.RefreshHud(near);
            saveTimer += Time.unscaledDeltaTime;
            if (saveTimer > 18f)
            {
                saveTimer = 0;
                SaveProgress();
            }
        }

        public void UseWarehouse(WarehouseStation station, int shelf = -1)
        {
            if (Online)
            {
                string action;
                switch(station)
                {
                    case WarehouseStation.Employment: action="employ"; break;
                    case WarehouseStation.Locker: action="locker"; break;
                    case WarehouseStation.Terminal: action="terminal"; break;
                    case WarehouseStation.Shelf: action="shelf"; break;
                    case WarehouseStation.Packing: action="pack"; break;
                    default: action="belt"; break;
                }
                Network.TryWork(action);
                Player.Animation.Work();
                return;
            }
            if (Session.Courier.Stage != 0)
            {
                Notify("Сначала завершите доставку.");
                return;
            }
            GameResult result = Session.Jobs.Use(station,shelf);
            Show(result);
            if (result.Success)
            {
                Player.Animation.Work();
                Player.Animation.Carry(Session.Jobs.IsCarrying);
                Player.ApplyWorkUniform(Session.Jobs.Stage >= WarehouseStage.Ready);
            }
        }

        public void UseCourier(int station)
        {
            if (Online || Network.IsConnecting)
            {
                Notify("Курьерские задания доступны в одиночной игре.");
                return;
            }
            if (Session.Jobs.Stage != WarehouseStage.NotHired)
            {
                Notify("Завершите смену на складе через приложение «Работа».");
                return;
            }
            GameResult result = Session.Courier.Visit(station);
            Show(result);
            if (result.Success)
            {
                Player.Animation.Work();
                Player.Animation.Carry(Session.Courier.Stage == 2);
            }
        }

        private static string OnlineTask(int stage,int shelf)
        {
            switch(stage)
            {
                case 1: return "Получите рабочую форму.";
                case 2: return "Возьмите заказ на терминале.";
                case 3: return "Заберите коробку · стеллаж "+(shelf+1);
                case 4: return "Отнесите заказ к упаковочному столу.";
                case 5: return "Перенесите заказ на конвейер.";
                default: return "Устройтесь на склад LC Логистика.";
            }
        }

        public void Toggle(string name)
        {
            if(ui.Mode == name)
                ui.Close();
            else
                Open(name);
        }

        public void Open(string mode) => ui.Open(mode);

        public void OpenService(string mode)
        {
            if (Online)
            {
                Notify("Эта услуга доступна в одиночной игре.");
                return;
            }
            Open(mode);
        }

        public void Show(GameResult result) => Notify(result.Message);
        public void Notify(string message) => ui?.Toast(message);

        public void SaveProgress()
        {
            if (Online || Network == null || Network.IsConnecting || Vehicles.IsDriving)
                return;
            Vector3 p = Player.transform.position;
            if (p.y > -.5f)
                Session.SetPosition(p.x,p.y,p.z);
            GameResult saved = Session.Save();
            if (!saved.Success)
                Show(saved);
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause && ui != null)
                SaveProgress();
        }

        private void OnApplicationQuit()
        {
            if (ui != null)
                SaveProgress();
        }
    }
}
