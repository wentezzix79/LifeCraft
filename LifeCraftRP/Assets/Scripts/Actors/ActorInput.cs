using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    public enum ActorAction
    {
        Interact, Vehicle, Inventory, Map, Phone, Menu, Pause, Camera, Attack, Weapon, Jump, Sprint, Crouch
    }

    [DefaultExecutionOrder(-150)]
    public sealed class ActorInput : MonoBehaviour
    {
        private readonly HashSet<ActorAction> pending = new HashSet<ActorAction>();
        private readonly HashSet<ActorAction> pressed = new HashSet<ActorAction>();
        private static readonly KeyCode[] Keys =
        {
            KeyCode.E, KeyCode.F, KeyCode.I, KeyCode.M, KeyCode.P, KeyCode.Tab,
            KeyCode.Escape, KeyCode.V, KeyCode.None, KeyCode.None, KeyCode.Space,
            KeyCode.LeftShift, KeyCode.LeftControl
        };
        private static readonly (ActorAction action, string text, float x, float y)[] TouchButtons =
        {
            (ActorAction.Interact, "ДЕЙСТВИЕ", 0f, 0f),
            (ActorAction.Vehicle, "АВТО", 1.2f, .9f),
            (ActorAction.Jump, "ПРЫЖОК", 1.15f, -.30f),
            (ActorAction.Attack, "УДАР", 2.32f, -.24f),
            (ActorAction.Sprint, "БЕГ", 2.4f, 1.12f),
            (ActorAction.Phone, "ТЕЛЕФОН", 0f, 1.32f),
            (ActorAction.Inventory, "ВЕЩИ", 1.25f, 2.19f),
            (ActorAction.Weapon, "СМЕНА", 2.4f, 2.30f),
        };

        private int joystickId = -1;
        private int lookId = -1;
        private int heldSprint = -1;
        private bool sprintLatch;
        private Vector2 joystickStart;
        private Vector2 joystickPoint;
        private GUIStyle buttonStyle;
        private Texture2D touchDisc;
        private GUIStyle badgeStyle;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool CrouchHeld { get; private set; }
        public bool BrakeHeld { get; private set; }
        public bool GameplayEnabled { get; set; } = true;
        public bool DrawTouchControls { get; set; } = true;
        public bool InteractPressed => WasPressed(ActorAction.Interact);
        public bool VehiclePressed => WasPressed(ActorAction.Vehicle);
        public bool InventoryPressed => WasPressed(ActorAction.Inventory);
        public bool MapPressed => WasPressed(ActorAction.Map);
        public bool PhonePressed => WasPressed(ActorAction.Phone);
        public bool MenuPressed => WasPressed(ActorAction.Menu);
        public bool PausePressed => WasPressed(ActorAction.Pause);
        public bool JumpPressed => WasPressed(ActorAction.Jump);
        public bool AttackPressed => WasPressed(ActorAction.Attack);
        public bool CameraPressed => WasPressed(ActorAction.Camera);
        public bool WasPressed(ActorAction action) => pressed.Contains(action);

        public void Trigger(ActorAction action)
        {
            pending.Add(action);
        }

        private void Update()
        {
            pressed.Clear();
            pressed.UnionWith(pending);
            pending.Clear();
            Move = Vector2.zero;
            Look = Vector2.zero;
            SprintHeld = CrouchHeld = BrakeHeld = false;
            for (var i = 0; i < Keys.Length; i++)
                if (Keys[i] != KeyCode.None && Input.GetKeyDown(Keys[i]))
                    pressed.Add((ActorAction)i);
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace))
                pressed.Add(ActorAction.Pause);
            if (!GameplayEnabled)
            {
                joystickId = lookId = heldSprint = -1;
                sprintLatch = false;
                return;
            }
            if (Application.isMobilePlatform)
            {
                ReadTouches();
                return;
            }
            float x = Axis(KeyCode.A, KeyCode.D, KeyCode.LeftArrow, KeyCode.RightArrow);
            float y = Axis(KeyCode.S, KeyCode.W, KeyCode.DownArrow, KeyCode.UpArrow);
            Move = Vector2.ClampMagnitude(new Vector2(x,y), 1f);
            if (Cursor.lockState == CursorLockMode.Locked)
                Look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2.5f;
            SprintHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            CrouchHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            BrakeHeld = Input.GetKey(KeyCode.Space);
            if (Cursor.lockState == CursorLockMode.Locked && Input.GetMouseButtonDown(0))
                pressed.Add(ActorAction.Attack);
            if (Input.mouseScrollDelta.y != 0f)
                pressed.Add(ActorAction.Weapon);
        }

        private static float Axis(KeyCode minus, KeyCode plus, KeyCode altMinus, KeyCode altPlus)
        {
            return (Input.GetKey(plus) || Input.GetKey(altPlus) ? 1f : 0f)
                   - (Input.GetKey(minus) || Input.GetKey(altMinus) ? 1f : 0f);
        }

        private static float ButtonSize => Mathf.Clamp(Screen.height * .096f, 43f, 120f);
        private static float JoystickRadius => Mathf.Clamp(Screen.height * .104f, 46f, 122f);

        private static Rect ButtonRect(float offsetX, float offsetY)
        {
            float size = ButtonSize;
            float cx = Screen.width - size * (0.75f + offsetX);
            float cy = Screen.height - size * (1.09f + offsetY);
            return new Rect(cx - size * .5f, cy - size * .5f, size, size);
        }

        private void ReadTouches()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                var p = new Vector2(touch.position.x, Screen.height - touch.position.y);
                if (touch.phase == TouchPhase.Began)
                {
                    if (HandleTouchButton(touch, p))
                        continue;
                    if (joystickId < 0 && p.x < Screen.width * .43f && p.y > Screen.height * .40f)
                    {
                        joystickId = touch.fingerId;
                        joystickStart = joystickPoint = p;
                    }
                    else if (lookId < 0)
                        lookId = touch.fingerId;
                }
                var ended = touch.phase == TouchPhase.Canceled || touch.phase == TouchPhase.Ended;
                if (touch.fingerId == joystickId)
                {
                    joystickPoint = p;
                    if (ended)
                        joystickId = -1;
                }
                if (touch.fingerId == lookId)
                {
                    if (touch.phase == TouchPhase.Moved)
                        Look = touch.deltaPosition * (125f / Mathf.Max(Screen.height,1));
                    if (ended)
                        lookId = -1;
                }
                if (touch.fingerId == heldSprint && ended)
                    heldSprint = -1;
            }
            if (joystickId >= 0)
            {
                Vector2 delta = (joystickPoint - joystickStart) / JoystickRadius;
                Move = Vector2.ClampMagnitude(new Vector2(delta.x, -delta.y), 1f);
            }
            SprintHeld = sprintLatch || heldSprint >= 0;
            BrakeHeld = pressed.Contains(ActorAction.Jump);
        }

        private bool HandleTouchButton(Touch touch, Vector2 point)
        {
            foreach (var binding in TouchButtons)
            {
                if (!ButtonRect(binding.x, binding.y).Contains(point))
                    continue;
                pressed.Add(binding.action);
                if (binding.action == ActorAction.Sprint)
                {
                    heldSprint = touch.fingerId;
                    sprintLatch = !sprintLatch;
                }
                return true;
            }
            return false;
        }

        private void OnGUI()
        {
            if (!Application.isMobilePlatform || !GameplayEnabled || !DrawTouchControls
                || Event.current.type != EventType.Repaint)
                return;
            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
                };
                buttonStyle.normal.textColor = Color.white;
                badgeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                badgeStyle.normal.textColor = new Color(.85f,.88f,.98f,1f);
                touchDisc = CreateCircleTexture();
            }
            float radius = JoystickRadius;
            Vector2 origin = joystickId < 0
                ? new Vector2(radius * 1.58f, Screen.height - radius * 1.54f)
                : joystickStart;
            Vector2 axis = new Vector2(Move.x,-Move.y);
            DrawDisc(origin, radius, new Color(.055f,.064f,.16f,.65f));
            DrawDisc(origin + axis * radius * .69f, radius * .31f, new Color(.64f,.62f,.95f,.82f));
            foreach (var binding in TouchButtons)
            {
                Rect rect = ButtonRect(binding.x,binding.y);
                bool active = WasPressed(binding.action) || binding.action == ActorAction.Sprint && SprintHeld;
                DrawDisc(rect.center, rect.width * .5f,
                    active ? new Color(.45f,.29f,.88f,.84f) : new Color(.09f,.10f,.20f,.79f));
                buttonStyle.fontSize = Mathf.RoundToInt(rect.width * .133f);
                GUI.Label(rect, binding.text, buttonStyle);
            }
            if (joystickId < 0)
            {
                badgeStyle.fontSize = Mathf.RoundToInt(radius * .18f);
                GUI.Label(new Rect(origin.x - radius, origin.y + radius * .35f,
                    radius * 2f, radius * .35f), "ДВИЖЕНИЕ", badgeStyle);
            }
        }

        private static Texture2D CreateCircleTexture()
        {
            const int size = 48;
            var texture = new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            var colors = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x + .5f - size * .5f,y + .5f - size * .5f);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(size * .5f - p.magnitude) * 255f);
                colors[y * size + x] = new Color32(255,255,255,alpha);
            }
            texture.SetPixels32(colors);
            texture.Apply(false,true);
            return texture;
        }

        private void DrawDisc(Vector2 center,float radius,Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x-radius,center.y-radius,radius*2f,radius*2f), touchDisc);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            if (touchDisc)
                Destroy(touchDisc);
        }
    }
}
