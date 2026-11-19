using System;
using System.Collections.Generic;
using LifeCraftRP;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    internal static class OmskLandmarkStreets
    {
        private static readonly Dictionary<string,Material> Materials = new Dictionary<string,Material>();

        [Serializable]
        private sealed class Layout
        {
            public Route[] mega;
            public Route[] parkMall;
            public Parking[] parking;
        }

        [Serializable]
        private sealed class Route
        {
            public string kind;
            public float[] xz;
        }

        [Serializable]
        private sealed class Parking
        {
            public float[] xz;
            public int[] triangles;
            public float[] marks;
        }

        internal static void Build(Transform root,string id)
        {
            if (id != "mega" && id != "park-mall")
                return;
            var asset = Resources.Load<TextAsset>("Omsk/LandmarkStreets");
            if (!asset)
                return;
            Layout layout;
            try
            {
                layout = JsonUtility.FromJson<Layout>(asset.text);
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
            Route[] routes = id == "mega" ? layout?.mega : layout?.parkMall;
            if (routes == null)
                return;

            var batches = new Dictionary<string,Batch>();
            foreach (var route in routes)
            {
                if (route.xz == null || route.xz.Length < 4 || route.xz.Length%2 != 0)
                    continue;
                var points = new Vector3[route.xz.Length/2];
                for (int i=0;i<points.Length;i++)
                    points[i] = new Vector3(route.xz[2*i]-root.position.x,0,route.xz[2*i+1]-root.position.z);
                if (route.kind == "contact")
                    ContactNetwork(points,batches);
                else
                {
                    bool cycle = route.kind == "cycle";
                    float halfWidth = cycle ? 1.02f : route.kind == "paving" ? 1.16f : .89f;
                    Paving(points,Group(batches,route.kind),halfWidth);
                    if (cycle)
                        AddCycleWayfinding(points,batches);
                }
            }
            if (id == "mega" && layout.parking != null)
                AddParking(root.position,layout.parking,batches);
            foreach (var pair in batches)
                Publish(root,pair.Key,pair.Value);
        }

        private static void AddParking(Vector3 origin,Parking[] parking,Dictionary<string,Batch> batches)
        {
            Batch asphalt = Group(batches,"lot");
            Batch markings = Group(batches,"paint");
            foreach (var parcel in parking)
            {
                int offset = asphalt.Points.Count;
                for (int i=0;i<parcel.xz.Length;i+=2)
                {
                    var point = new Vector3(parcel.xz[i]-origin.x,.019f,parcel.xz[i+1]-origin.z);
                    asphalt.Points.Add(point);
                    asphalt.Coordinates.Add(new Vector2(point.x,point.z));
                }
                foreach (int index in parcel.triangles)
                    asphalt.Indices.Add(index+offset);
                for (int i=0;i<parcel.marks.Length;i+=4)
                {
                    var a = new Vector3(parcel.marks[i]-origin.x,.046f,parcel.marks[i+1]-origin.z);
                    var b = new Vector3(parcel.marks[i+2]-origin.x,.046f,parcel.marks[i+3]-origin.z);
                    markings.Line(a,b,.052f);
                }
            }
        }

        private static void Paving(Vector3[] points,Batch batch,float halfWidth)
        {
            var edges = new Vector3[points.Length];
            for (int i=0;i<points.Length;i++)
            {
                Vector3 incoming = (points[i]-points[Mathf.Max(0,i-1)]).normalized;
                Vector3 outgoing = (points[Mathf.Min(points.Length-1,i+1)]-points[i]).normalized;
                Vector3 along = (incoming+outgoing).normalized;
                Vector3 side = Side(along);
                Vector3 direction = i==0 ? outgoing : incoming;
                float width = halfWidth/Mathf.Max(.69f,Vector3.Dot(side,Side(direction)));
                edges[i] = side*width;
            }
            for (int i=1;i<points.Length;i++)
            {
                Vector3 a = points[i-1]+Vector3.up*.084f, b = points[i]+Vector3.up*.084f;
                batch.Quad(a-edges[i-1],b-edges[i],b+edges[i],a+edges[i-1]);
                batch.Quad(a-edges[i-1]-Vector3.up*.051f,b-edges[i]-Vector3.up*.051f,b-edges[i],a-edges[i-1]);
                batch.Quad(b+edges[i]-Vector3.up*.051f,a+edges[i-1]-Vector3.up*.051f,a+edges[i-1],b+edges[i]);
            }
        }

        private static void AddCycleWayfinding(Vector3[] points,Dictionary<string,Batch> batches)
        {
            var paint = Group(batches,"paint");
            PaintOnRoute(points,16f,48f,(position,along)=>
            {
                Vector3 side = Side(along);
                Vector3 center = position+Vector3.up*.087f;
                Vector3 front = center+along*.50f+side*.07f;
                Vector3 rear = center-along*.52f+side*.07f;
                paint.Circle(front,.262f,.017f);
                paint.Circle(rear,.262f,.017f);
                Vector3 saddle = center+side*.30f-along*.20f;
                Vector3 crank = center-side*.04f-along*.12f;
                paint.Line(rear,saddle,.017f);
                paint.Line(saddle,crank,.017f);
                paint.Line(crank,rear,.017f);
                paint.Line(crank,front,.017f);
                paint.Line(front,center+side*.29f+along*.37f,.017f);
            });
        }

        private static void ContactNetwork(Vector3[] points,Dictionary<string,Batch> batches)
        {
            var metal = Group(batches,"metal");
            var wire = Group(batches,"wire");
            for (int i=1;i<points.Length;i++)
            {
                Vector3 along = (points[i]-points[i-1]).normalized;
                Vector3 side = Side(along);
                for (int direction=-1;direction<=1;direction+=2)
                {
                    Vector3 offset = Vector3.up*5.78f+side*(1.22f+direction*.31f);
                    wire.Tube(points[i-1]+offset,points[i]+offset,.014f,4);
                }
            }
            PaintOnRoute(points,15f,46f,(position,along)=>
            {
                Vector3 side = Side(along);
                Vector3 foot = position+side*7.8f+Vector3.up*.045f;
                Vector3 mast = foot+Vector3.up*7.75f;
                metal.Tube(foot,mast,.083f,7);
                metal.Tube(foot,foot+Vector3.up*.46f,.13f,7);
                Vector3 cross = mast-side*5.31f;
                metal.Tube(mast,cross,.047f,6);
                Vector3 centre = position+side*1.22f+Vector3.up*5.78f;
                wire.Tube(mast-Vector3.up*.88f,centre-side*.31f,.013f,4);
                wire.Tube(cross-Vector3.up*.14f,centre+side*.31f,.013f,4);
            });
        }

        private static void PaintOnRoute(Vector3[] points,float offset,float interval,Action<Vector3,Vector3> emit)
        {
            float remaining = offset;
            for (int i=1;i<points.Length;i++)
            {
                Vector3 delta = points[i]-points[i-1];
                float length = delta.magnitude;
                if (length <= .04f)
                    continue;
                Vector3 along = delta/length;
                while (remaining < length)
                {
                    emit(points[i-1]+along*remaining,along);
                    remaining += interval;
                }
                remaining -= length;
            }
        }

        private static Vector3 Side(Vector3 along) => new Vector3(along.z,0,-along.x);

        private static Batch Group(Dictionary<string,Batch> groups,string style)
        {
            if (!groups.TryGetValue(style,out Batch result))
                groups.Add(style,result = new Batch());
            return result;
        }

        private static Material Paint(string style)
        {
            if (Materials.TryGetValue(style,out Material found) && found)
                return found;

            var ratio = .25f;
            Color color;
            string texture;
            switch (style)
            {
                case "cycle": color = new Color(.57f,.233f,.235f); texture = "Asphalt"; ratio = .44f; break;
                case "paving": color = new Color(.91f,.89f,.83f); texture = "ConcretePavers"; break;
                case "concrete": color = new Color(.78f,.76f,.70f); texture = "WeatheredConcrete"; break;
                case "asphalt": color = new Color(.68f,.67f,.65f); texture = "Asphalt"; break;
                case "lot": color = new Color(.76f,.75f,.72f); texture = "Asphalt"; break;
                case "paint": color = new Color(.81f,.79f,.72f); texture = null; break;
                case "metal": color = new Color(.45f,.47f,.48f); texture = null; break;
                default: color = new Color(.29f,.31f,.32f); texture = null; break;
            }

            var material = new Material(RuntimeShaders.Lit()) { name="Омск · "+style,color=color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor",color);
            if (texture != null)
                StreetMaterials.Apply(material,texture,ratio,ratio);
            Materials[style] = material;
            return material;
        }

        private static void Publish(Transform root,string style,Batch data)
        {
            if (data.Indices.Count == 0)
                return;
            var obj = new GameObject("Подходы · "+style,typeof(MeshRenderer),typeof(MeshFilter));
            obj.transform.SetParent(root,false);
            var mesh = new Mesh { name=obj.name };
            if (data.Points.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(data.Points);
            mesh.SetUVs(0,data.Coordinates);
            mesh.SetTriangles(data.Indices,0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Paint(style);
            renderer.shadowCastingMode = style=="metal" ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mesh.UploadMeshData(true);
        }

        private sealed class Batch
        {
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal readonly List<Vector2> Coordinates = new List<Vector2>();
            internal readonly List<int> Indices = new List<int>();

            internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int n = Points.Count;
                Points.Add(a); Points.Add(b); Points.Add(c); Points.Add(d);
                Coordinates.Add(new Vector2(a.x,a.z)); Coordinates.Add(new Vector2(b.x,b.z));
                Coordinates.Add(new Vector2(c.x,c.z)); Coordinates.Add(new Vector2(d.x,d.z));
                Indices.Add(n); Indices.Add(n+1); Indices.Add(n+2);
                Indices.Add(n); Indices.Add(n+2); Indices.Add(n+3);
            }

            internal void Tube(Vector3 a,Vector3 b,float radius,int sides)
            {
                Vector3 axis = (b-a).normalized;
                Vector3 right = Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 other = Vector3.Cross(axis,right);
                for (int i=0;i<sides;i++)
                {
                    Vector3 p = Point(i), q = Point(i+1);
                    Quad(a+p,a+q,b+q,b+p);
                }

                Vector3 Point(int step)
                {
                    float angle = 2f*Mathf.PI*step/sides;
                    return radius*(right*Mathf.Cos(angle)+other*Mathf.Sin(angle));
                }
            }

            internal void Line(Vector3 a,Vector3 b,float halfWidth)
            {
                Vector3 width = Side((b-a).normalized)*halfWidth;
                Quad(a-width,b-width,b+width,a+width);
            }

            internal void Circle(Vector3 position,float radius,float thickness)
            {
                Vector3 last = position+Vector3.right*radius;
                for (int i=1;i<=12;i++)
                {
                    float angle = 2f*Mathf.PI*i/12;
                    var next = position+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                    Line(last,next,thickness);
                    last=next;
                }
            }
        }
    }
}
