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
        private Material skybox;
        private Material previousSkybox;
        private float clockMinutes = 9 * 60 + 42;
        private float saveTimer;
        private Vector3 beforeOnline;
        private int quality;
        private WeatherController weather;

        private readonly struct VisualProfile
        {
            public readonly float ViewDistance, ShadowDistance;
            public readonly int DesktopSamples, MobileSamples, Cascades;
            public readonly ShadowResolution ShadowResolution;

            public VisualProfile(float viewDistance,float shadowDistance,int desktopSamples,
                int mobileSamples,int cascades,ShadowResolution shadowResolution)
            {
                ViewDistance = viewDistance;
                ShadowDistance = shadowDistance;
                DesktopSamples = desktopSamples;
                MobileSamples = mobileSamples;
                Cascades = cascades;
                ShadowResolution = shadowResolution;
            }
        }

        private static readonly VisualProfile[] VisualProfiles =
        {
            new VisualProfile(370f,0f,1,1,1,ShadowResolution.Low),
            new VisualProfile(510f,42f,2,1,1,ShadowResolution.Medium),
            new VisualProfile(745f,72f,4,2,2,ShadowResolution.High),
            new VisualProfile(1020f,116f,4,2,2,ShadowResolution.High),
            new VisualProfile(1410f,166f,8,4,4,ShadowResolution.VeryHigh),
        };

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
            sun.shadowBias = .045f;
            sun.shadowNormalBias = .34f;
            sun.shadowNearPlane = .20f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0011f;

            Shader skyShader = Shader.Find("Skybox/Procedural");
            previousSkybox = RenderSettings.skybox;
            if (skyShader != null && skyShader.isSupported)
            {
                skybox = new Material(skyShader) { name = "LC · Атмосфера Омска" };
                skybox.SetFloat("_SunSize",.028f);
                skybox.SetFloat("_AtmosphereThickness",.92f);
                skybox.SetFloat("_SunDisk",2f);
                skybox.DisableKeyword("_SUNDISK_NONE");
                skybox.DisableKeyword("_SUNDISK_SIMPLE");
                skybox.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                RenderSettings.skybox = skybox;
            }
            SetLight();
        }

        public void ApplyQuality(int preset)
        {
            quality = Mathf.Clamp(preset,0,VisualProfiles.Length-1);
            VisualProfile profile = VisualProfiles[quality];
            PlayerPrefs.SetInt("LifeCraft.Graphics",quality);
            World.SetViewDistance(quality <= 1);

            bool mobile = Application.isMobilePlatform;
            bool hdr = !mobile && quality >= 3;
            int requested = mobile ? profile.MobileSamples : profile.DesktopSamples;
            int samples = SupportedSamples(requested,hdr);
            if (hdr && samples == 1)
            {
                hdr = false;
                samples = SupportedSamples(requested,false);
            }
            if (worldCamera != null)
            {
                worldCamera.farClipPlane = profile.ViewDistance;
                worldCamera.allowDynamicResolution = false;
                worldCamera.allowMSAA = samples > 1;
                worldCamera.allowHDR = hdr;
                worldCamera.clearFlags = skybox != null && quality >= 1
                    ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            }
            sun.shadows = quality == 0 ? LightShadows.None :
                quality == 1 ? LightShadows.Hard : LightShadows.Soft;
            QualitySettings.shadowDistance = profile.ShadowDistance;
            QualitySettings.shadowCascades = profile.Cascades > 1 ? profile.Cascades : 0;
            QualitySettings.shadowResolution = profile.ShadowResolution;
            QualitySettings.anisotropicFiltering = quality <= 1
                ? AnisotropicFiltering.Enable : AnisotropicFiltering.ForceEnable;
            QualitySettings.globalTextureMipmapLimit = 0;

            RenderPipelineAsset pipeline = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null)
                QualitySettings.antiAliasing = samples > 1 ? samples : 0;
            else
            {
                // URP 14 exposes these properties publicly. Leave other SRP assets alone.
                Type type = pipeline.GetType();
                if (type.FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset")
                {
                    type.GetProperty("renderScale")?.SetValue(pipeline,1f);
                    type.GetProperty("msaaSampleCount")?.SetValue(pipeline,samples);
                    type.GetProperty("supportsHDR")?.SetValue(pipeline,hdr);
                    type.GetProperty("shadowDistance")?.SetValue(pipeline,profile.ShadowDistance);
                    type.GetProperty("shadowCascadeCount")?.SetValue(pipeline,profile.Cascades);
                }
            }
        }

        private static int SupportedSamples(int requested,bool hdr)
        {
            if (requested <= 1)
                return 1;
            var target = new RenderTextureDescriptor(64,64,
                hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default,24)
            {
                msaaSamples = requested,
            };
            int supported = SystemInfo.GetRenderTextureSupportedMSAASampleCount(target);
            return supported >= 8 ? 8 : supported >= 4 ? 4 : supported >= 2 ? 2 : 1;
        }

        public int Quality => quality;
        public string Weather => weather != null ? weather.Name : "Ясно";

        public void ChangeWeather() => Notify("Погода: "+weather.Next()+".");

        private void SetLight()
        {
            float phase = (clockMinutes - 6*60) / 1440f * Mathf.PI * 2;
            float height = Mathf.Sin(phase);
            float daylight = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.08f,.75f,height));
            float twilight = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.15f,.25f,height)) *
                (1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.24f,.55f,height)));
            float overcast = Mathf.InverseLerp(.0015f,.009f,RenderSettings.fogDensity);
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(-9f,height*61f),126f-height*57f,0f);
            sun.intensity = Mathf.Lerp(.08f,1.36f,daylight) * Mathf.Lerp(1f,.55f,overcast);
            sun.shadowStrength = Mathf.Lerp(.50f,.84f,daylight) * Mathf.Lerp(1f,.78f,overcast);
            Color morning = Color.Lerp(new Color(1f,.956f,.884f),new Color(1f,.72f,.49f),twilight*.67f);
            sun.color = Color.Lerp(new Color(.58f,.70f,1f),morning,daylight+twilight*(1f-daylight));

            RenderSettings.ambientSkyColor = Color.Lerp(new Color(.105f,.147f,.231f),
                new Color(.60f,.71f,.81f),daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(.069f,.088f,.127f),
                new Color(.412f,.466f,.496f),daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(.050f,.061f,.074f),
                new Color(.255f,.274f,.267f),daylight);
            Color horizon = Color.Lerp(new Color(.066f,.102f,.192f),new Color(.586f,.733f,.814f),daylight);
            horizon = Color.Lerp(horizon,new Color(.831f,.598f,.468f),.33f*twilight);
            RenderSettings.fogColor = Color.Lerp(horizon,Color.Lerp(new Color(.145f,.187f,.253f),
                new Color(.679f,.718f,.739f),daylight),overcast*.8f);

            if (worldCamera != null)
                worldCamera.backgroundColor = horizon;
            if (skybox != null)
            {
                skybox.SetColor("_SkyTint",Color.Lerp(new Color(.25f,.34f,.58f),
                    new Color(.49f,.55f,.62f),daylight));
                skybox.SetColor("_GroundColor",RenderSettings.fogColor);
                skybox.SetFloat("_Exposure",Mathf.Lerp(.31f,1.16f,daylight));
            }
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

        private void OnDestroy()
        {
            if (skybox != null)
            {
                if (RenderSettings.skybox == skybox)
                    RenderSettings.skybox = previousSkybox;
                Destroy(skybox);
            }
        }

        private void OnApplicationQuit()
        {
            if (ui != null)
                SaveProgress();
        }
    }
}
