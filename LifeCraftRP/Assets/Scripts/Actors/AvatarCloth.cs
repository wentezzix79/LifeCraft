using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    internal static class AvatarCloth
    {
        // Profile samples encode height, side drift, offset forward, width and depth.
        internal readonly struct Profile
        {
            internal readonly float Y,X,Z,W,D;
            internal Profile(float y,float x,float z,float width,float depth)
            {
                Y=y; X=x; Z=z; W=width; D=depth;
            }
        }

        private static readonly Dictionary<Material,Material> Leather = new Dictionary<Material,Material>();
        private static readonly Dictionary<Material,Material> Denim = new Dictionary<Material,Material>();

        internal static Material Textured(Material source,bool leather)
        {
            var variants = leather ? Leather : Denim;
            if (variants.TryGetValue(source,out var existing) && existing)
                return existing;
            string name = leather ? "LCLeatherDetail" : "LCDenimDetail";
            var material = new Material(source) { name = name };
            var color = Resources.Load<Texture2D>("Art/"+name);
            var normal = Resources.Load<Texture2D>("Art/"+name+"Normal");
            if (color)
                material.mainTexture = color;
            if (leather)
            {
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness",.41f);
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness",.38f);
            }
            if (normal && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap",normal);
                material.SetFloat("_BumpScale",leather ? .39f : .50f);
                material.EnableKeyword("_NORMALMAP");
            }
            variants[source] = material;
            return material;
        }

        internal static void Jacket(Transform body,Material material)
        {
            Material cotton = ActorArt.Paint(new Color(.025f,.024f,.025f),.12f);
            Tailor("Чёрная футболка",body,null,cotton,1f,.0024f,
                new[]
                {
                    new Profile(-.070f,0f,0f,.158f,.091f),
                    new Profile(-.015f,0f,0f,.156f,.099f),
                    new Profile(.170f,0f,0f,.155f,.099f),
                    new Profile(.287f,0f,.001f,.167f,.107f),
                    new Profile(.395f,0f,-.006f,.176f,.112f),
                    new Profile(.450f,0f,-.008f,.111f,.090f),
                    new Profile(.475f,0f,-.004f,.072f,.072f),
                });
            Tailor("Расстёгнутая кожаная куртка",body,null,Textured(material,true),
                1f,.0042f,GetJacket(),openFront:true);
            ActorArt.Loft("Горловина футболки",body,
                new[] {new Vector3(0,.459f,-.005f),new Vector3(0,.471f,-.004f),
                    new Vector3(0,.475f,-.004f)},
                new[] {new Vector2(.081f,.075f),new Vector2(.077f,.072f),
                    new Vector2(.072f,.070f)},24,cotton);
        }

        internal static void Sleeve(Transform upper,Transform lower,float side,Material material)
        {
            Tailor("Кожаная куртка · цельный рукав",upper,lower,Textured(material,true),side,.0054f,
                new[]
                {
                    new Profile(-.496f,0f,.001f,.040f,.035f),
                    new Profile(-.480f,.001f,.001f,.043f,.038f),
                    new Profile(-.455f,.006f,.005f,.050f,.041f),
                    new Profile(-.407f,.009f,.006f,.053f,.049f),
                    new Profile(-.346f,.009f,.002f,.058f,.050f),
                    new Profile(-.263f,.014f,-.003f,.064f,.060f),
                    new Profile(-.189f,.011f,-.002f,.071f,.064f),
                    new Profile(-.117f,.011f,.001f,.073f,.064f),
                    new Profile(-.048f,.004f,-.003f,.078f,.070f),
                    new Profile(.023f,-.010f,-.005f,.065f,.060f),
                    new Profile(.053f,-.024f,-.004f,.054f,.052f),
                    new Profile(.077f,-.040f,-.003f,.027f,.034f),
                });
        }

        internal static void Jeans(Transform upper,Transform lower,float side,Material material)
        {
            Tailor("Широкие джинсы · цельная штанина",upper,lower,Textured(material,false),side,.0069f,
                new[]
                {
                    new Profile(-.842f,.009f,.016f,.077f,.074f),
                    new Profile(-.829f,.010f,.017f,.079f,.080f),
                    new Profile(-.813f,.011f,.022f,.091f,.087f),
                    new Profile(-.794f,.013f,.016f,.082f,.079f),
                    new Profile(-.768f,.017f,.014f,.100f,.093f),
                    new Profile(-.741f,.019f,.002f,.091f,.089f),
                    new Profile(-.706f,.021f,-.003f,.105f,.095f),
                    new Profile(-.662f,.023f,-.006f,.105f,.096f),
                    new Profile(-.588f,.025f,-.004f,.104f,.096f),
                    new Profile(-.506f,.021f,0f,.102f,.094f),
                    new Profile(-.408f,.016f,.005f,.095f,.091f),
                    new Profile(-.306f,.013f,.008f,.092f,.089f),
                    new Profile(-.205f,.015f,.011f,.092f,.096f),
                    new Profile(-.104f,.007f,.005f,.084f,.098f),
                    new Profile(-.027f,.004f,0f,.080f,.094f),
                    new Profile(.025f,0f,-.001f,.078f,.092f),
                },denim:true);
        }

        internal static void TailoringDetails(Transform body,Material edge,Material leather,Material hardware)
        {
            var yoke=new[]
            {
                new Vector3(-.178f,.388f,-.082f),new Vector3(-.110f,.356f,-.112f),
                new Vector3(0f,.350f,-.130f),new Vector3(.106f,.357f,-.116f),
                new Vector3(.176f,.391f,-.081f),
            };
            for(int k=0;k<yoke.Length-1;k++)
                ActorArt.Bar("Кокетка · шов спины",body,yoke[k],yoke[k+1],.0045f,.0045f,edge);
            for (int side=-1;side<=1;side+=2)
            {
                Vector3 shoulder = new Vector3(side*.115f,.466f,-.046f);
                ActorArt.Bar("Плечевой шов",body,shoulder,new Vector3(side*.184f,.429f,-.056f),
                    .0045f,.0050f,edge);
                ActorArt.Bar("Клапан кожаного кармана",body,new Vector3(side*.103f,.082f,.116f),
                    new Vector3(side*.154f,.139f,.099f),.017f,.0044f,leather);
                ActorArt.Bar("Молния кармана",body,new Vector3(side*.115f,.087f,.120f),
                    new Vector3(side*.160f,.142f,.105f),.0035f,.0045f,hardware);
            }
            SpreadCollar(body,ActorArt.Paint(leather.color,.34f),edge);
        }

        private static void SpreadCollar(Transform body,Material leather,Material edge)
        {
            var backPoints = new List<Vector3>();
            var backFaces = new List<int>();
            const int spread = 20;
            for (int i=0;i<=spread;i++)
            {
                float a = 2.18f + i*(5.67f-2.18f)/spread;
                float c = Mathf.Cos(a), v = Mathf.Sin(a);
                backPoints.Add(new Vector3(.064f*c,.515f,-.019f+.053f*v));
                backPoints.Add(new Vector3(.098f*c,.476f-.006f*Mathf.Abs(c),-.022f+.079f*v));
                if (i>0)
                    AddQuad(backFaces,(i-1)*2,(i-1)*2+1,i*2+1,i*2);
            }
            DoubleSided("Воротник · отложная часть сзади",body,backPoints,backFaces,leather);

            for (int side=-1;side<=1;side+=2)
            {
                var fold = new[]
                {
                    new Vector3(side*.054f,.516f,.028f),
                    new Vector3(side*.065f,.488f,.061f),
                    new Vector3(side*.080f,.451f,.091f),
                    new Vector3(side*.086f,.417f,.108f),
                };
                var outer = new[]
                {
                    new Vector3(side*.091f,.498f,-.026f),
                    new Vector3(side*.120f,.469f,.029f),
                    new Vector3(side*.151f,.431f,.083f),
                    new Vector3(side*.144f,.395f,.115f),
                };
                var points = new List<Vector3>();
                var faces = new List<int>();
                for (int i=0;i<fold.Length;i++)
                {
                    points.Add(fold[i]);
                    points.Add((fold[i]+outer[i])*.5f+new Vector3(0,.007f,.008f));
                    points.Add(outer[i]);
                    if (i>0)
                    {
                        int p=(i-1)*3;
                        AddQuad(faces,p,p+3,p+4,p+1);
                        AddQuad(faces,p+1,p+4,p+5,p+2);
                        ActorArt.Bar("Прострочка воротника",body,outer[i-1],outer[i],.0028f,.003f,edge);
                    }
                }
                DoubleSided("Кожаная куртка · отложной лацкан",body,points,faces,leather);
            }
        }

        private static void AddQuad(List<int> faces,int a,int b,int c,int d)
        {
            faces.AddRange(new[]{a,b,c,a,c,d});
        }

        private static void DoubleSided(string name,Transform body,List<Vector3> points,
            List<int> faces,Material material)
        {
            int offset = points.Count;
            int original = faces.Count;
            for (int i=0;i<offset;i++)
                points.Add(points[i]+new Vector3(0f,0f,-.00045f));
            for (int i=0;i<original;i+=3)
            {
                faces.Add(faces[i]+offset);
                faces.Add(faces[i+2]+offset);
                faces.Add(faces[i+1]+offset);
            }
            ActorArt.Custom(name,body,points,faces,material);
        }

        private static void FrontEdges(Transform body,List<Vector3> shell,int columns,int rows)
        {
            var burgundy = ActorArt.Paint(new Color(.114f,.014f,.025f),.23f);
            var seam = ActorArt.Paint(new Color(.037f,.032f,.030f),.32f);
            var zip = ActorArt.Paint(new Color(.167f,.142f,.111f),.45f,.22f);
            for(int side=-1;side<=1;side+=2)
            {
                // The sampled shell seam and its piping share the same path.
                int column = side < 0 ? 0 : columns-1;
                var lining = new List<Vector3>();
                var stitches = new List<Vector3>();
                var teeth = new List<Vector3>();
                var liningFaces = new List<int>();
                var stitchFaces = new List<int>();
                var toothFaces = new List<int>();
                for(int i=0;i<rows;i++)
                {
                    Vector3 p=shell[i*columns+column];
                    float width = Mathf.Lerp(.0045f,.0080f,Mathf.Clamp01((p.y+.04f)/.5f));
                    lining.Add(p+new Vector3(side*.0004f,0,.0018f));
                    lining.Add(p+new Vector3(-side*width,0,-.0054f));
                    stitches.Add(p+new Vector3(side*.0022f,0,.0034f));
                    stitches.Add(p+new Vector3(-side*.0008f,0,.0034f));
                    if(i==0)
                        continue;
                    int line=(i-1)*2;
                    AddQuad(liningFaces,line,line+1,line+3,line+2);
                    AddQuad(stitchFaces,line,line+1,line+3,line+2);
                    if (i%2 != 0 || p.y > .397f)
                        continue;
                    int tooth=teeth.Count;
                    teeth.Add(p+new Vector3(-side*.0035f,-.0016f,.0037f));
                    teeth.Add(p+new Vector3(side*.0003f,-.0016f,.0037f));
                    teeth.Add(p+new Vector3(side*.0003f,.0011f,.0037f));
                    teeth.Add(p+new Vector3(-side*.0035f,.0011f,.0037f));
                    AddQuad(toothFaces,tooth,tooth+1,tooth+2,tooth+3);
                }
                DoubleSided("Узкая бордовая подкладка",body,lining,liningFaces,burgundy);
                DoubleSided("Кант борта",body,stitches,stitchFaces,seam);
                DoubleSided("Зубья расстёгнутой молнии",body,teeth,toothFaces,zip);
            }
        }

        private static Profile[] GetJacket() => new[]
        {
            new Profile(-.067f,0f,0f,.173f,.108f),
            new Profile(-.044f,0f,0f,.183f,.115f),
            new Profile(-.020f,0f,0f,.184f,.119f),
            new Profile(.034f,0f,0f,.196f,.130f),
            new Profile(.110f,-.001f,.002f,.183f,.123f),
            new Profile(.178f,0f,.003f,.183f,.118f),
            new Profile(.248f,.002f,.004f,.190f,.124f),
            new Profile(.319f,.001f,.002f,.202f,.120f),
            new Profile(.388f,0f,-.004f,.224f,.128f),
            new Profile(.428f,0f,-.008f,.216f,.116f),
            new Profile(.468f,0f,-.009f,.172f,.094f),
            new Profile(.503f,0f,-.013f,.087f,.068f),
        };

        private static void Tailor(string name,Transform upper,Transform lower,Material material,
            float side,float amplitude,Profile[] profile,bool openFront=false,bool denim=false)
        {
            const int slices = 36;
            const int samples = 4;
            const int columns = slices+1;
            int total = (profile.Length-1)*samples+1;
            var points = new List<Vector3>(total*columns+2);
            var uvs = new List<Vector2>(total*columns+2);
            var triangles = new List<int>((total+1)*slices*6);
            var weights = new List<BoneWeight>(total*columns+2);
            float pivot = lower != null ? lower.localPosition.y : 0f;
            for (int row=0;row<total;row++)
            {
                int i=Mathf.Min(row/samples,profile.Length-2);
                float t=(row-i*samples)/(float)samples;
                Profile a=profile[i],b=profile[i+1];
                float y=Mathf.Lerp(a.Y,b.Y,t),x=Mathf.Lerp(a.X,b.X,t)*side,z=Mathf.Lerp(a.Z,b.Z,t);
                float w=Mathf.Lerp(a.W,b.W,t),d=Mathf.Lerp(a.D,b.D,t);
                float v=row/(float)(total-1);
                float blend=lower==null?0f:Mathf.SmoothStep(0f,1f,Mathf.Clamp01((pivot+.085f-y)/.17f));
                var weight=new BoneWeight { boneIndex0=0,boneIndex1=lower!=null?1:0,weight0=1f-blend,weight1=blend };
                for(int column=0;column<columns;column++)
                {
                    float u=column/(float)slices;
                    float angle=u*2f*Mathf.PI;
                    if (openFront)
                    {
                        float gap = .125f + .52f*Mathf.SmoothStep(0f,1f,
                            Mathf.Clamp01((y-.178f)/.326f)) + .018f*Mathf.Sin(y*23f);
                        angle = Mathf.PI*.5f + gap + u*(2f*Mathf.PI-2f*gap);
                    }
                    float cosine=Mathf.Cos(angle),sine=Mathf.Sin(angle);
                    float ripples = lower != null && !denim ? 1f-.55f*v : 1f;
                    float fold=amplitude*(.59f*Mathf.Sin(angle*4f+side*v*5.2f)
                        +.25f*Mathf.Sin(angle*3f-v*32f+side)
                        +.13f*ripples*Mathf.Sin(angle*8f+v*67f));
                    float gather = denim
                        ? .0038f*Mathf.Sin((y+.842f)*275f+1.9f*Mathf.Sin(angle))
                            *Mathf.Exp(-Mathf.Max(0f,y+.829f)*21f)
                        : amplitude*.81f*Mathf.Sin(133f*v+1.5f*Mathf.Sin(angle))*Mathf.Exp(-v*28f);
                    float sideSeam=lower==null?0f:amplitude*.31f*Mathf.Pow(Mathf.Max(0f,side*cosine),26f);
                    float hemDrift=denim ? .010f*Mathf.Sin(angle+side*.8f+v*10f)
                        *Mathf.Exp(-v*10f) : 0f;
                    points.Add(new Vector3(x+(w+fold+gather+sideSeam)*cosine,y+hemDrift,
                        z+(d+fold+gather+sideSeam)*sine));
                    uvs.Add(new Vector2(u*2.6f,v*3.1f));
                    weights.Add(weight);
                    if (row==0 || column==slices)
                        continue;
                    int p=(row-1)*columns+column,q=p+1,r=p+columns,s=r+1;
                    triangles.Add(p);triangles.Add(r);triangles.Add(q);
                    triangles.Add(q);triangles.Add(r);triangles.Add(s);
                }
            }
            if (openFront)
            {
                FrontEdges(upper,points,columns,total);
            }
            else
            {
                AddCap(points,uvs,weights,triangles,profile[0],side,0,slices,false);
                AddCap(points,uvs,weights,triangles,profile[profile.Length-1],side,(total-1)*columns,slices,true);
            }
            var obj=new GameObject(name);
            obj.transform.SetParent(upper,false);
            var mesh=new Mesh {name=name};
            mesh.SetVertices(points);
            mesh.SetTriangles(triangles,0);
            mesh.SetUVs(0,uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (lower==null)
            {
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                obj.AddComponent<MeshRenderer>().sharedMaterial=material;
                return;
            }
            mesh.boneWeights=weights.ToArray();
            mesh.bindposes=new []
            {
                upper.worldToLocalMatrix*obj.transform.localToWorldMatrix,
                lower.worldToLocalMatrix*obj.transform.localToWorldMatrix,
            };
            var skin=obj.AddComponent<SkinnedMeshRenderer>();
            skin.rootBone=upper;
            skin.bones=new[]{upper,lower};
            skin.sharedMesh=mesh;
            skin.sharedMaterial=material;
            skin.localBounds=new Bounds(mesh.bounds.center,mesh.bounds.size+Vector3.one*.36f);
        }

        private static void AddCap(List<Vector3> points,List<Vector2> uvs,List<BoneWeight> weights,
            List<int> triangles,Profile profile,float side,int offset,int slices,bool top)
        {
            int center=points.Count;
            points.Add(new Vector3(profile.X*side,profile.Y,profile.Z));
            uvs.Add(new Vector2(.5f,top?1f:0f));
            weights.Add(weights[offset]);
            for(int j=0;j<slices;j++)
            {
                triangles.Add(center);
                triangles.Add(offset+j+(top?1:0));
                triangles.Add(offset+j+(top?0:1));
            }
        }
    }
}
