using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    internal static class AvatarEyewear
    {
        internal static void Build(Transform head, Material frame, Material lens, Material hardware,
            bool anatomicalHead)
        {
            float height = anatomicalHead ? .124f : .139f;
            float depth = anatomicalHead ? .114f : .100f;

            Bar("Перемычка очков",head,new Vector3(-.014f,height+.008f,depth),
                new Vector3(.014f,height+.008f,depth),.0068f,.006f,frame);
            var stud = ActorArt.Paint(new Color(.71f,.74f,.77f),.68f,.55f);
            for (int i=-1;i<=1;i+=2)
            {
                float side = i;
                Transform mount = ActorArt.Joint("Оправа",head,
                    new Vector3(side*.044f,height,depth-.004f));
                mount.localRotation = Quaternion.Euler(0,side*12f,-side*3f);
                Rim(mount,frame);
                Lens(mount,lens);
                Vector3 hinge = new Vector3(side*.081f,height+.012f,depth-.017f);
                float earHeight = anatomicalHead ? .124f : .126f;
                float earDepth = anatomicalHead ? -.023f : -.004f;
                var joint = new Vector3(side*.088f,earHeight+.004f,.014f);
                var tip = new Vector3(side*.087f,earHeight-.007f,earDepth);
                Bar("Дужка очков",head,hinge,joint,.0065f,.0050f,frame);
                Bar("Заушник",head,joint,tip,.0056f,.0047f,frame);
                ActorArt.Form(PrimitiveType.Sphere,"Штифт оправы",mount,
                    new Vector3(side*.0345f,.013f,.0080f),
                    new Vector3(.0034f,.0020f,.0015f),hardware);
                Vector3 lobe = anatomicalHead ? new Vector3(side*.089f,.094f,-.018f)
                    : new Vector3(side*.093f,.084f,.009f);
                ActorArt.Form(PrimitiveType.Sphere,"Серебряная серьга",head,lobe,
                    new Vector3(.0064f,.0061f,.005f),stud);
            }
        }

        private static Vector2 Shape(float angle)
        {
            float a = Mathf.Cos(angle), b = Mathf.Sin(angle);
            float x = Mathf.Sign(a)*Mathf.Pow(Mathf.Abs(a),.43f)*(1f+.08f*b);
            float y = Mathf.Sign(b)*Mathf.Pow(Mathf.Abs(b),.50f);
            return new Vector2(x,y);
        }

        private static void Rim(Transform mount, Material material)
        {
            const int sides = 32;
            var vertices = new List<Vector3>(sides*4);
            var indices = new List<int>(sides*24);
            for (int j=0;j<4;j++)
            for (int i=0;i<sides;i++)
            {
                Vector2 p = Shape(2f*Mathf.PI*i/sides);
                float width = j==0 || j==1 ? .0403f : .0346f;
                float height = j==0 || j==1 ? .0247f : .0198f;
                float z = j==1 || j==2 ? .0056f : -.0046f;
                vertices.Add(new Vector3(p.x*width,p.y*height,z-.0023f*p.x*p.x));
            }

            for (int j=0;j<4;j++)
            for (int i=0;i<sides;i++)
            {
                int next = (i+1)%sides;
                int a = j*sides+i, b = j*sides+next;
                int c = ((j+1)%4)*sides+i, d = ((j+1)%4)*sides+next;
                indices.AddRange(new[]{a,b,d,a,d,c});
            }
            ActorArt.Custom("Полированная чёрная оправа",mount,vertices,indices,material);
        }

        private static void Lens(Transform mount, Material material)
        {
            const int sides = 32;
            var vertices = new List<Vector3>(sides*2+1) { new Vector3(0,0,.009f) };
            var indices = new List<int>(sides*9);
            for (int ring=1;ring<=2;ring++)
            for (int i=0;i<sides;i++)
            {
                Vector2 p = Shape(2f*Mathf.PI*i/sides);
                float scale = ring==1 ? .66f : 1f;
                vertices.Add(new Vector3(p.x*.0354f*scale,p.y*.0206f*scale,
                    .009f-scale*scale*(.0032f+.0023f*p.x*p.x)));
                int next = (i+1)%sides;
                if (ring==1)
                    indices.AddRange(new[]{0,i+1,next+1});
                else
                    indices.AddRange(new[]{i+1,i+1+sides,next+1+sides,i+1,next+1+sides,next+1});
            }
            ActorArt.Custom("Выпуклая дымчатая линза",mount,vertices,indices,material);
        }

        private static void Bar(string name, Transform head, Vector3 a, Vector3 b,
            float width,float thickness,Material material)
        {
            var bar = ActorArt.Form(PrimitiveType.Cube,name,head,(a+b)*.5f,
                new Vector3(width,(b-a).magnitude+.002f,thickness),material);
            bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up,b-a);
        }
    }
}
