using System;
using System.Collections.Generic;
using LifeCraftRP;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    public sealed class OmskBusView : MonoBehaviour
    {
        public static readonly Vector3 BoardingOffset = new Vector3(1.95f, .08f, 2f);
        public static IReadOnlyList<Vector3> SeatOffsets { get; } = Array.AsReadOnly(new[]
        {
            new Vector3(.61f, .36f, .65f), new Vector3(.61f, .36f, -.80f),
            new Vector3(.61f, .36f, -2.25f), new Vector3(.61f, .36f, -4.05f),
            new Vector3(-.67f, .36f, 2.45f), new Vector3(-.67f, .36f, .74f),
            new Vector3(-.67f, .36f, -.97f), new Vector3(-.67f, .36f, -2.94f),
        });

        private enum Paint
        {
            White, Green, DeepGreen, Glass, GlassHighlight, Rubber, Metal, Asphalt,
            Seat, Rail, Headlight, TailLight, TurnLight, Display,
        }

        private static readonly Color32[] Palette =
        {
            new Color32(226, 230, 221, 255), new Color32(128, 172, 46, 255),
            new Color32(81, 119, 30, 255), new Color32(32, 51, 61, 255),
            new Color32(62, 83, 91, 255), new Color32(28, 30, 29, 255),
            new Color32(159, 165, 159, 255), new Color32(65, 69, 68, 255),
            new Color32(61, 100, 113, 255), new Color32(218, 160, 45, 255),
            new Color32(226, 241, 242, 255), new Color32(213, 40, 36, 255),
            new Color32(235, 138, 36, 255), new Color32(15, 19, 19, 255),
        };

        private const float HalfWidth = 1.25f;
        private const float FrontDoor = 4.13f;
        private const float MiddleDoor = 2f;
        private const float DoorHalfWidth = .49f;
        private const float DoorSlide = .43f;
        private const float WindowSill = 1.32f;
        private const float WindowTop = 2.67f;
        private static Mesh bodyMesh, frontLeavesMesh, rearLeavesMesh;
        private static Material paintMaterial;
        private static Font signFont;
        private Transform frontLeaves, rearLeaves;
        private TextMesh frontSign, rearSign;
        private float opening;
        private bool doorsOpen;

        public static OmskBusView Create(Transform parent, string routeNumber, string destination)
        {
            BuildSharedVisuals();
            var root = new GameObject("Омский автобус");
            root.transform.SetParent(parent, false);
            var bus = root.AddComponent<OmskBusView>();
            bus.Attach("Кузов и салон", bodyMesh);
            bus.frontLeaves = bus.Attach("Передние створки", frontLeavesMesh);
            bus.rearLeaves = bus.Attach("Задние створки", rearLeavesMesh);
            bus.frontSign = bus.MakeSign("Переднее табло", new Vector3(0f, 2.738f, 5.151f), 180f);
            bus.rearSign = bus.MakeSign("Заднее табло", new Vector3(0f, 2.723f, -5.114f), 0f);
            bus.SetRoute(routeNumber, destination);
            return bus;
        }

        public void SetRoute(string routeNumber, string destination)
        {
            gameObject.name = "Омский автобус · " + routeNumber;
            if (frontSign)
            {
                string terminal = (destination ?? string.Empty).Trim();
                if (terminal.Length > 18)
                    terminal = terminal.Substring(0, 17).TrimEnd() + "…";
                frontSign.text = terminal.Length == 0 ? routeNumber : routeNumber + "   " + terminal;
                frontSign.characterSize = Mathf.Min(.16f, 1.74f / Mathf.Max(1, frontSign.text.Length));
            }
            if (rearSign)
                rearSign.text = routeNumber;
        }

        public void SetDoors(bool open) => doorsOpen = open;

        private void LateUpdate()
        {
            float next = Mathf.MoveTowards(opening, doorsOpen ? 1f : 0f, Time.deltaTime * 2.25f);
            if (next == opening)
                return;
            opening = next;
            PaintDoorPose();
        }

        private void OnDisable()
        {
            doorsOpen = false;
            opening = 0f;
            if (frontLeaves)
                PaintDoorPose();
        }

        private void PaintDoorPose()
        {
            float sideways = Mathf.SmoothStep(0f, .082f, Mathf.Clamp01(opening * 4f));
            float along = Mathf.SmoothStep(0f, DoorSlide, Mathf.InverseLerp(.18f, 1f, opening));
            frontLeaves.localPosition = new Vector3(sideways, 0f, along);
            rearLeaves.localPosition = new Vector3(sideways, 0f, -along);
        }

        private Transform Attach(string label, Mesh mesh)
        {
            var child = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(transform, false);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            child.GetComponent<MeshRenderer>().sharedMaterial = paintMaterial;
            // The scheduled bus is a visual proxy. Passenger interaction uses BoardingOffset.
            return child.transform;
        }

        private TextMesh MakeSign(string label, Vector3 position, float yaw)
        {
            if (!signFont)
                return null;
            var child = new GameObject(label, typeof(TextMesh));
            child.transform.SetParent(transform, false);
            child.transform.localPosition = position;
            child.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            TextMesh text = child.GetComponent<TextMesh>();
            text.font = signFont;
            text.fontSize = 44;
            text.fontStyle = FontStyle.Bold;
            text.characterSize = .16f;
            text.color = new Color32(255, 201, 94, 255);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = signFont.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return text;
        }

        private static void BuildSharedVisuals()
        {
            if (bodyMesh && paintMaterial)
                return;
            var colors = new Texture2D(Palette.Length, 1, TextureFormat.RGBA32, false)
            {
                name = "Омский автобус · палитра",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            colors.SetPixels32(Palette);
            colors.Apply(false, true);
            paintMaterial = new Material(RuntimeShaders.Lit())
            {
                name = "Омский автобус · отделка",
                mainTexture = colors,
                enableInstancing = true,
            };
            if (paintMaterial.HasProperty("_BaseMap"))
                paintMaterial.SetTexture("_BaseMap", colors);
            if (paintMaterial.HasProperty("_Smoothness"))
                paintMaterial.SetFloat("_Smoothness", .38f);
            signFont = Resources.Load<Font>("Art/DejaVuSans") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var body = new Shapes();
            BuildBody(body);
            bodyMesh = body.Finish("Омский автобус · кузов");
            frontLeavesMesh = BuildDoorLeaves(true);
            rearLeavesMesh = BuildDoorLeaves(false);
        }

        private static void BuildBody(Shapes body)
        {
            body.Box(Paint.Asphalt, -.99f, .99f, .23f, .36f, -5.03f, 5.03f);
            body.Box(Paint.Asphalt, -1.15f, 1.15f, .31f, .36f, -4.98f, 5.01f);
            body.Box(Paint.White, -1.23f, 1.23f, 2.67f, 2.88f, -5.03f, 5.01f);
            body.Box(Paint.White, -1.06f, 1.06f, 2.84f, 3.00f, -4.88f, 4.85f);
            body.Box(Paint.Metal, -.57f, .57f, 2.98f, 3.17f, -.83f, 1.16f);
            body.Box(Paint.White, -.72f, .72f, 2.96f, 3.08f, -3.83f, -2.12f);

            BodySide(body, -1, -5.02f, 4.99f);
            BodySide(body, 1, -5.02f, MiddleDoor - DoorHalfWidth);
            BodySide(body, 1, MiddleDoor + DoorHalfWidth, FrontDoor - DoorHalfWidth);
            BodySide(body, 1, FrontDoor + DoorHalfWidth, 4.99f);
            foreach (float center in new[] { FrontDoor, MiddleDoor })
            {
                body.Box(Paint.Asphalt, 1.09f, 1.27f, .289f, .36f, center - .49f, center + .49f);
                body.Box(Paint.Rail, 1.27f, 1.29f, .317f, .36f, center - .49f, center + .49f);
                body.Box(Paint.Rail, .98f, 1.028f, .36f, 2.60f, center - .47f, center - .422f);
                body.Box(Paint.Rail, .98f, 1.028f, .36f, 2.60f, center + .422f, center + .47f);
            }

            body.Box(Paint.Green, -1.24f, 1.24f, .36f, 1.33f, 4.93f, 5.16f);
            body.Box(Paint.Rubber, -1.215f, 1.215f, .33f, .52f, 5.13f, 5.21f);
            body.Box(Paint.Glass, -1.20f, 1.20f, 2.63f, 2.90f, 4.97f, 5.12f);
            body.Box(Paint.Display, -1.10f, 1.10f, 2.65f, 2.83f, 5.116f, 5.141f);
            body.Quad(Paint.Glass, new Vector3(-1.17f, 1.33f, 5.158f), new Vector3(1.17f, 1.33f, 5.158f),
                new Vector3(1.11f, 2.648f, 5.106f), new Vector3(-1.11f, 2.648f, 5.106f), Vector3.forward);
            body.Box(Paint.Rubber, -.018f, .018f, 1.34f, 2.64f, 5.10f, 5.16f);
            body.Box(Paint.Rubber, -.47f, .47f, .53f, .70f, 5.16f, 5.179f);
            body.Box(Paint.White, -.23f, .23f, .55f, .65f, 5.179f, 5.192f);
            body.Box(Paint.Asphalt, -.91f, -.15f, .91f, 1.225f, 4.48f, 5.108f);

            body.Box(Paint.Green, -1.24f, 1.24f, .36f, 1.54f, -5.14f, -4.93f);
            body.Box(Paint.Rubber, -1.215f, 1.215f, .34f, .49f, -5.18f, -5.12f);
            body.Box(Paint.White, -1.22f, 1.22f, 2.59f, 2.89f, -5.06f, -4.88f);
            body.Box(Paint.Display, -.41f, .41f, 2.625f, 2.83f, -5.101f, -5.05f);
            body.Quad(Paint.Glass, new Vector3(1.14f, 1.52f, -5.12f), new Vector3(-1.14f, 1.52f, -5.12f),
                new Vector3(-1.10f, 2.60f, -5.07f), new Vector3(1.10f, 2.60f, -5.07f), Vector3.back);
            body.Box(Paint.Rubber, -.48f, .48f, .66f, 1.10f, -5.158f, -5.13f);
            for (var i = 0; i < 5; i++)
            {
                float y = .694f + i * .081f;
                body.Box(Paint.DeepGreen, -.445f, .445f, y, y + .038f, -5.166f, -5.152f);
            }

            foreach (int side in new[] { -1, 1 })
            {
                body.SideBox(Paint.GlassHighlight, side, 1.25f, 1.46f, 1.94f, 1.99f, 4.76f, 4.84f);
                body.SideBox(Paint.Rubber, side, 1.41f, 1.54f, 1.73f, 2.11f, 4.72f, 4.88f);
                body.SideBox(Paint.GlassHighlight, side, 1.43f, 1.52f, 1.75f, 2.09f, 4.704f, 4.728f);
                body.SideBox(Paint.Glass, side, .65f, 1.17f, .63f, .92f, 5.143f, 5.191f);
                body.SideBox(Paint.Headlight, side, .69f, 1.10f, .66f, .798f, 5.193f, 5.211f);
                body.SideBox(Paint.TurnLight, side, .91f, 1.11f, .828f, .883f, 5.192f, 5.209f);
                body.SideBox(Paint.Rubber, side, 1.01f, 1.18f, .57f, 1.40f, -5.155f, -5.124f);
                body.SideBox(Paint.TailLight, side, 1.025f, 1.168f, .65f, 1.09f, -5.177f, -5.143f);
                body.SideBox(Paint.TurnLight, side, 1.025f, 1.168f, 1.14f, 1.32f, -5.171f, -5.143f);
                Wheel(body, side, -3.13f);
                Wheel(body, side, 3.02f);
            }

            foreach (Vector3 seat in SeatOffsets)
            {
                float x = seat.x, z = seat.z;
                body.Box(Paint.Asphalt, x - .154f, x + .154f, .36f, .73f, z - .17f, z + .16f);
                body.Box(Paint.Seat, x - .26f, x + .26f, .73f, .83f, z - .24f, z + .22f);
                body.Box(Paint.Seat, x - .25f, x + .25f, .80f, 1.45f, z - .29f, z - .195f);
                body.Box(Paint.Metal, x - .16f, x + .16f, 1.40f, 1.49f, z - .28f, z - .203f);
            }
            foreach (float side in new[] { -.37f, .37f })
            {
                body.Box(Paint.Rail, side - .022f, side + .022f, 2.47f, 2.514f, -4.30f, 3.20f);
                body.Box(Paint.Rail, side - .021f, side + .021f, .36f, 2.67f, -1.14f, -1.10f);
            }
        }

        private static void BodySide(Shapes body, int side, float from, float to)
        {
            body.SideBox(Paint.Green, side, 1.152f, HalfWidth, .971f, WindowSill, from, to);
            body.SideBox(Paint.White, side, 1.163f, HalfWidth + .004f, 1.266f, WindowSill, from, to);
            // The lower skirt is split by the wheel openings. Dark arch liners sit behind the cutout.
            float edge = from;
            foreach (float wheel in new[] { -3.13f, 3.02f })
            {
                float gapFrom = wheel - .55f, gapTo = wheel + .55f;
                float skirtEnd = Mathf.Min(gapFrom, to);
                body.SideBox(Paint.Green, side, 1.14f, HalfWidth, .367f, .973f, edge, skirtEnd);
                body.SideBox(Paint.Rubber, side, .979f, 1.11f, .36f, .974f,
                    Mathf.Max(from, gapFrom), Mathf.Min(to, gapTo));
                edge = Mathf.Max(edge, gapTo);
            }
            body.SideBox(Paint.Green, side, 1.14f, HalfWidth, .367f, .973f, edge, to);

            body.SideBox(Paint.White, side, 1.15f, HalfWidth, 2.58f, 2.76f, from, to);
            int windows = Mathf.Max(1, Mathf.RoundToInt((to - from) / 1.45f));
            for (var i = 0; i < windows; i++)
            {
                float start = Mathf.Lerp(from, to, (float)i / windows);
                float end = Mathf.Lerp(from, to, (float)(i + 1) / windows);
                body.SideBox(Paint.Rubber, side, 1.15f, HalfWidth, WindowSill, 2.585f, start, start + .075f);
                body.SideFace(Paint.Glass, side, HalfWidth - .005f, WindowSill, 2.58f, start + .073f, end - .048f);
                body.SideFace(Paint.GlassHighlight, side, HalfWidth - .003f, 2.42f, 2.48f, start + .075f, end - .051f);
                body.SideBox(Paint.Rubber, side, 1.15f, HalfWidth, WindowSill, 2.585f, end - .049f, end);
            }
        }

        private static Mesh BuildDoorLeaves(bool forward)
        {
            var doors = new Shapes();
            foreach (float center in new[] { FrontDoor, MiddleDoor })
            {
                float start = forward ? center + .012f : center - DoorHalfWidth;
                float end = forward ? center + DoorHalfWidth : center - .012f;
                doors.Box(Paint.Green, 1.17f, 1.248f, .355f, 1.06f, start, end);
                doors.Box(Paint.Rubber, 1.173f, 1.258f, 1.04f, 2.57f, start, start + .044f);
                doors.Box(Paint.Rubber, 1.173f, 1.258f, 1.04f, 2.57f, end - .044f, end);
                doors.Box(Paint.White, 1.17f, 1.255f, 2.51f, 2.67f, start, end);
                doors.SideFace(Paint.Glass, 1, 1.246f, 1.06f, 2.51f, start + .042f, end - .042f);
                doors.SideFace(Paint.GlassHighlight, 1, 1.248f, 2.38f, 2.42f, start + .043f, end - .043f);
            }
            return doors.Finish(forward ? "Омский автобус · передние створки" : "Омский автобус · задние створки");
        }

        private static void Wheel(Shapes body, int side, float z)
        {
            body.WheelDisc(Paint.Rubber, side, 1.01f, 1.279f, .472f, .46f, z);
            body.WheelDisc(Paint.Metal, side, 1.278f, 1.286f, .267f, .46f, z);
            body.WheelDisc(Paint.Asphalt, side, 1.285f, 1.295f, .170f, .46f, z);
            body.WheelDisc(Paint.Metal, side, 1.294f, 1.31f, .093f, .46f, z);
        }

        private sealed class Shapes
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();
            private readonly List<Vector2> uv = new List<Vector2>();

            internal void Quad(Paint paint, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int start = vertices.Count;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
                    (b, d) = (d, b);
                Vector2 color = new Vector2(((int)paint + .5f) / Palette.Length, .5f);
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(color); uv.Add(color); uv.Add(color); uv.Add(color);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            internal void Box(Paint paint, float x0, float x1, float y0, float y1, float z0, float z1)
            {
                if (x0 >= x1 || y0 >= y1 || z0 >= z1)
                    return;
                var a = new Vector3(x0, y0, z0); var b = new Vector3(x1, y0, z0);
                var c = new Vector3(x1, y0, z1); var d = new Vector3(x0, y0, z1);
                var e = new Vector3(x0, y1, z0); var f = new Vector3(x1, y1, z0);
                var g = new Vector3(x1, y1, z1); var h = new Vector3(x0, y1, z1);
                Quad(paint, d, c, g, h, Vector3.forward);
                Quad(paint, b, a, e, f, Vector3.back);
                Quad(paint, a, d, h, e, Vector3.left);
                Quad(paint, c, b, f, g, Vector3.right);
                Quad(paint, h, g, f, e, Vector3.up);
                Quad(paint, a, b, c, d, Vector3.down);
            }

            internal void SideBox(Paint paint, int side, float minX, float maxX, float y0, float y1, float z0, float z1)
            {
                Box(paint, side > 0 ? minX : -maxX, side > 0 ? maxX : -minX, y0, y1, z0, z1);
            }

            internal void SideFace(Paint paint, int side, float x, float y0, float y1, float z0, float z1)
            {
                if (z0 >= z1)
                    return;
                x *= side;
                Quad(paint, new Vector3(x, y0, z0), new Vector3(x, y1, z0),
                    new Vector3(x, y1, z1), new Vector3(x, y0, z1), Vector3.right * side);
            }

            internal void WheelDisc(Paint paint, int side, float inside, float outside, float radius, float y, float z)
            {
                const int segments = 14;
                var center = new Vector3(side * outside, y, z);
                Vector3 end = center + Vector3.up * radius;
                Vector3 innerOffset = Vector3.right * (side * (inside - outside));
                for (var i = 1; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    Vector3 next = center + new Vector3(0f, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                    Quad(paint, end, next, next + innerOffset, end + innerOffset, (end + next - 2f * center).normalized);
                    int start = vertices.Count;
                    Vector2 color = new Vector2(((int)paint + .5f) / Palette.Length, .5f);
                    vertices.Add(center); vertices.Add(end); vertices.Add(next);
                    uv.Add(color); uv.Add(color); uv.Add(color);
                    triangles.Add(start);
                    triangles.Add(start + (side > 0 ? 1 : 2));
                    triangles.Add(start + (side > 0 ? 2 : 1));
                    end = next;
                }
            }

            internal Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.SetUVs(0, uv);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
