using UnityEngine;

namespace LifeCraftRP
{
    [DefaultExecutionOrder(-70)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float orbitDistance = 3.65f;
        private readonly RaycastHit[] hits = new RaycastHit[12];
        private Camera sceneCamera;
        private float pitch = 8f;
        private float currentDistance;
        private Vector3 smoothedAnchor;
        private bool resetAnchor = true;
        private bool portraitRequested;
        private bool showingPortrait;
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
            pitch = FirstPerson ? 0f : 8f;
            resetAnchor = true;
        }

        public void SetFirstPerson(bool enabled)
        {
            FirstPerson = enabled;
            if (player)
                player.Animation.SetFirstPerson(enabled);
            pitch = enabled ? 0f : 8f;
            resetAnchor = true;
        }

        private void Update()
        {
            portraitRequested = false;
            if (!player || player.MenusOpen)
                return;
            ActorInput controls = player.Input;
            if (controls.CameraPressed)
                SetFirstPerson(!FirstPerson);
            portraitRequested = !FirstPerson && !player.IsDriving && !player.IsRidingBus && !Application.isMobilePlatform &&
                Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
                controls.Move.sqrMagnitude < .001f && !controls.JumpPressed && Input.GetKey(KeyCode.C);
            if (portraitRequested)
                return;
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
            bool portrait = portraitRequested && !FirstPerson && !player.IsDriving && !player.MenusOpen;
            bool switched = showingPortrait != portrait;
            showingPortrait = portrait;
            float dt = Time.deltaTime;
            float fov = portrait ? 43f : FirstPerson ? 66f : player.IsDriving ? 65f : 59f;
            sceneCamera.fieldOfView = switched ? fov :
                Mathf.Lerp(sceneCamera.fieldOfView,fov,1f-Mathf.Exp(-12f*dt));
            Quaternion rotation = Quaternion.Euler(pitch,Yaw,0f);
            if (FirstPerson)
            {
                transform.SetPositionAndRotation(player.FirstPersonAnchor,rotation);
                return;
            }

            Vector3 follow = player.CameraAnchor;
            Vector3 anchor = portrait ? Vector3.Lerp(player.Position,follow,.59f) : follow;
            bool snap = resetAnchor || switched || (anchor-smoothedAnchor).sqrMagnitude > 9f;
            smoothedAnchor = snap ? anchor : Vector3.Lerp(smoothedAnchor,anchor,1f-Mathf.Exp(-19f*dt));
            resetAnchor = false;

            Vector3 offset;
            if (portrait)
                offset = Quaternion.Euler(0f,player.transform.eulerAngles.y,0f) * new Vector3(.22f,.23f,3.10f);
            else if (player.IsDriving)
                offset = rotation * new Vector3(.20f,.36f,-orbitDistance-1.12f);
            else if (player.IsRidingBus)
                offset = rotation * new Vector3(.56f,1.7f,-orbitDistance-4f);
            else
                offset = rotation * new Vector3(.56f,.08f,-orbitDistance);

            Vector3 position = FollowWithCollision(smoothedAnchor,offset,snap,dt);
            if (portrait)
                rotation = Quaternion.LookRotation(smoothedAnchor-position,Vector3.up);
            transform.SetPositionAndRotation(position,rotation);
        }

        private Vector3 FollowWithCollision(Vector3 origin,Vector3 line,bool snap,float dt)
        {
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
            currentDistance = snap || goal < currentDistance
                ? goal
                : Mathf.MoveTowards(currentDistance,goal,dt*9f);
            return origin+direction*currentDistance;
        }
    }
}
