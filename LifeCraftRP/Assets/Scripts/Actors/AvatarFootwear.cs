using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    /// <summary>Original layered running shoe built in metres around each ankle joint.</summary>
    internal static class AvatarFootwear
    {
        private static readonly float[] Stations =
            { -.080f, -.069f, -.046f, -.012f, .026f, .061f, .093f, .128f, .162f, .186f, .200f, .207f };
        private static readonly float[] Width =
            { .018f, .043f, .054f, .055f, .054f, .062f, .071f, .077f, .071f, .058f, .038f, .014f };
        private static readonly float[] Upper =
            { -.023f, .012f, .043f, .031f, .031f, .021f, .004f, -.009f, -.021f, -.029f, -.042f, -.053f };
        private const float Join = -.062f;

        internal static void Build(Transform ankle, float side, Material rubber, Material shoeGrey, Material shoeWhite)
        {
            var shoe = ActorArt.Joint("Кроссовок · профиль стопы", ankle, Vector3.zero);
            var tread = ActorArt.Paint(new Color(.104f,.115f,.124f),.19f);
            var heel = ActorArt.Paint(new Color(.224f,.25f,.261f),.25f);
            var stripe = ActorArt.Paint(new Color(.067f,.093f,.108f),.33f);
            var trim = ActorArt.Paint(new Color(.54f,.579f,.595f),.31f);

            Solid("Подошва · нижний слой",shoe,side,tread,
                (z,u) => Join-.041f + ToeRise(z) + (.010f*u));
            Solid("Подошва · формованная пена",shoe,side,rubber,
                (z,u) => Join-.033f + ToeRise(z) + (.040f*u));
            Solid("Подошва · светлые ребра",shoe,side,shoeWhite,
                (z,u) => Join-.031f + ToeRise(z)*.86f + (.008f*u));

            Band("Сетчатый верх",shoe,side,shoeGrey,Stations[0],Stations[Stations.Length-1],
                0,Mathf.PI,0,26,18);
            Band("Носок · накладка",shoe,side,trim,.148f,.207f,0,Mathf.PI,.0015f,12,15);
            Band("Пятка · усиление",shoe,side,heel,-.080f,-.040f,0,Mathf.PI,.002f,12,14);
            Band("Язычок",shoe,side,shoeGrey,-.013f,.099f,.93f,2.21f,.003f,16,9);

            for (var flank = -1; flank <= 1; flank += 2)
            {
                float lower = flank > 0 ? .02f : Mathf.PI-.41f;
                float top = flank > 0 ? .41f : Mathf.PI-.02f;
                Band("Боковая накладка",shoe,side,shoeWhite,-.046f,.175f,lower,top,.0017f,23,6);
                lower = flank > 0 ? .67f : Mathf.PI-1.05f;
                top = flank > 0 ? 1.05f : Mathf.PI-.67f;
                Band("Люверсы · планка",shoe,side,trim,-.014f,.111f,lower,top,.0025f,15,5);
                Accent(shoe,side,flank, stripe);
            }
            for (int i = 0; i < 6; i++)
            {
                float z = .020f+i*.014f;
                Vector3 left = Along(z,.91f,side,.007f);
                Vector3 right = Along(z+.008f,2.23f,side,.007f);
                ActorArt.Bar("Плоский шнурок",shoe,left,right,.0036f,.0037f,shoeWhite);
                ActorArt.Form(PrimitiveType.Sphere,"Люверс",shoe,left,
                    new Vector3(.009f,.006f,.009f),heel);
                ActorArt.Form(PrimitiveType.Sphere,"Люверс",shoe,right,
                    new Vector3(.009f,.006f,.009f),heel);
            }
            Tread(shoe,side,tread);
        }

        private static float ToeRise(float z) => Mathf.Clamp01((z-.135f)/.072f)*.020f;
        private static float Center(float z, float side) =>
            side*.013f*Mathf.Sin(Mathf.Clamp01((z+.032f)/.239f)*Mathf.PI);

        private static float Read(float[] values,float z)
        {
            for (int i=1;i<Stations.Length;i++)
            {
                if (z > Stations[i])
                    continue;
                return Mathf.Lerp(values[i-1],values[i],(z-Stations[i-1])/(Stations[i]-Stations[i-1]));
            }
            return values[values.Length-1];
        }

        private static Vector3 Along(float z,float angle,float side,float offset)
        {
            float bottom = Join + ToeRise(z)*.72f;
            float height = Mathf.Max(.004f,Read(Upper,z)-bottom);
            return new Vector3(Center(z,side)+Mathf.Cos(angle)*(Read(Width,z)+offset),
                bottom+Mathf.Sin(angle)*(height+offset),z);
        }

        private static void Solid(string name,Transform shoe,float side,Material material,
            System.Func<float,float,float> y)
        {
            var points = new List<Vector3>();
            var faces = new List<int>();
            const int columns = 12;
            for(int j=0;j<Stations.Length;j++)
            {
                float z = Stations[j];
                float w = Width[j];
                for (int i=0;i<columns;i++)
                {
                    float angle = i*2*Mathf.PI/columns;
                    float c = Mathf.Cos(angle);
                    float width = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c),.53f);
                    float tall = Mathf.Sign(Mathf.Sin(angle))*Mathf.Pow(Mathf.Abs(Mathf.Sin(angle)),.53f);
                    points.Add(new Vector3(Center(z,side) + w*width, y(z,.5f+.5f*tall), z));
                }
            }
            for(int j=0;j<Stations.Length-1;j++)
            for(int i=0;i<columns;i++)
            {
                int a=j*columns+i, b=j*columns+(i+1)%columns;
                faces.AddRange(new[]{a,b,a+columns,b,b+columns,a+columns});
            }
            int end=(Stations.Length-1)*columns;
            for(int i=1;i+1<columns;i++)
                faces.AddRange(new[]{0,i+1,i,end,end+i,end+i+1});
            ActorArt.Custom(name,shoe,points,faces,material);
        }

        private static void Band(string name,Transform shoe,float side,Material material,
            float z0,float z1,float theta0,float theta1,float offset,int cuts,int radial)
        {
            var points = new List<Vector3>();
            var faces = new List<int>();
            for(int j=0;j<=cuts;j++)
            for(int i=0;i<=radial;i++)
                points.Add(Along(Mathf.Lerp(z0,z1,j/(float)cuts),
                    Mathf.Lerp(theta0,theta1,i/(float)radial),side,offset));
            int row=radial+1;
            for(int j=0;j<cuts;j++)
            for(int i=0;i<radial;i++)
            {
                int a=j*row+i;
                faces.AddRange(new[]{a,a+1,a+row,a+1,a+row+1,a+row});
            }
            ActorArt.Custom(name,shoe,points,faces,material);
        }

        private static void Accent(Transform shoe,float side,int flank,Material material)
        {
            var points = new List<Vector3>();
            var faces = new List<int>();
            for(int i=0;i<=16;i++)
            {
                float t=i/16f;
                float z=Mathf.Lerp(-.042f,.150f,t);
                float mid=.34f + .26f*Mathf.Sin(t*Mathf.PI);
                float half=.030f + .11f*Mathf.Sin(t*Mathf.PI);
                if(flank<0)
                    mid=Mathf.PI-mid;
                points.Add(Along(z,mid-half,side,.0032f));
                points.Add(Along(z,mid+half,side,.0032f));
                if(i>0)
                {
                    int a=(i-1)*2;
                    faces.AddRange(new[]{a,a+1,a+2,a+1,a+3,a+2});
                }
            }
            ActorArt.Custom(flank*side>0 ? "Графитовая вставка · внешняя" : "Графитовая вставка · внутренняя",
                shoe,points,faces,material);
        }

        private static void Tread(Transform shoe,float side,Material material)
        {
            var points = new List<Vector3>();
            var faces = new List<int>();
            int[] cube = { 0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,1,5,6,1,6,2,
                2,6,7,2,7,3,3,7,4,3,4,0 };
            for(int row=0;row<10;row++)
            {
                float z=Mathf.Lerp(-.060f,.178f,row/9f);
                float w=Read(Width,z);
                for(int column=-1;column<=1;column++)
                {
                    if((row==3||row==4)&&column==0)
                        continue;
                    float x=Center(z,side)+column*w*.53f;
                    float dx=w*.19f,dz=.0088f;
                    float slope=column*.003f;
                    float bottom=Join-.046f+ToeRise(z);
                    int start=points.Count;
                    for(int layer=0;layer<=1;layer++)
                    {
                        float y=bottom+layer*.008f;
                        points.Add(new Vector3(x-dx,y,z-dz+slope));
                        points.Add(new Vector3(x+dx,y,z-dz-slope));
                        points.Add(new Vector3(x+dx,y,z+dz-slope));
                        points.Add(new Vector3(x-dx,y,z+dz+slope));
                    }
                    foreach(int index in cube)
                        faces.Add(start+index);
                }
            }
            ActorArt.Custom("Протектор · резиновые сегменты",shoe,points,faces,material);
        }
    }
}
