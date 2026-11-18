using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    /// <summary>Streams material-batched OpenStreetMap geometry around the local player.</summary>
    [DisallowMultipleComponent]
    public sealed class OmskWorld : MonoBehaviour
    {
        public const string Attribution = "© OpenStreetMap contributors · ODbL";
        public const string CopyrightUrl = "https://www.openstreetmap.org/copyright";
        public const float TileSize = 500f;
        public const double OriginLongitude = 73.3935;
        public const double OriginLatitude = 54.9726;
        private const double EastMeters = 63987.4;
        private const double NorthMeters = 111321.2;
        public static readonly Rect WorldBounds = Rect.MinMaxRect(
            (float)((73.15 - OriginLongitude) * EastMeters),
            (float)((54.82 - OriginLatitude) * NorthMeters),
            (float)((73.64 - OriginLongitude) * EastMeters),
            (float)((55.17 - OriginLatitude) * NorthMeters));
        public static readonly Rect StarterSite = Rect.MinMaxRect(-120f, -100f, 120f, 110f);

        [SerializeField, Range(1, 4)] private int desktopRadius = 2;
        [SerializeField, Range(1, 3)] private int androidRadius = 1;
        [SerializeField, Min(.05f)] private float refreshSeconds = .22f;
        public Transform target;

        private static readonly string[] Palette = {
            "#416F8E", "#687851", "#465C43", "#827952", "#838277",
            "#585D5D", "#51565B", "#9A998B", "#70635B", "#C7C1A7",
            "#ADAAA1", "#B2A99E", "#9C7361", "#898E90", "#736F69",
            "#445968", "#594938", "#57734C", "#777C6A"
        };
        // Named districts are representative points for wayfinding.
        private static readonly (string Name, double Lon, double Lat)[] Districts = {
            ("Центр", 73.371529, 54.991375),
            ("Привокзальный", 73.3870, 54.9412),
            ("Рабочие", 73.419435, 54.942835),
            ("Чкаловск", 73.4731016, 54.9778085),
            ("Старая Московка", 73.4712567, 54.9247624),
            ("Новая Московка", 73.4503245, 54.9123408),
            ("Левобережье", 73.2898522, 54.9871166),
            ("Нефтяники", 73.2792131, 55.0328394),
            ("Амурский посёлок", 73.4122585, 55.0241154),
            ("Кировск", 73.334493, 54.9433196),
            ("Правобережье", 73.401, 54.970)
        };

        [Serializable]
        private sealed class AdministrativeData
        {
            public AdministrativeArea[] districts;
        }

        [Serializable]
        private sealed class AdministrativeArea
        {
            public string name;
            public AdministrativePolygon[] polygons;
        }

        [Serializable]
        private sealed class AdministrativePolygon
        {
            public Ring[] rings;
        }

        [Serializable]
        private sealed class Ring
        {
            public float[] xz;
        }

        private static AdministrativeData administration;
        private static bool administrationRead;
        private readonly HashSet<Vector2Int> manifest = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> failed = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
        private Material[] materials;
        private GameObject ground;
        private Mesh groundMesh;
        private ResourceRequest request;
        private Vector2Int requested;
        private Vector2Int center;
        private float refreshAt;
        private int radius;
        private bool reducedDetail;

        private sealed class Tile
        {
            public GameObject Object;
            public Mesh Display;
            public GameObject Decorations;
            public Mesh DecorationMesh;
            public MeshRenderer Renderer;
            public Mesh Collision;
            public MeshCollider Collider;
        }

        private sealed class Group
        {
            public byte Style;
            public Vector3[] Points;
            public int[] Indices;
            public bool Solid => Style >= 10 && Style <= 14;
            public bool Detail => Style == 9 || Style == 15 || Style == 16 || Style == 17;
        }

        public string CurrentDistrict => DistrictName(FocusPosition());
        public int LoadedTileCount => tiles.Count;

        public static Texture2D LoadMapTexture()
        {
            return Resources.Load<Texture2D>("Omsk/map");
        }

        public static Vector2 MapPosition(Vector3 position)
        {
            return new Vector2(Mathf.InverseLerp(WorldBounds.xMin, WorldBounds.xMax, position.x),
                               Mathf.InverseLerp(WorldBounds.yMin, WorldBounds.yMax, position.z));
        }

        public static Vector2 Coordinates(Vector3 position)
        {
            return new Vector2((float)(OriginLongitude + position.x / EastMeters),
                               (float)(OriginLatitude + position.z / NorthMeters));
        }

        public static string DistrictName(Vector3 position)
        {
            string result = "Омск";
            double nearest = 3500 * 3500;
            foreach (var district in Districts)
            {
                double dx = (district.Lon - OriginLongitude) * EastMeters - position.x;
                double dz = (district.Lat - OriginLatitude) * NorthMeters - position.z;
                double distance = dx * dx + dz * dz;
                if (distance < nearest)
                {
                    nearest = distance;
                    result = district.Name;
                }
            }
            return result;
        }

        public static string AdministrativeDistrict(Vector3 position)
        {
            if (!administrationRead)
            {
                var asset = Resources.Load<TextAsset>("Omsk/districts");
                if (asset)
                {
                    administration = JsonUtility.FromJson<AdministrativeData>(asset.text);
                    Resources.UnloadAsset(asset);
                }
                administrationRead = true;
            }
            if (administration?.districts == null)
                return "Омск";
            foreach (var district in administration.districts)
            {
                foreach (var polygon in district.polygons)
                {
                    if (polygon.rings.Length == 0 || !Contains(position, polygon.rings[0].xz))
                        continue;
                    bool inHole = false;
                    for (int r = 1; r < polygon.rings.Length; r++)
                        inHole |= Contains(position, polygon.rings[r].xz);
                    if (!inHole)
                        return district.name;
                }
            }
            return "Окрестности Омска";
        }

        private static bool Contains(Vector3 p, float[] ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 2; i < ring.Length; j = i, i += 2)
            {
                float zi = ring[i + 1];
                float zj = ring[j + 1];
                if ((zi > p.z) != (zj > p.z) &&
                    p.x < ring[i] + (p.z - zi) * (ring[j] - ring[i]) / (zj - zi))
                    inside = !inside;
            }
            return inside;
        }

        public void SetViewDistance(bool low)
        {
            reducedDetail = low || Application.isMobilePlatform;
            radius = low ? 1 : Application.isMobilePlatform ? androidRadius : desktopRadius;
            refreshAt = 0f;
        }

        private void Awake()
        {
            SetViewDistance(false);
            MakeMaterials();
            MakeGround();
            var index = Resources.Load<TextAsset>("Omsk/tiles");
            if (!index)
            {
                Debug.LogError("Omsk/tiles is missing. Import the bundled Omsk GIS tiles.", this);
                return;
            }
            foreach (var line in index.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('_');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                    manifest.Add(new Vector2Int(x, y));
            }
            Resources.UnloadAsset(index);
            center = TileAt(FocusPosition());
            var start = Resources.Load<TextAsset>(ResourcePath(center));
            if (start)
                Consume(center, start);
        }

        private void Update()
        {
            if (Time.unscaledTime < refreshAt && request == null)
                return;
            center = TileAt(FocusPosition());
            if (request != null)
            {
                if (!request.isDone)
                    return;
                var asset = request.asset as TextAsset;
                request = null;
                if (asset)
                {
                    if (Distance(requested, center) <= radius + 1)
                        Consume(requested, asset);
                    else
                        Resources.UnloadAsset(asset);
                }
                else
                {
                    failed.Add(requested);
                }
            }
            CullDistant();
            Vector2Int candidate = default;
            int nearest = int.MaxValue;
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    var key = center + new Vector2Int(x, z);
                    var d = x * x + z * z;
                    if (d >= nearest || tiles.ContainsKey(key) || !manifest.Contains(key) || failed.Contains(key))
                        continue;
                    nearest = d;
                    candidate = key;
                }
            }
            if (nearest != int.MaxValue)
            {
                requested = candidate;
                request = Resources.LoadAsync<TextAsset>(ResourcePath(requested));
            }
            refreshAt = Time.unscaledTime + refreshSeconds;
        }

        private static Vector2Int TileAt(Vector3 p)
        {
            return new Vector2Int(Mathf.FloorToInt((p.x + TileSize * .5f) / TileSize),
                                Mathf.FloorToInt((p.z + TileSize * .5f) / TileSize));
        }

        private static int Distance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        private Vector3 FocusPosition()
        {
            if (target)
                return target.position;
            var camera = Camera.main;
            return camera ? camera.transform.position : Vector3.zero;
        }

        private static string ResourcePath(Vector2Int key)
        {
            return "Omsk/" + key.x + "_" + key.y;
        }

        private void MakeMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
                shader = Shader.Find("Standard");
            if (!shader)
                shader = Shader.Find("Unlit/Color");
            materials = new Material[Palette.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                ColorUtility.TryParseHtmlString(Palette[i], out var color);
                var m = new Material(shader) { name = "Omsk/GIS " + i, color = color };
                m.enableInstancing = true;
                if (m.HasProperty("_Smoothness"))
                    m.SetFloat("_Smoothness", i == 0 || i == 15 ? .6f : .12f);
                if (m.HasProperty("_Metallic"))
                    m.SetFloat("_Metallic", i == 15 ? .24f : 0f);
                materials[i] = m;
            }
        }

        private void MakeGround()
        {
            ground = new GameObject("Омск · поверхность");
            ground.transform.SetParent(transform, false);
            float x0 = WorldBounds.xMin, x1 = WorldBounds.xMax;
            float z0 = WorldBounds.yMin, z1 = WorldBounds.yMax;
            groundMesh = new Mesh
            {
                name = "Omsk base",
                vertices = new[] {
                    new Vector3(x0, 0, z0), new Vector3(x0, 0, z1),
                    new Vector3(x1, 0, z1), new Vector3(x1, 0, z0)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            groundMesh.RecalculateNormals();
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = materials[18];
            var collider = ground.AddComponent<BoxCollider>();
            collider.center = new Vector3(WorldBounds.center.x, -.5f, WorldBounds.center.y);
            collider.size = new Vector3(WorldBounds.width, 1f, WorldBounds.height);
        }

        private static List<Group> ReadGroups(byte[] asset)
        {
            using (var data = new MemoryStream(asset, false))
            using (var gzip = new GZipStream(data, CompressionMode.Decompress))
            using (var r = new BinaryReader(gzip))
            {
                if (r.ReadUInt32() != 0x3157434C)
                    throw new InvalidDataException("Invalid Omsk mesh signature.");
                int count = r.ReadUInt16();
                if (count > Palette.Length - 1)
                    throw new InvalidDataException("Invalid Omsk material count.");
                var result = new List<Group>(count);
                for (var i = 0; i < count; i++)
                {
                    byte style = r.ReadByte();
                    int points = r.ReadInt32();
                    int indices = r.ReadInt32();
                    if (style >= Palette.Length - 1 || points <= 0 || points > 2000000 ||
                        indices <= 0 || indices > 5000000 || indices % 3 != 0)
                        throw new InvalidDataException("Invalid Omsk mesh counts.");
                    var group = new Group { Style = style, Points = new Vector3[points], Indices = new int[indices] };
                    for (int p = 0; p < points; p++)
                        group.Points[p] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int j = 0; j < indices; j++)
                    {
                        var index = r.ReadInt32();
                        if ((uint)index >= (uint)points)
                            throw new InvalidDataException("Invalid Omsk vertex index.");
                        group.Indices[j] = index;
                    }
                    result.Add(group);
                }
                return result;
            }
        }

        private void Consume(Vector2Int key, TextAsset asset)
        {
            var tile = new Tile();
            try
            {
                var groups = ReadGroups(asset.bytes);
                var collisionPoints = new List<Vector3>();
                var faces = new List<int>();
                foreach (var group in groups)
                {
                    if (!group.Solid)
                        continue;
                    foreach (var index in group.Indices)
                        faces.Add(index + collisionPoints.Count);
                    collisionPoints.AddRange(group.Points);
                }
                tile.Object = new GameObject("OSM " + key.x + "," + key.y);
                tile.Object.transform.SetParent(transform, false);
                tile.Object.transform.localPosition = new Vector3(key.x * TileSize, 0, key.y * TileSize);
                tile.Display = MakeDisplay(tile.Object, groups, false, out tile.Renderer);
                if (groups.Exists(group => group.Detail))
                {
                    tile.Decorations = new GameObject("Фасады и растительность");
                    tile.Decorations.transform.SetParent(tile.Object.transform, false);
                    tile.DecorationMesh = MakeDisplay(tile.Decorations, groups, true, out var detailRenderer);
                    detailRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                if (faces.Count > 0)
                {
                    tile.Collision = new Mesh
                    {
                        name = tile.Object.name + " collider",
                        indexFormat = Format(collisionPoints.Count)
                    };
                    tile.Collision.SetVertices(collisionPoints);
                    tile.Collision.SetTriangles(faces, 0, true);
                    tile.Collider = tile.Object.AddComponent<MeshCollider>();
                }
                ApplyDistance(tile, Distance(key, center));
                tiles.Add(key, tile);
            }
            catch (Exception error)
            {
                DestroyTile(tile);
                failed.Add(key);
                Debug.LogError("Omsk tile " + key + ": " + error.Message, this);
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
        }

        private Mesh MakeDisplay(GameObject targetObject, List<Group> groups, bool detail, out MeshRenderer renderer)
        {
            var points = new List<Vector3>();
            var activeMaterials = new List<Material>();
            var partIndices = new List<int[]>();
            foreach (var group in groups)
            {
                if (group.Detail != detail)
                    continue;
                int offset = points.Count;
                points.AddRange(group.Points);
                for (int j = 0; j < group.Indices.Length; j++)
                    group.Indices[j] += offset;
                partIndices.Add(group.Indices);
                activeMaterials.Add(materials[group.Style]);
            }
            var mesh = new Mesh { name = targetObject.name, indexFormat = Format(points.Count) };
            mesh.SetVertices(points);
            mesh.subMeshCount = partIndices.Count;
            for (int i = 0; i < partIndices.Count; i++)
                mesh.SetTriangles(partIndices[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            targetObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = targetObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = activeMaterials.ToArray();
            return mesh;
        }

        private void ApplyDistance(Tile tile, int distance)
        {
            tile.Object.SetActive(distance <= radius);
            if (tile.Decorations)
                tile.Decorations.SetActive(distance <= (reducedDetail ? 0 : 1));
            tile.Renderer.shadowCastingMode = reducedDetail ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (tile.Collider)
            {
                var desired = distance <= 1 ? tile.Collision : null;
                if (tile.Collider.sharedMesh != desired)
                    tile.Collider.sharedMesh = desired;
            }
        }

        private static IndexFormat Format(int vertexCount)
        {
            return vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        }

        private void CullDistant()
        {
            var expired = new List<Vector2Int>();
            foreach (var item in tiles)
            {
                int distance = Distance(item.Key, center);
                if (distance > radius + 1)
                    expired.Add(item.Key);
                else
                    ApplyDistance(item.Value, distance);
            }
            foreach (var key in expired)
            {
                DestroyTile(tiles[key]);
                tiles.Remove(key);
            }
        }

        private static void DestroyTile(Tile tile)
        {
            if (tile.Object)
                Destroy(tile.Object);
            if (tile.Display)
                Destroy(tile.Display);
            if (tile.DecorationMesh)
                Destroy(tile.DecorationMesh);
            if (tile.Collision)
                Destroy(tile.Collision);
        }

        private void OnDestroy()
        {
            foreach (var item in tiles)
                DestroyTile(item.Value);
            if (request != null && request.isDone && request.asset)
                Resources.UnloadAsset(request.asset);
            if (ground)
                Destroy(ground);
            if (groundMesh)
                Destroy(groundMesh);
            if (materials == null)
                return;
            foreach (var m in materials)
                Destroy(m);
        }
    }
}
