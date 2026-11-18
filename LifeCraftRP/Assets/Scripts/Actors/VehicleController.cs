using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        private const float TireRadius = .317f;
        private const float TankCapacity = 44f;
        private readonly WheelCollider[] suspension = new WheelCollider[4];
        private readonly Transform[] wheels = new Transform[4];
        private readonly Light[] beams = new Light[2];
        private readonly Renderer[] brakeLights = new Renderer[2];
        private readonly Renderer[] blinkers = new Renderer[4];
        private Vector2 input;
        private Transform driverDoor;
        private Transform steeringWheel;
        private Rigidbody body;
        private Material offRed;
        private Material onRed;
        private Material offGold;
        private Material onGold;
        private float brakeAmount;
        private float doorUntil;
        private float steerAngle;
        private float fuel = TankCapacity * .66f;
        private float condition = 1f;
        private int turnSignal;
        private bool braking;
        private bool lights;
        private bool drivable = true;
        private float nextHit;

        public PlayerController Driver { get; private set; }
        public bool IsOccupied => Driver;
        public bool Drivable
        {
            get => drivable;
            set => drivable = value;
        }
        public float SpeedKmh => body ? body.velocity.magnitude*3.6f : 0f;
        public float FuelLiters => fuel;
        public float Fuel01 => fuel/TankCapacity;
        public float Condition01 => condition;
        public string ModelName => "Волна 1.5";

        public static VehicleController Create(Vector3 position, Quaternion rotation)
        {
            var vehicle = new GameObject("Волна 1.5");
            vehicle.transform.SetPositionAndRotation(position+Vector3.up*.17f,rotation);
            return vehicle.AddComponent<VehicleController>();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.mass = 1080f;
            body.drag = .06f;
            body.angularDrag = 1.7f;
            body.centerOfMass = new Vector3(0f,.45f,.02f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var hull = gameObject.AddComponent<BoxCollider>();
            hull.center = new Vector3(0,.75f,0);
            hull.size = new Vector3(1.64f,.78f,4.12f);
            var roof = gameObject.AddComponent<BoxCollider>();
            roof.center = new Vector3(0,1.22f,-.27f);
            roof.size = new Vector3(1.42f,.34f,1.81f);
            Build();
        }

        public bool TryAssignDriver(PlayerController player)
        {
            if (!player || !Drivable || Driver || SpeedKmh > 7f || condition <= .01f)
                return false;
            Driver = player;
            doorUntil = Time.time+.86f;
            return true;
        }

        public void ReleaseDriver(PlayerController player)
        {
            if (Driver != player)
                return;
            Driver = null;
            input = Vector2.zero;
            braking = false;
            doorUntil = Time.time+.89f;
            turnSignal = 0;
        }

        public void SetInputs(Vector2 axes,bool handbrake)
        {
            input = Vector2.ClampMagnitude(axes,1f);
            braking = handbrake;
        }

        public void SetLights(bool enabled)
        {
            lights = enabled;
            foreach (var beam in beams)
                if (beam)
                    beam.enabled = enabled;
        }

        public void SetTurnSignal(int signal) => turnSignal = Mathf.Clamp(signal,-1,1);

        public void SetFuel(float liters) => fuel = Mathf.Clamp(liters,0f,TankCapacity);

        public void SetCondition(float value) => condition = Mathf.Clamp01(value);

        public void Refuel(float liters) => SetFuel(fuel+Mathf.Max(0f,liters));

        public void Repair(float amount01) => condition = Mathf.Clamp01(condition+Mathf.Max(0,amount01));

        private void FixedUpdate()
        {
            if (!suspension[0])
                return;
            Vector3 local = transform.InverseTransformDirection(body.velocity);
            bool controlled = Driver && Drivable && condition > .01f;
            float forward = controlled ? input.y : 0f;
            float kmh = Mathf.Abs(local.z*3.6f);
            bool reversingDirection = kmh > 3f && forward*local.z < -.15f;
            bool limit = forward > 0 ? kmh > 115f : kmh > 19f && forward < 0f;
            float power = reversingDirection || limit || fuel <= 0f ? 0f
                : Mathf.Lerp(390f,102f,Mathf.Clamp01(kmh/120f))*forward*Mathf.Lerp(.45f,1f,condition);
            brakeAmount = !controlled || braking ? 1f : reversingDirection ? Mathf.Abs(forward) : 0f;
            float cornerLimit = Mathf.Lerp(33f,11f,Mathf.InverseLerp(18f,100f,kmh));
            steerAngle = Mathf.MoveTowards(steerAngle,controlled ? input.x*cornerLimit : 0f,
                Time.fixedDeltaTime*138f);
            for (var i=0;i<4;i++)
            {
                suspension[i].steerAngle = i < 2 ? steerAngle : 0f;
                suspension[i].motorTorque = i < 2 ? power : 0f;
                suspension[i].brakeTorque = brakeAmount*(i < 2 ? 910f : 1270f);
                StabilizeAxle(i);
            }
            if (controlled && fuel > 0f)
                fuel = Mathf.Max(0f,fuel-(.00024f+.00155f*Mathf.Abs(power)/390f)*Time.fixedDeltaTime);
        }

        private void StabilizeAxle(int i)
        {
            if ((i & 1) != 0)
                return;
            WheelCollider left = suspension[i];
            WheelCollider right = suspension[i+1];
            bool leftGrounded = left.GetGroundHit(out WheelHit hitLeft);
            bool rightGrounded = right.GetGroundHit(out WheelHit hitRight);
            float leftCompression = leftGrounded ? SpringCompression(left,hitLeft) : 1f;
            float rightCompression = rightGrounded ? SpringCompression(right,hitRight) : 1f;
            float force = 5200f*(leftCompression-rightCompression);
            if (leftGrounded)
                body.AddForceAtPosition(-left.transform.up*force,left.transform.position);
            if (rightGrounded)
                body.AddForceAtPosition(right.transform.up*force,right.transform.position);
        }

        private static float SpringCompression(WheelCollider wheel,WheelHit hit)
        {
            float y = wheel.transform.InverseTransformPoint(hit.point).y;
            return (-y-wheel.radius)/wheel.suspensionDistance;
        }

        private void Update()
        {
            if (Driver && !Driver.MenusOpen && !Application.isMobilePlatform)
            {
                if (Input.GetKeyDown(KeyCode.L))
                    SetLights(!lights);
                if (Input.GetKeyDown(KeyCode.Q))
                    SetTurnSignal(turnSignal == -1 ? 0 : -1);
                if (Input.GetKeyDown(KeyCode.R))
                    SetTurnSignal(turnSignal == 1 ? 0 : 1);
            }
            foreach(var lamp in brakeLights)
                if (lamp)
                    lamp.sharedMaterial = brakeAmount > .12f ? onRed : offRed;
            bool pulse = Mathf.Repeat(Time.time, .81f) < .46f;
            for (var i=0;i<blinkers.Length;i++)
                if (blinkers[i])
                    blinkers[i].sharedMaterial = pulse && turnSignal == ((i & 1) == 0 ? -1 : 1)
                        ? onGold : offGold;
        }

        private void LateUpdate()
        {
            if (steeringWheel)
                steeringWheel.localRotation = Quaternion.Euler(22f,0f,-steerAngle*3.7f);
            if (driverDoor)
            {
                float transition = Mathf.Clamp01((doorUntil-Time.time)/.86f);
                driverDoor.localRotation = Quaternion.Euler(0f,57f*Mathf.Sin(Mathf.PI*transition),0f);
            }
            for (var i=0;i<wheels.Length;i++)
                if (suspension[i] && wheels[i])
                {
                    suspension[i].GetWorldPose(out Vector3 p,out Quaternion q);
                    wheels[i].SetPositionAndRotation(p,q);
                }
        }

        private void OnCollisionEnter(Collision contact)
        {
            if (Time.time < nextHit || contact.relativeVelocity.magnitude <= 4.8f
                || contact.contactCount == 0 || Mathf.Abs(contact.GetContact(0).normal.y) > .71f)
                return;
            float hit = Mathf.Clamp01((contact.relativeVelocity.magnitude-4.8f)/120f);
            condition = Mathf.Max(0f,condition-hit);
            nextHit = Time.time+.24f;
            if (Driver)
                Driver.ApplyDamage(hit*.42f);
        }

        private void Build()
        {
            var paint = ActorArt.Paint(new Color(.31f,.12f,.48f),.58f,.33f);
            var edge = ActorArt.Paint(new Color(.05f,.052f,.065f),.38f);
            var glass = Glass();
            var tire = ActorArt.Paint(new Color(.045f,.044f,.042f),.13f);
            var metal = ActorArt.Paint(new Color(.57f,.60f,.66f),.52f,.54f);
            var bright = ActorArt.Paint(new Color(.88f,.92f,.97f),.75f);
            offRed = ActorArt.Paint(new Color(.34f,.055f,.058f),.64f);
            onRed = Lamp(new Color(.86f,.063f,.045f));
            offGold = ActorArt.Paint(new Color(.48f,.23f,.03f),.55f);
            onGold = Lamp(new Color(1f,.57f,.071f));

            Transform visual = ActorArt.Joint("Кузов",transform,Vector3.zero);
            Shell(visual,paint);
            var length = 1.92f;
            ActorArt.Form(PrimitiveType.Cube,"Передний бампер",visual,new Vector3(0,.48f,length+.184f),
                new Vector3(1.55f,.14f,.13f),edge);
            ActorArt.Form(PrimitiveType.Cube,"Задний бампер",visual,new Vector3(0,.49f,-length-.156f),
                new Vector3(1.55f,.14f,.14f),edge);
            ActorArt.Form(PrimitiveType.Cube,"Решётка радиатора",visual,new Vector3(0,.655f,2.126f),
                new Vector3(.87f,.142f,.057f),edge);
            for (var k=0;k<5;k++)
                ActorArt.Form(PrimitiveType.Cube,"Хромированная решётка",visual,new Vector3(0,.60f+.025f*k,2.158f),
                    new Vector3(.84f,.008f,.008f),metal);
            ActorArt.Form(PrimitiveType.Cube,"Передний номер",visual,new Vector3(0,.494f,2.181f),
                new Vector3(.326f,.085f,.006f),bright);
            ActorArt.Form(PrimitiveType.Cube,"Задний номер",visual,new Vector3(0,.622f,-2.103f),
                new Vector3(.326f,.085f,.007f),bright);
            ActorArt.Form(PrimitiveType.Cube,"Выхлоп",visual,new Vector3(.566f,.399f,-2.058f),
                new Vector3(.12f,.049f,.172f),metal);
            Cabin(visual,glass,paint);
            BuildInterior(visual,edge,metal);

            for (var i=0;i<2;i++)
            {
                float s = i == 0 ? -1f : 1f;
                ActorArt.Form(PrimitiveType.Cube,"Дневные ходовые огни",visual,
                    new Vector3(s*.585f,.741f,2.056f),new Vector3(.30f,.115f,.045f),bright);
                brakeLights[i] = ActorArt.Form(PrimitiveType.Cube,"Фонарь",visual,
                    new Vector3(s*.603f,.756f,-2.024f),new Vector3(.29f,.119f,.069f),offRed)
                    .GetComponent<Renderer>();
                blinkers[i] = ActorArt.Form(PrimitiveType.Cube,"Передний указатель",visual,
                    new Vector3(s*.777f,.701f,1.983f),new Vector3(.055f,.077f,.127f),offGold)
                    .GetComponent<Renderer>();
                blinkers[i+2] = ActorArt.Form(PrimitiveType.Cube,"Задний указатель",visual,
                    new Vector3(s*.769f,.744f,-1.984f),new Vector3(.057f,.075f,.10f),offGold)
                    .GetComponent<Renderer>();
                Transform lamp = ActorArt.Joint("Дальний свет",visual,new Vector3(s*.582f,.775f,2.133f));
                lamp.localRotation = Quaternion.Euler(7f,0f,0f);
                beams[i] = lamp.gameObject.AddComponent<Light>();
                beams[i].type = LightType.Spot;
                beams[i].spotAngle = 42f;
                beams[i].intensity = 8f;
                beams[i].range = 36f;
                beams[i].color = new Color(.88f,.93f,1f);
                beams[i].enabled = false;

                Transform hinge = ActorArt.Joint("Дверь "+i,visual,new Vector3(s*.846f,.833f,.68f));
                if (i == 0)
                    driverDoor = hinge;
                ActorArt.Form(PrimitiveType.Cube,"Панель двери",hinge,new Vector3(0,0,-.50f),
                    new Vector3(.042f,.38f,.98f),paint);
                ActorArt.Form(PrimitiveType.Cube,"Ручка",hinge,new Vector3(s*.035f,.081f,-.78f),
                    new Vector3(.037f,.031f,.147f),metal);
                ActorArt.Form(PrimitiveType.Sphere,"Зеркало",hinge,new Vector3(s*.137f,.312f,-.055f),
                    new Vector3(.21f,.088f,.15f),paint);
            }
            for (var i=0;i<4;i++)
            {
                float side = (i & 1) == 0 ? -1f : 1f;
                var center = new Vector3(side*.762f,.52f,i < 2 ? 1.332f : -1.328f);
                Transform carrier = ActorArt.Joint("Узел подвески "+i,transform,center);
                var wheel = carrier.gameObject.AddComponent<WheelCollider>();
                wheel.radius = TireRadius;
                wheel.mass = 18f;
                wheel.suspensionDistance = .226f;
                JointSpring spring = wheel.suspensionSpring;
                spring.spring = 33700f;
                spring.damper = 4600f;
                spring.targetPosition = .53f;
                wheel.suspensionSpring = spring;
                WheelFrictionCurve forward = wheel.forwardFriction;
                forward.stiffness = 1.22f;
                wheel.forwardFriction = forward;
                WheelFrictionCurve sideways = wheel.sidewaysFriction;
                sideways.stiffness = 1.26f;
                wheel.sidewaysFriction = sideways;
                wheel.ConfigureVehicleSubsteps(5f,10,4);
                suspension[i] = wheel;
                wheels[i] = ActorArt.Joint("Колесо "+i,visual,center);
                GameObject rubber = ActorArt.Form(PrimitiveType.Cylinder,"Шина",wheels[i],Vector3.zero,
                    new Vector3(TireRadius*2,.106f,TireRadius*2),tire);
                rubber.transform.localRotation = Quaternion.Euler(0f,0f,90f);
                GameObject cap = ActorArt.Form(PrimitiveType.Cylinder,"Диск",wheels[i],Vector3.right*side*.106f,
                    new Vector3(.434f,.010f,.434f),metal);
                cap.transform.localRotation = Quaternion.Euler(0f,0f,90f);
                for(var r=0;r<5;r++)
                {
                    float rad = r*Mathf.PI*2f/5f;
                    var spoke = ActorArt.Form(PrimitiveType.Cube,"Прорезь диска",wheels[i],
                        new Vector3(side*.116f,.124f*Mathf.Cos(rad),.124f*Mathf.Sin(rad)),
                        new Vector3(.008f,.101f,.029f),edge);
                    spoke.transform.localRotation = Quaternion.Euler(rad*Mathf.Rad2Deg,0f,0f);
                }
            }
        }

        private void BuildInterior(Transform mount,Material upholstery,Material trim)
        {
            ActorArt.Form(PrimitiveType.Cube,"Приборная панель",mount,new Vector3(0,.966f,.688f),
                new Vector3(1.35f,.14f,.31f),upholstery);
            ActorArt.Form(PrimitiveType.Cube,"Приборы",mount,new Vector3(-.337f,1.032f,.556f),
                new Vector3(.27f,.125f,.065f),trim);
            ActorArt.Form(PrimitiveType.Cube,"Центральная консоль",mount,new Vector3(0,.829f,.086f),
                new Vector3(.19f,.205f,.63f),upholstery);
            for (var i=0;i<2;i++)
            {
                float x = i == 0 ? -.342f : .342f;
                ActorArt.Form(PrimitiveType.Cube,"Подушка сиденья",mount,new Vector3(x,.786f,-.33f),
                    new Vector3(.47f,.13f,.435f),upholstery);
                var back = ActorArt.Form(PrimitiveType.Cube,"Спинка сиденья",mount,new Vector3(x,.969f,-.53f),
                    new Vector3(.421f,.40f,.102f),upholstery);
                back.transform.localRotation = Quaternion.Euler(-13f,0f,0f);
                ActorArt.Form(PrimitiveType.Cube,"Подголовник",mount,new Vector3(x,1.205f,-.577f),
                    new Vector3(.212f,.126f,.093f),upholstery);
            }
            steeringWheel = ActorArt.Joint("Руль",mount,new Vector3(-.335f,1.056f,.467f));
            for (var i=0;i<14;i++)
            {
                float phi = i*Mathf.PI*2f/14f;
                var r = ActorArt.Form(PrimitiveType.Capsule,"Обод",steeringWheel,
                    new Vector3(Mathf.Sin(phi)*.132f,Mathf.Cos(phi)*.132f,0f),
                    new Vector3(.023f,.042f,.028f),upholstery);
                r.transform.localRotation = Quaternion.Euler(0f,0f,90f-phi*Mathf.Rad2Deg);
            }
            ActorArt.Form(PrimitiveType.Cube,"Ступица",steeringWheel,Vector3.zero,
                new Vector3(.214f,.037f,.022f),upholstery);
        }

        private static Material Glass()
        {
            var mat = ActorArt.Paint(new Color(.25f,.44f,.58f,.33f),.81f,.06f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface",1f);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode",2f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull",0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip",0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite",0f);
            mat.SetOverrideTag("RenderType","Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        private static Material Lamp(Color color)
        {
            var mat = ActorArt.Paint(color,.67f);
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor",color*1.5f);
            return mat;
        }

        private static void Shell(Transform root,Material bodyPaint)
        {
            float[] z = {-2.1f,-1.99f,-1.52f,-.87f,.70f,1.47f,1.94f,2.14f};
            float[] wing = {.68f,.777f,.809f,.843f,.843f,.805f,.77f,.70f};
            float[] shoulder = {.71f,.837f,.899f,.93f,.947f,.887f,.798f,.712f};
            var vertices = new List<Vector3>();
            for (var i=0;i<z.Length;i++)
            {
                float w = wing[i];
                float y = shoulder[i];
                vertices.Add(new Vector3(-w*.89f,.451f,z[i]));
                vertices.Add(new Vector3(w*.89f,.451f,z[i]));
                vertices.Add(new Vector3(w,y-.096f,z[i]));
                vertices.Add(new Vector3(w*.70f,y,z[i]));
                vertices.Add(new Vector3(-w*.70f,y,z[i]));
                vertices.Add(new Vector3(-w,y-.096f,z[i]));
            }
            var triangles = new List<int>();
            for (var i=0;i<z.Length-1;i++)
            for (var j=0;j<6;j++)
            {
                int a = i*6+j;
                int b = i*6+(j+1)%6;
                triangles.AddRange(new[]{a,b,a+6,b,b+6,a+6});
            }
            for (var i=1;i<5;i++)
            {
                triangles.AddRange(new[]{0,i+1,i});
                int m = (z.Length-1)*6;
                triangles.AddRange(new[]{m,m+i,m+i+1});
            }
            ActorArt.Custom("Штампованный кузов",root,vertices,triangles,bodyPaint);
        }

        private static void Cabin(Transform root, Material glass,Material bodyPaint)
        {
            var vertices = new List<Vector3>
            {
                new Vector3(-.700f,.905f,-1.63f),new Vector3(.700f,.905f,-1.63f),
                new Vector3(-.734f,.937f,.94f),new Vector3(.734f,.937f,.94f),
                new Vector3(-.597f,1.386f,-1.105f),new Vector3(.597f,1.386f,-1.105f),
                new Vector3(-.593f,1.398f,.31f),new Vector3(.593f,1.398f,.31f)
            };
            var triangles = new List<int>
            {
                0,4,1,1,4,5, 0,2,4,2,6,4, 1,5,3,3,5,7, 2,3,6,3,7,6,
            };
            ActorArt.Custom("Стёкла салона",root,vertices,triangles,glass);
            ActorArt.Form(PrimitiveType.Cube,"Панель крыши",root,
                new Vector3(0,1.402f,-.39f),new Vector3(1.228f,.034f,1.453f),bodyPaint);
            for(var i=0;i<2;i++)
                ActorArt.Form(PrimitiveType.Cube,"Стойка кузова",root,
                    new Vector3(i == 0 ? -.651f : .651f,1.177f,-.283f),
                    new Vector3(.062f,.437f,.061f),bodyPaint);
        }
    }
}
