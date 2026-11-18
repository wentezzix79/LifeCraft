using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    internal static class AvatarHair
    {
        private const float Turn = 2f * Mathf.PI;
        private const float Crown = .161f;
        private static readonly Vector3 Ellipsoid = new Vector3(.092f,.100f,.105f);

        internal static void Build(Transform head, Material roots, Material curls, Material highlights)
        {
            BuildCap(head,roots);
            var vertices = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            var faces = new[] { new List<int>(), new List<int>(), new List<int>() };
            int[] counts = { 7, 11, 16, 18, 21 };
            for (int row = 0; row < counts.Length; row++)
            for (int i = 0; i < counts[row]; i++)
            {
                float seed = 17.31f * (i + 1) + 11.25f * row;
                float azimuth = (i + row*.407f + .37f*Mathf.Sin(seed*.43f)) * Turn / counts[row];
                float polar = (.11f + .188f*row + .045f*Mathf.Sin(seed)) * HairlineAngle(azimuth);
                Vector3 root = Shell(azimuth,polar);
                Vector3 normal = ShellNormal(azimuth,polar);
                Vector3 across = new Vector3(-Mathf.Sin(azimuth),0f,Mathf.Cos(azimuth));
                Vector3 along = Vector3.Cross(normal,across).normalized;
                float width = .011f + .0037f*Mathf.Sin(seed*.68f);
                float length = .019f + .009f*Mathf.Sin(seed*.59f);
                float turn = (Mathf.Sin(seed*.21f) > -.15f ? 1f : -1f) * (4.7f + 1.2f*Mathf.Sin(seed*.82f));
                float bias = .007f + .005f*Mathf.Cos(seed*.57f);
                float start = seed*.32f;
                float endLift = .001f + .005f*Mathf.Cos(seed*.91f);
                int tint = (i+2*row)%11 == 0 ? 2 : (i+row)%5 == 0 ? 0 : 1;

                Vector3 Path(float t, float strand)
                {
                    float angle = start + turn*t;
                    float arc = Mathf.Sin(Mathf.PI*t);
                    float size = (.75f+.25f*arc)*(1f-.25f*t);
                    return root + normal*(-.004f + (.009f+width*.30f)*arc + endLift*t + strand*.0008f)
                        + across*(width*size*(Mathf.Cos(angle)-Mathf.Cos(start)) + bias*t + strand*.0032f)
                        + along*(width*size*(Mathf.Sin(angle)-Mathf.Sin(start)) + length*t);
                }

                AddStrand(vertices[tint],faces[tint],t => Path(t,0),normal,.0043f,18,6);
                int threadTint = tint == 0 || (i+row)%4 == 0 ? 1 : 2;
                AddStrand(vertices[threadTint],faces[threadTint],t => Path(t,-.83f),normal,.0020f,16,5);
                if ((i+row)%3 == 0)
                    AddStrand(vertices[0],faces[0],t => Path(t,1.15f),normal,.0017f,14,5);
            }

            AddFringe(vertices,faces);
            Material[] shades = { roots,curls,highlights };
            for (int i = 0; i < shades.Length; i++)
                ActorArt.Custom("Волосы · кудри " + (i + 1),head,vertices[i],faces[i],shades[i]);
        }

        private static void AddFringe(List<Vector3>[] vertices,List<int>[] faces)
        {
            for (int i=0;i<12;i++)
            {
                float key = 6.91f*i;
                float x = -.082f + .0143f*i + .0045f*Mathf.Sin(key);
                float y = .204f + .011f*Mathf.Cos((i-5f)*.29f) + .0048f*Mathf.Sin(key*.71f);
                float z = .081f - .018f*Mathf.Abs(x)/.082f;
                float hang = .041f + .016f*Mathf.Sin(key*.62f) + (i==4 || i==8 ? .011f : 0);
                float loop = .0075f + .0035f*Mathf.Sin(key*.53f);
                float sweep = .012f + .008f*Mathf.Sin(key*.34f);
                float twist = (i%3 == 1 ? -1f : 1f) * (5.0f + .75f*Mathf.Sin(key*.95f));

                Vector3 Guide(float t, float strand)
                {
                    float lift = Mathf.Sin(Mathf.PI*t);
                    float a = key + twist*t;
                    return new Vector3(
                        x+sweep*t+loop*lift*Mathf.Cos(a)+strand*.0035f,
                        y-hang*t+loop*.92f*lift*Mathf.Sin(a),
                        z+.027f*lift+.015f*t+.004f*lift*Mathf.Sin(a+.56f)+strand*.001f);
                }

                Vector3 normal = Vector3.forward;
                AddStrand(vertices[1],faces[1],t => Guide(t,0),normal,.0045f,22,6);
                AddStrand(vertices[i%4 == 1 ? 1 : 2],faces[i%4 == 1 ? 1 : 2],
                    t => Guide(t,-.76f),normal,.0020f,18,5);
                AddStrand(vertices[0],faces[0],t => Guide(t,1.05f),normal,.0017f,16,5);
            }
        }

        private static float HairlineAngle(float azimuth)
        {
            float front = Mathf.Sin(azimuth);
            return 1.84f - .53f*front + .045f*Mathf.Sin(5f*azimuth) + .038f*Mathf.Cos(9f*azimuth);
        }

        private static Vector3 Shell(float azimuth,float polar)
        {
            Vector3 normal = ShellNormal(azimuth,polar);
            float ripple = (.0039f*Mathf.Sin(12f*azimuth + 5.1f*polar)
                + .0025f*Mathf.Cos(19f*azimuth - 7.3f*polar)
                + .0015f*Mathf.Sin(31f*azimuth + 13.6f*polar))*Mathf.Sin(polar);
            return new Vector3(Ellipsoid.x*normal.x,Crown+Ellipsoid.y*normal.y,
                -.008f+Ellipsoid.z*normal.z)+normal*ripple;
        }

        private static Vector3 ShellNormal(float azimuth,float polar)
        {
            float sp = Mathf.Sin(polar);
            return new Vector3(sp*Mathf.Cos(azimuth),Mathf.Cos(polar),sp*Mathf.Sin(azimuth));
        }

        private static void BuildCap(Transform head, Material roots)
        {
            const int rows = 28, sides = 80;
            const int rowSize = sides+1;
            var vertices = new List<Vector3>(rows*rowSize+1) { new Vector3(0,Crown+Ellipsoid.y,-.008f) };
            var uv = new List<Vector2>(vertices.Capacity) { new Vector2(.5f,.01f) };
            var faces = new List<int>(rows*sides*6);
            for (int ring=1;ring<=rows;ring++)
            for (int i=0;i<=sides;i++)
            {
                float azimuth = i*Turn/sides;
                float y = ring/(float)rows;
                vertices.Add(Shell(azimuth,y*HairlineAngle(azimuth)));
                uv.Add(new Vector2(i/(float)sides,y));
                if (i==sides)
                    continue;
                int a = 1+(ring-1)*rowSize+i;
                int b = a+1;
                if (ring==1)
                    faces.AddRange(new[]{0,b,a});
                else
                    faces.AddRange(new[]{a-rowSize,b-rowSize,b,a-rowSize,b,a});
            }
            Material detail = RootMaterial(roots);
            ActorArt.Custom("Волосы · корни",head,vertices,faces,detail,uv);
        }

        private static Material rootMaterial;

        private static Material RootMaterial(Material source)
        {
            if (rootMaterial)
                return rootMaterial;
            var fibers = Resources.Load<Texture2D>("Art/AvatarHairGrain");
            if (!fibers)
                return source;
            rootMaterial = new Material(source) { name = "LC · Волосы", mainTexture = fibers };
            return rootMaterial;
        }

        private static void AddStrand(List<Vector3> vertices,List<int> faces,
            System.Func<float,Vector3> guide, Vector3 normal,float broadness,int steps,int sides)
        {
            int offset = vertices.Count;
            for (int ring=0;ring<=steps;ring++)
            {
                float t = ring/(float)steps;
                Vector3 axis = (guide(Mathf.Min(1f,t+.014f))-guide(Mathf.Max(0f,t-.014f))).normalized;
                Vector3 u = Vector3.Cross(axis,normal).normalized;
                if (u.magnitude < .001f)
                    u = Vector3.Cross(axis,Vector3.right).normalized;
                Vector3 v = Vector3.Cross(axis,u).normalized;
                float thickness = broadness*(.35f+.65f*Mathf.Sin(Mathf.PI*(.16f+t*.80f)))*(1f-.88f*t*t*t);
                Vector3 position = guide(t);
                for (int i=0;i<sides;i++)
                {
                    float angle = i*Turn/sides;
                    vertices.Add(position + thickness*(Mathf.Cos(angle)*u + .69f*Mathf.Sin(angle)*v));
                    if (ring == 0)
                        continue;
                    int a = offset+(ring-1)*sides+i;
                    int b = offset+(ring-1)*sides+(i+1)%sides;
                    faces.AddRange(new[]{a,b,a+sides,b,b+sides,a+sides});
                }
            }
        }
    }
}
