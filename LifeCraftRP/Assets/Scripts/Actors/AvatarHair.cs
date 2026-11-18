using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    /// <summary>Close-fitting dark roots with layered ringlets, finer strands and a swept fringe.</summary>
    internal static class AvatarHair
    {
        private const float Turn = 2f * Mathf.PI;
        private const float Crown = .166f;
        private static readonly Vector3 Ellipsoid = new Vector3(.086f,.093f,.094f);

        internal static void Build(Transform head, Material roots, Material curls, Material highlights)
        {
            BuildCap(head,roots);
            var vertices = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            var faces = new[] { new List<int>(), new List<int>(), new List<int>() };
            int[] counts = { 8, 13, 17, 23, 26 };
            for (int row = 0; row < counts.Length; row++)
            for (int i = 0; i < counts[row]; i++)
            {
                float key = 19.13f * (i + 1) + 7.22f * row;
                float azimuth = (i + row*.391f + .35f*Mathf.Sin(key*.31f)) * Turn / counts[row];
                float f = Mathf.Max(0f,Mathf.Sin(azimuth));
                float t = .103f + .195f*row + .046f*Mathf.Sin(key);
                float polar = t * HairlineAngle(azimuth);
                Vector3 root = Shell(azimuth,polar);
                Vector3 normal = new Vector3(Mathf.Cos(azimuth)*Mathf.Sin(polar),Mathf.Cos(polar),
                    Mathf.Sin(azimuth)*Mathf.Sin(polar)).normalized;
                Vector3 across = new Vector3(-Mathf.Sin(azimuth),0f,Mathf.Cos(azimuth));
                Vector3 along = Vector3.Cross(normal,across).normalized;
                float width = .0085f + .0047f*Mathf.Cos(key*.81f);
                float length = .019f + .007f*Mathf.Sin(key*.67f);
                float direction = Mathf.Sin(key*.37f) > -.25f ? 1f : -1f;
                float sweep = .018f + .009f*Mathf.Cos(key*.337f);
                float fall = row == 4 ? .009f*f*(1f+.4f*Mathf.Sin(key)) : 0f;
                int tint = (i+row*4)%13 == 0 ? 2 : (i+row)%7 == 0 ? 0 : 1;

                Vector3 Path(float u, float thread)
                {
                    float coil = key + u*direction*(Turn*(.63f+.30f*Mathf.Sin(key*.37f)));
                    float curlSize = (.62f+.43f*Mathf.Sin(Mathf.PI*u))*(1f-.26f*u);
                    float floatHeight = -.0039f + (.0083f+width*.26f)*Mathf.Sin(Mathf.PI*u*.91f);
                    return root + normal*(floatHeight + .0003f*thread)
                        + across*(width*curlSize*Mathf.Cos(coil) + u*sweep + thread*.0034f)
                        + along*(width*curlSize*Mathf.Sin(coil)*.88f + length*(u-.23f))
                        + Vector3.up*(-fall*u*u);
                }
                AddStrand(vertices[tint], faces[tint], u => Path(u,0f),
                    normal,.0031f+.00058f*Mathf.Cos(key),13,5,0);
                if ((i+row)%3 != 0)
                {
                    int threadTint = tint == 1 && (i+row)%4 == 0 ? 2 : 1;
                    float side = i%2 == 0 ? -.86f : .83f;
                    AddStrand(vertices[threadTint],faces[threadTint],u => Path(u,side),
                        normal,.00145f,11,4,1);
                }
            }
            AddFringe(vertices,faces);
            Material[] shades = { roots,curls,highlights };
            for (int i = 0; i < shades.Length; i++)
                ActorArt.Custom("Кудрявые волосы · слой "+(i + 1),head,vertices[i],faces[i],shades[i]);
        }

        private static void AddFringe(List<Vector3>[] vertices,List<int>[] faces)
        {
            // Extra swept locks at the forehead use custom guides to lie above the temples and lenses.
            for (int i=0;i<11;i++)
            {
                float id = i;
                float x = -.073f + id*.0128f + .0034f*Mathf.Sin(id*4.37f);
                float y = .195f + .013f*Mathf.Cos((id-4f)*.41f) + .0038f*Mathf.Sin(id*3.11f);
                float z = .081f - .009f*Mathf.Abs(x)/.074f;
                float sweep = .013f + .0036f*Mathf.Sin(id*6.7f);
                float hang = .029f+.011f*Mathf.Sin(id*2.74f) + .0046f*Mathf.Cos(id*.53f);
                float offset = .0055f+.0016f*Mathf.Cos(id*3.71f);
                float theta = id*.87f;
                float twist = (i%3==1 ? -1f : 1f)*(4.4f+.51f*Mathf.Sin(id));
                int tint = i%5==2 ? 2 : 1;
                Vector3 Guide(float u, float separation)
                {
                    float size = Mathf.Sin(Mathf.PI*u);
                    float angle = theta + twist*u;
                    return new Vector3(
                        x+sweep*u+offset*size*Mathf.Cos(angle)+separation,
                        y-hang*u+offset*.78f*size*Mathf.Sin(angle),
                        z+.015f*size+.0103f*u+.0037f*size*Mathf.Sin(angle+.67f));
                }
                AddStrand(vertices[tint],faces[tint],u => Guide(u,0f),
                    new Vector3(0,0,1),.0039f,16,6,0);
                AddStrand(vertices[1],faces[1],u => Guide(u,-.0031f),
                    new Vector3(0,0,1),.0017f,13,4,1);
            }
        }

        private static float HairlineAngle(float azimuth)
        {
            float front = Mathf.Sin(azimuth);
            return 1.75f - .48f*front + .045f*Mathf.Sin(3f*azimuth) + .040f*Mathf.Cos(7f*azimuth);
        }

        private static Vector3 Shell(float azimuth,float polar)
        {
            float sp = Mathf.Sin(polar);
            return new Vector3(Ellipsoid.x*sp*Mathf.Cos(azimuth),
                Crown + Ellipsoid.y*Mathf.Cos(polar),
                -.012f + Ellipsoid.z*sp*Mathf.Sin(azimuth));
        }

        private static void BuildCap(Transform head, Material roots)
        {
            const int rows = 20, sides = 72;
            var vertices = new List<Vector3>(rows*sides+1) { new Vector3(0f,Crown+Ellipsoid.y,-.012f) };
            var faces = new List<int>();
            for (int ring=1;ring<=rows;ring++)
            for (int i=0;i<sides;i++)
            {
                float angle = i*Turn/sides;
                float polar = ring*HairlineAngle(angle)/rows;
                Vector3 p = Shell(angle,polar);
                float rootWave = .0017f*Mathf.Sin(19f*angle+ring*.94f)
                    +.0014f*Mathf.Cos(33f*angle+ring*.53f);
                float ripple = rootWave*Mathf.Sin(polar);
                p.x += ripple*Mathf.Cos(angle);
                p.y += ripple*.72f;
                p.z += ripple*Mathf.Sin(angle);
                vertices.Add(p);
                int a=1+(ring-1)*sides+i;
                int b=1+(ring-1)*sides+(i+1)%sides;
                if (ring==1)
                    faces.AddRange(new[]{0,b,a});
                else
                    faces.AddRange(new[]{a-sides,b-sides,b,a-sides,b,a});
            }
            ActorArt.Custom("Волосы · естественная линия роста",head,vertices,faces,roots);
        }

        private static void AddStrand(List<Vector3> vertices,List<int> faces,
            System.Func<float,Vector3> guide, Vector3 normal, float broadness,int steps,int sides,int narrow)
        {
            int offset=vertices.Count;
            for (int ring=0;ring<=steps;ring++)
            {
                float t = ring/(float)steps;
                Vector3 axis = (guide(Mathf.Min(1f,t+.013f))-guide(Mathf.Max(0f,t-.013f))).normalized;
                Vector3 u = Vector3.Cross(axis,normal).normalized;
                Vector3 v = Vector3.Cross(axis,u).normalized;
                float taper = 1f-.81f*t*t*t;
                float thickness = (.0004f+broadness*Mathf.Sin(Mathf.PI*(.20f+t*.74f)))*taper;
                Vector3 position = guide(t);
                for (int i=0;i<sides;i++)
                {
                    float angle=i*Turn/sides;
                    vertices.Add(position+thickness*(Mathf.Cos(angle)*u+Mathf.Sin(angle)*v*(narrow==0?.76f:.68f)));
                    if (ring == 0)
                        continue;
                    int a = offset + (ring-1)*sides+i;
                    int b = offset + (ring-1)*sides+(i+1)%sides;
                    faces.AddRange(new[]{a,b,a+sides,b,b+sides,a+sides});
                }
            }
        }
    }
}
