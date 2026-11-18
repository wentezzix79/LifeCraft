using UnityEngine;

namespace LifeCraftRP
{
    [DefaultExecutionOrder(-70)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField] private float orbitDistance = 4.24f;
        private readonly RaycastHit[] hits = new RaycastHit[12];
        private Camera sceneCamera;
        private float pitch = 12f;
        private float currentDistance;
        private PlayerController player;

        public float Yaw { get; private set; }
        public bool FirstPerson { get; private set; }
        public Camera SceneCamera => sceneCamera;

        public static CameraController Create(PlayerController player)
        {
            var go = new GameObject("Главная камера");
            go.tag = "MainCamera";
            var view = go.AddComponent<CameraController>();
            go.AddComponent<AudioListener>();
            view.Attach(player);
            return view;
        }

        private void Awake()
        {
            sceneCamera = GetComponent<Camera>();
            sceneCamera.nearClipPlane = .075f;
            sceneCamera.farClipPlane = 1350f;
            sceneCamera.fieldOfView = 63f;
            currentDistance = orbitDistance;
        }

        public void Attach(PlayerController actor)
        {
            if (!actor)
                return;
            player = actor;
            player.View = this;
            Align(actor.transform.eulerAngles.y);
            PositionCamera();
        }

        public void Align(float facing)
        {
            Yaw = facing;
            pitch = FirstPerson ? 0f : 12f;
        }

        public void SetFirstPerson(bool enabled)
        {
            FirstPerson = enabled;
            if (player)
                player.Animation.SetFirstPerson(enabled);
            pitch = enabled ? 0f : 12f;
        }

        private void Update()
        {
            if (!player || player.MenusOpen)
                return;
            ActorInput controls = player.Input;
            if (controls.CameraPressed)
                SetFirstPerson(!FirstPerson);
            Yaw = Mathf.Repeat(Yaw+controls.Look.x,360f);
            pitch = Mathf.Clamp(pitch-controls.Look.y,-42f,FirstPerson ? 75f : 56f);
        }

        private void LateUpdate()
        {
            if (player)
                PositionCamera();
        }

        private void PositionCamera()
        {
            Quaternion rotation = Quaternion.Euler(pitch,Yaw,0f);
            if (FirstPerson)
            {
                transform.SetPositionAndRotation(player.FirstPersonAnchor,rotation);
                return;
            }
            Vector3 origin = player.CameraAnchor;
            Vector3 rest = origin + rotation * new Vector3(.58f, .14f, -orbitDistance);
            Vector3 line = rest-origin;
            float goal = line.magnitude;
            Vector3 direction = line/goal;
            int count = Physics.SphereCastNonAlloc(origin,.15f,direction,hits,goal,~0,QueryTriggerInteraction.Ignore);
            for (var i=0;i<count;i++)
            {
                var other = hits[i].collider;
                if (!other || other.transform.IsChildOf(player.transform)
                    || (player.IsDriving && other.transform.IsChildOf(player.CurrentVehicle.transform)))
                    continue;
                goal = Mathf.Min(goal,Mathf.Max(.11f,hits[i].distance-.07f));
            }
            currentDistance = goal < currentDistance
                ? goal
                : Mathf.MoveTowards(currentDistance,goal,Time.deltaTime*9f);
            transform.SetPositionAndRotation(origin+direction*currentDistance,rotation);
        }
    }
}
