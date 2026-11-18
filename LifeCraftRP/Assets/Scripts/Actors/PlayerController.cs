using UnityEngine;

namespace LifeCraftRP
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CharacterController),typeof(ActorInput),typeof(PlayerAnimationController))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float StandHeight = 1.79f;
        private const float CrouchHeight = 1.14f;
        private const float Gravity = 22f;
        private CharacterController controller;
        private Vector3 velocity;
        private Vector3 spawnPlace;
        private float stamina = 1f;
        private bool sprintExhausted;
        private bool menusOpen;
        private bool crouched;
        private float nextPunch;
        private readonly Collider[] overlap = new Collider[12];

        public ActorInput Input { get; private set; }
        public PlayerAnimationController Animation { get; private set; }
        public CameraController View { get; internal set; }
        public bool IsDriving => CurrentVehicle;
        public VehicleController CurrentVehicle { get; private set; }
        public VehicleController FocusedVehicle { get; set; }
        public float Stamina01 => stamina;
        public float Health01 { get; private set; } = 1f;
        public float Armor01 { get; private set; }
        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;
        public Vector3 CameraAnchor => IsDriving
            ? CurrentVehicle.transform.TransformPoint(new Vector3(0f,1.31f,.04f))
            : transform.position + Vector3.up*(crouched ? 1.10f : 1.62f);
        public Vector3 FirstPersonAnchor => IsDriving
            ? CurrentVehicle.transform.TransformPoint(new Vector3(-.33f,1.18f,.26f))
            : transform.position + Vector3.up*(crouched ? 1.09f : 1.62f);

        public bool MenusOpen
        {
            get => menusOpen;
            set
            {
                menusOpen = value;
                if (Input)
                    Input.GameplayEnabled = !value;
                ApplyCursor();
            }
        }

        public static PlayerController Create(Vector3 spawn)
        {
            var player = new GameObject("Местный игрок");
            player.transform.position = spawn;
            return player.AddComponent<PlayerController>();
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            controller.height = StandHeight;
            controller.center = new Vector3(0,StandHeight/2f,0);
            controller.radius = .30f;
            controller.skinWidth = .035f;
            controller.stepOffset = .31f;
            controller.slopeLimit = 48f;
            controller.minMoveDistance = 0f;
            Input = GetComponent<ActorInput>();
            Animation = GetComponent<PlayerAnimationController>();
            Animation.Build();
            spawnPlace = transform.position;
        }

        private void Start() => ApplyCursor();

        private void OnApplicationFocus(bool focused)
        {
            if (focused)
                ApplyCursor();
        }

        private void ApplyCursor()
        {
            if (Application.isMobilePlatform)
                return;
            Cursor.lockState = menusOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = menusOpen;
        }

        public void ApplyWorkUniform(bool enabled) => Animation.ApplyWorkUniform(enabled);

        public void RestoreHealth()
        {
            Health01 = 1f;
        }

        public void Heal(float amount01)
        {
            Health01 = Mathf.Clamp01(Health01+Mathf.Max(0,amount01));
        }

        public void SetArmor(float amount01) => Armor01 = Mathf.Clamp01(amount01);

        public void ApplyDamage(float amount01)
        {
            float damage = Mathf.Max(0,amount01);
            float absorbed = Mathf.Min(Armor01,damage);
            Armor01 -= absorbed;
            Health01 = Mathf.Max(0,Health01-damage+absorbed);
            if (Health01 > 0f)
                return;
            ExitVehicle(true);
            Teleport(spawnPlace);
            RestoreHealth();
        }

        public void Teleport(Vector3 position)
        {
            if (IsDriving && !ExitVehicle(true))
                return;
            controller.enabled = false;
            transform.position = position;
            velocity = Vector3.zero;
            controller.enabled = true;
        }

        public bool TryEnter(VehicleController vehicle)
        {
            if (!vehicle || IsDriving || !vehicle.Drivable || vehicle.IsOccupied
                || (vehicle.transform.position-transform.position).sqrMagnitude > 4.25f*4.25f
                || !vehicle.TryAssignDriver(this))
                return false;
            Animation.Enter();
            controller.enabled = false;
            CurrentVehicle = vehicle;
            transform.SetParent(vehicle.transform,false);
            transform.localPosition = new Vector3(-.34f,.18f,-.20f);
            transform.localRotation = Quaternion.identity;
            velocity = Vector3.zero;
            if (View)
                View.Align(vehicle.transform.eulerAngles.y);
            return true;
        }

        public bool ExitVehicle(bool force = false)
        {
            var vehicle = CurrentVehicle;
            if (!vehicle)
                return false;
            if (!force && vehicle.SpeedKmh > 9f)
                return false;
            Vector3 destination = default;
            bool clear = false;
            foreach (Vector3 side in new[]{Vector3.left,Vector3.right,Vector3.back})
            {
                Vector3 aim = vehicle.transform.position + vehicle.transform.TransformDirection(side)*2.1f;
                if (Physics.Raycast(aim + Vector3.up*2f,Vector3.down,out var floor,4f,
                    ~0,QueryTriggerInteraction.Ignore))
                    aim.y = floor.point.y+.07f;
                if (BlockedAt(aim))
                    continue;
                destination = aim;
                clear = true;
                break;
            }
            if (!clear && !force)
                return false;
            transform.SetParent(null,true);
            transform.position = clear ? destination : vehicle.transform.position + Vector3.up*1.8f;
            transform.rotation = Quaternion.Euler(0,vehicle.transform.eulerAngles.y,0);
            CurrentVehicle = null;
            Animation.Exit();
            vehicle.ReleaseDriver(this);
            controller.enabled = true;
            velocity = Vector3.zero;
            if (View)
                View.Align(transform.eulerAngles.y);
            return true;
        }

        private bool BlockedAt(Vector3 foot)
        {
            int count = Physics.OverlapCapsuleNonAlloc(foot + Vector3.up*.34f,foot+Vector3.up*1.44f,
                .26f,overlap,~0,QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (overlap[i] && !overlap[i].transform.IsChildOf(transform))
                    return true;
            return false;
        }

        private void Update()
        {
            if (IsDriving)
            {
                CurrentVehicle.SetInputs(menusOpen ? Vector2.zero : Input.Move,menusOpen || Input.BrakeHeld);
                Animation.Animate(0,true,false,true,0);
                stamina = Mathf.Min(1f,stamina+Time.deltaTime*.21f);
                return;
            }
            if (!controller.enabled)
                controller.enabled = true;
            Vector2 axes = menusOpen ? Vector2.zero : Input.Move;
            float yaw = View ? View.Yaw : transform.eulerAngles.y;
            Vector3 heading = Quaternion.Euler(0,yaw,0) * new Vector3(axes.x,0f,axes.y);
            float dt = Time.deltaTime;
            bool grounded = controller.isGrounded;
            if (Input.CrouchHeld && !menusOpen)
                crouched = true;
            else if (!crouched || !BlockedAt(transform.position))
                crouched = false;
            var desiredHeight = crouched ? CrouchHeight : StandHeight;
            controller.height = Mathf.MoveTowards(controller.height,desiredHeight,dt*4f);
            controller.center = Vector3.up*controller.height*.5f;
            controller.stepOffset = crouched ? .23f : .31f;

            if (stamina <= .02f)
                sprintExhausted = true;
            else if (stamina >= .23f)
                sprintExhausted = false;
            bool sprinting = !menusOpen && Input.SprintHeld && !sprintExhausted && !crouched
                && heading.sqrMagnitude > .09f && grounded;
            stamina = Mathf.Clamp01(stamina+dt*(sprinting ? -.155f : .19f));
            float speed = crouched ? 1.84f : sprinting ? 7.23f : 3.94f;
            Vector3 desired = heading*speed;
            if (Animation.Carrying)
                desired *= .70f;
            Vector3 flat = new Vector3(velocity.x,0f,velocity.z);
            flat = Vector3.MoveTowards(flat,desired,dt*(grounded ? 25f : 7f));
            velocity.x = flat.x;
            velocity.z = flat.z;
            if (grounded && velocity.y < 0)
                velocity.y = -2.4f;
            if (grounded && !crouched && !menusOpen && Input.JumpPressed)
                velocity.y = Mathf.Sqrt(2f*Gravity*1.02f);
            velocity.y -= Gravity*dt;
            CollisionFlags collision = controller.Move(velocity*dt);
            if ((collision & CollisionFlags.Above) != 0 && velocity.y > 0f)
                velocity.y = 0f;
            if (View && View.FirstPerson && !menusOpen)
                transform.rotation = Quaternion.Euler(0f,yaw,0f);
            else if (heading.sqrMagnitude > .002f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(heading),dt*750f);
            if (!menusOpen && Input.AttackPressed && !Animation.Carrying && Time.time >= nextPunch)
            {
                Animation.Punch();
                nextPunch = Time.time+.52f;
            }
            Animation.Animate(new Vector2(controller.velocity.x,controller.velocity.z).magnitude,
                controller.isGrounded,crouched,false,velocity.y);
            if (transform.position.y < -35f)
                Teleport(spawnPlace);
        }
    }
}
