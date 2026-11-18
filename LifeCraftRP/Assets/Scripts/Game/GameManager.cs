using System;
using System.IO;
using LifeCraft.World;
using LifeCraftRP.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ShadowResolution = UnityEngine.ShadowResolution;

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
        private StreetColorGrade streetColorGrade;
        private UniversalRenderPipelineAsset renderPipeline;
        private RenderPipelineAsset previousQualityPipeline;
        private float nextLightUpdate;
        private float clockMinutes = 10 * 60 + 22;
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
            sun.shadowBias = .035f;
            sun.shadowNormalBias = .28f;
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
            bool hdr = quality >= (mobile ? 3 : 2) &&
                SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.DefaultHDR);
            int requested = mobile ? profile.MobileSamples : profile.DesktopSamples;
            int samples = SupportedSamples(requested,hdr);
            if (hdr && samples == 1)
            {
                hdr = false;
                samples = SupportedSamples(requested,false);
            }
            if (worldCamera != null)
            {
                worldCamera.nearClipPlane = .10f;
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

            if (renderPipeline == null &&
                (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline)
                    is UniversalRenderPipelineAsset projectPipeline)
            {
                previousQualityPipeline = QualitySettings.renderPipeline;
                renderPipeline = Instantiate(projectPipeline);
                renderPipeline.name = "LC · настройки текущей игры";
                QualitySettings.renderPipeline = renderPipeline;
            }
            if (renderPipeline != null)
            {
                renderPipeline.renderScale = 1f;
                renderPipeline.msaaSampleCount = samples;
                renderPipeline.supportsHDR = hdr;
                renderPipeline.colorGradingMode = hdr
                    ? ColorGradingMode.HighDynamicRange : ColorGradingMode.LowDynamicRange;
                renderPipeline.shadowDistance = profile.ShadowDistance;
                renderPipeline.shadowCascadeCount = profile.Cascades;
                renderPipeline.cascadeBorder = .17f;
                if (worldCamera != null && streetColorGrade == null)
                    streetColorGrade = new StreetColorGrade(worldCamera);
                streetColorGrade?.SetEnabled(hdr);
            }
            else
            {
                QualitySettings.antiAliasing = samples > 1 ? samples : 0;
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

        public void ChangeWeather()
        {
            Notify("Погода: "+weather.Next()+".");
            nextLightUpdate = 0f;
        }

        private void SetLight()
        {
            float phase = (clockMinutes - 6f*60f) / 1440f * Mathf.PI*2f;
            float height = Mathf.Sin(phase);
            float daylight = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.08f,.72f,height));
            float twilight = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.15f,.20f,height)) *
                (1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.19f,.53f,height)));
            float overcast = Mathf.InverseLerp(.0013f,.009f,RenderSettings.fogDensity);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            // An angled daylight key and softer directional fill keep asphalt and masonry readable.
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(-9f,height*54f),
                Mathf.Repeat(179f-phase*Mathf.Rad2Deg,360f),0f);
            sun.intensity = Mathf.Lerp(.073f,linear ? 1.33f : 1.15f,daylight) *
                Mathf.Lerp(1f,.56f,overcast);
            sun.shadowStrength = Mathf.Lerp(.47f,.82f,daylight) * Mathf.Lerp(1f,.68f,overcast);
            Color sunColor = Color.Lerp(new Color(1f,.967f,.895f),
                new Color(1f,.750f,.554f),twilight*.68f);
            sunColor = Color.Lerp(sunColor,new Color(.874f,.932f,1f),overcast*.66f);
            sun.color = Color.Lerp(new Color(.63f,.72f,.97f),sunColor,daylight+twilight*(1f-daylight));

            Color skyFill = Color.Lerp(new Color(.112f,.158f,.234f),new Color(.641f,.739f,.839f),daylight);
            Color horizonFill = Color.Lerp(new Color(.074f,.092f,.125f),new Color(.472f,.529f,.570f),daylight);
            Color groundBounce = Color.Lerp(new Color(.064f,.071f,.090f),new Color(.325f,.304f,.270f),daylight);
            RenderSettings.ambientSkyColor = Color.Lerp(skyFill,horizonFill,overcast*.45f);
            RenderSettings.ambientEquatorColor = horizonFill;
            RenderSettings.ambientGroundColor = groundBounce;

            Color horizon = Color.Lerp(new Color(.067f,.105f,.183f),new Color(.638f,.748f,.826f),daylight);
            horizon = Color.Lerp(horizon,new Color(.841f,.633f,.477f),.29f*twilight);
            RenderSettings.fogColor = Color.Lerp(horizon,Color.Lerp(new Color(.157f,.184f,.237f),
                new Color(.684f,.723f,.749f),daylight),overcast*.84f);

            if (worldCamera != null)
                worldCamera.backgroundColor = RenderSettings.fogColor;
            if (skybox != null)
            {
                Color clearSky = Color.Lerp(new Color(.273f,.323f,.489f),new Color(.496f,.559f,.640f),daylight);
                skybox.SetColor("_SkyTint",Color.Lerp(clearSky,new Color(.546f,.569f,.589f),overcast*.60f));
                skybox.SetColor("_GroundColor",RenderSettings.fogColor);
                skybox.SetFloat("_Exposure",Mathf.Lerp(.28f,1.11f,daylight)*Mathf.Lerp(1f,.89f,overcast));
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
            if (Time.unscaledTime >= nextLightUpdate)
            {
                SetLight();
                nextLightUpdate = Time.unscaledTime + .08f;
            }
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
            streetColorGrade?.Dispose();
            if (renderPipeline != null)
            {
                if (QualitySettings.renderPipeline == renderPipeline)
                    QualitySettings.renderPipeline = previousQualityPipeline;
                Destroy(renderPipeline);
            }
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
