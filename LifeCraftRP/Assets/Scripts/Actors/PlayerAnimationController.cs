using UnityEngine;

namespace LifeCraftRP
{
    /// <summary>Articulates the original in-project avatar. The render hierarchy stays separate from collision.</summary>
    public sealed class PlayerAnimationController : MonoBehaviour
    {
        private static readonly Color Jacket = new Color(.024f,.022f,.026f);
        private static readonly Color Pants = new Color(.035f,.033f,.037f);
        private static readonly Color Rubber = new Color(.846f,.856f,.846f);
        private static readonly Color SkinTone = new Color(.72f,.51f,.396f);
        private static readonly Color Hair = new Color(.257f,.177f,.111f);
        private static readonly Color WorkBlue = new Color(.105f,.296f,.75f);
        private static readonly Color WorkLime = new Color(.79f,.87f,.27f);
        private static readonly int RelaxedIdle = Animator.StringToHash("Base Layer.Relaxed Idle");

        private readonly Transform[] shoulders = new Transform[2];
        private readonly Transform[] elbows = new Transform[2];
        private readonly Transform[] hips = new Transform[2];
        private readonly Transform[] knees = new Transform[2];
        private readonly Transform[] ankles = new Transform[2];
        private Transform body;
        private Transform head;
        private GameObject box;
        private GameObject phone;
        private GameObject vest;
        private GameObject bag;
        private float phase;
        private float stepAmount;
        private float animationSpeed;
        private float workUntil;
        private float punchUntil;
        private float enterUntil;
        private float exitUntil;
        private float doorUntil;
        private float pickUpUntil;
        private float landUntil;
        private string currentState;
        private float landAmount;
        private bool carrying;
        private bool onPhone;
        private bool firstPerson;
        private bool lastGrounded = true;

        public Animator Animator { get; private set; }
        public Transform Visual { get; private set; }
        public bool Carrying => carrying;

        internal void Build()
        {
            Visual = ActorArt.Joint("Avatar · LIFE CRAFT",transform,Vector3.zero);
            Animator = Visual.gameObject.AddComponent<Animator>();
            Animator.applyRootMotion = false;
            Animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("LifeCraftActors/Avatar");
            body = ActorArt.Joint("Body", Visual, new Vector3(0f,.95f,0f));

            Material skin = ActorArt.Paint(SkinTone,.29f);
            Material hair = ActorArt.Paint(Hair,.24f);
            Material copper = ActorArt.Paint(new Color(.405f,.288f,.188f),.23f);
            Material blonde = ActorArt.Paint(new Color(.515f,.383f,.262f),.24f);
            Material jacket = ActorArt.Paint(Jacket,.59f);
            Material leatherEdge = ActorArt.Paint(new Color(.046f,.043f,.042f),.47f);
            Material pants = ActorArt.Paint(Pants,.26f);
            Material rubber = ActorArt.Paint(Rubber,.20f);
            Material shoeGrey = ActorArt.Paint(new Color(.40f,.414f,.414f),.24f);
            Material shoeWhite = ActorArt.Paint(new Color(.83f,.826f,.766f),.31f);
            Material frame = ActorArt.Paint(new Color(.012f,.012f,.016f),.76f);
            Material lens = ActorArt.Paint(new Color(.028f,.025f,.024f),.86f,.13f);
            Material silver = ActorArt.Paint(new Color(.81f,.80f,.75f),.53f,.70f);
            Material trim = ActorArt.Paint(new Color(.135f,.112f,.089f),.40f);
            Material webbing = ActorArt.Paint(new Color(.045f,.041f,.039f),.19f);

            GameObject Form(PrimitiveType type, string name, Transform joint,
                float x, float y, float z, float w, float h, float d, Material material)
                => ActorArt.Form(type,name,joint,new Vector3(x,y,z),new Vector3(w,h,d),material);

            AvatarCloth.Jacket(body,jacket);
            AvatarCloth.TailoringDetails(body,leatherEdge,jacket,trim);
            ActorArt.Loft("Джинсы · пояс",body,
                new[]{new Vector3(0,-.082f,0f),new Vector3(0,-.049f,0f),
                    new Vector3(0,.022f,0f)},
                new[]{new Vector2(.158f,.094f),new Vector2(.167f,.098f),new Vector2(.164f,.098f)},24,pants);
            bag = ActorArt.Joint("Messenger bag",body,Vector3.zero).gameObject;
            var slash = new[]
            {
                new Vector3(-.143f,.448f,.121f),new Vector3(-.079f,.353f,.147f),
                new Vector3(.016f,.235f,.149f),new Vector3(.091f,.128f,.142f),
                new Vector3(.166f,.035f,.116f),
            };
            for(int i=0;i<slash.Length-1;i++)
                ActorArt.Bar("Ремень сумки",bag.transform,slash[i],slash[i+1],.027f,.007f,webbing);
            ActorArt.Bar("Ремень на спине",bag.transform,new Vector3(-.134f,.455f,-.085f),
                new Vector3(.169f,.024f,-.104f),.027f,.007f,webbing);
            ActorArt.RoundedPanel("Сумка через плечо",bag.transform,new Vector3(.149f,.017f,-.066f),
                .171f,.217f,.117f,5.5f,leatherEdge);
            ActorArt.RoundedPanel("Клапан сумки",bag.transform,new Vector3(.149f,.066f,-.126f),
                .160f,.095f,.019f,5f,jacket);
            Form(PrimitiveType.Cube,"Пряжка ремня",bag.transform,-.045f,.309f,.155f,.029f,.024f,.008f,trim);
            ActorArt.Bar("Молния сумки",bag.transform,new Vector3(.082f,.055f,-.133f),
                new Vector3(.216f,.055f,-.133f),.0032f,.0027f,trim);
            for(int pull=0;pull<2;pull++)
                ActorArt.RoundedPanel("Металлический бегунок сумки",bag.transform,
                    new Vector3(.189f+pull*.011f,.045f,-.137f),.007f,.015f,.003f,4f,silver);
            ActorArt.Bar("Шов на клапане",bag.transform,new Vector3(.089f,.071f,-.137f),
                new Vector3(.207f,.071f,-.137f),.0022f,.0022f,leatherEdge);

            vest = ActorArt.Joint("Warehouse uniform",body,Vector3.zero).gameObject;
            Material uniform = ActorArt.Paint(WorkBlue);
            Material stripe = ActorArt.Paint(WorkLime);
            ActorArt.Loft("Work vest",vest.transform,
                new[]{new Vector3(0,.022f,0),new Vector3(0,.18f,0),new Vector3(0,.41f,-.005f),
                    new Vector3(0,.47f,-.010f)},
                new[]{new Vector2(.208f,.147f),new Vector2(.208f,.143f),new Vector2(.228f,.147f),
                    new Vector2(.185f,.122f)},20,uniform);
            for (var i = 0; i < 2; i++)
                Form(PrimitiveType.Cube,"Reflective strip",vest.transform,i==0?-.100f:.100f,
                    .241f,.149f,.031f,.350f,.011f,stripe);
            vest.SetActive(false);

            head = ActorArt.Joint("Head",body,new Vector3(0f,.556f,.022f));
            bool anatomicalHead = AvatarHead.Build(head,skin);
            if (!anatomicalHead)
                BuildFallbackHead(body,head,skin);
            AvatarHair.Build(head,hair,copper,blonde);
            AvatarEyewear.Build(head,frame,lens,silver,anatomicalHead);

            for (var i=0;i<2;i++)
            {
                float side = i == 0 ? -1f : 1f;
                shoulders[i] = ActorArt.Joint("Shoulder "+i,body,new Vector3(side*.190f,.409f,0));
                elbows[i] = ActorArt.Joint("Elbow "+i,shoulders[i],new Vector3(0,-.257f,0));
                AvatarCloth.Sleeve(shoulders[i],elbows[i],side,jacket);
                Form(PrimitiveType.Sphere,"Кисть",elbows[i],0,-.258f,.007f,.062f,.068f,.047f,skin);
                for (int f=0;f<4;f++)
                    Form(PrimitiveType.Sphere,"Палец",elbows[i],-.019f+f*.0126f,
                        -.286f-(f==1||f==2?.005f:0f),.012f,.014f,.043f,.022f,skin);
                Form(PrimitiveType.Sphere,"Большой палец",elbows[i],-side*.025f,-.262f,.022f,
                    .018f,.048f,.025f,skin);

                hips[i] = ActorArt.Joint("Hip "+i,body,new Vector3(side*.083f,-.012f,0f));
                knees[i] = ActorArt.Joint("Knee "+i,hips[i],new Vector3(0,-.406f,0));
                AvatarCloth.Jeans(hips[i],knees[i],side,pants);
                ankles[i] = ActorArt.Joint("Ankle "+i,knees[i],new Vector3(0,-.410f,0));
                AvatarFootwear.Build(ankles[i],side,rubber,shoeGrey,shoeWhite);
            }
            box = ActorArt.Form(PrimitiveType.Cube,"Carried parcel",body,new Vector3(0,.078f,.350f),
                new Vector3(.365f,.285f,.335f),ActorArt.Paint(new Color(.55f,.368f,.176f)));
            ActorArt.Form(PrimitiveType.Cube,"Packing tape",box.transform,new Vector3(0,.505f,0),
                new Vector3(.17f,.015f,1.005f),ActorArt.Paint(new Color(.71f,.519f,.24f)));
            phone = ActorArt.Form(PrimitiveType.Cube,"Phone",elbows[1],new Vector3(0,-.24f,.04f),
                new Vector3(.06f,.102f,.015f),ActorArt.Paint(new Color(.18f,.21f,.28f),.75f));
            box.SetActive(false);
            phone.SetActive(false);
        }

        private static void BuildFallbackHead(Transform body,Transform head,Material skin)
        {
            ActorArt.Loft("Шея · переход к челюсти",body,
                new[]{new Vector3(0,.474f,-.011f),new Vector3(0,.495f,.004f),
                    new Vector3(0,.525f,.017f),new Vector3(0,.553f,.019f),new Vector3(0,.579f,.021f)},
                new[]{new Vector2(.071f,.055f),new Vector2(.057f,.049f),
                    new Vector2(.044f,.047f),new Vector2(.044f,.048f),new Vector2(.039f,.043f)},28,skin);
            var sections = new[] {
                new Vector3(.002f,.026f,.035f),new Vector3(.019f,.046f,.051f),
                new Vector3(.043f,.059f,.063f),new Vector3(.066f,.065f,.071f),
                new Vector3(.087f,.073f,.076f),new Vector3(.108f,.079f,.080f),
                new Vector3(.127f,.083f,.083f),new Vector3(.151f,.081f,.083f),
                new Vector3(.181f,.079f,.084f),new Vector3(.205f,.071f,.072f),
                new Vector3(.228f,.052f,.056f),new Vector3(.243f,.019f,.027f),
            };
            float Gaussian(float value,float radius)
            {
                float q = value/radius;
                return (float)System.Math.Exp(-q*q);
            }
            Vector3 CrossSection(float y)
            {
                for (int j=1;j<sections.Length;j++)
                    if (y <= sections[j].x)
                    {
                        Vector3 a=sections[j-1],b=sections[j];
                        float t=(y-a.x)/(b.x-a.x);
                        return a+(b-a)*t;
                    }
                return sections[sections.Length-1];
            }
            float FacialDetail(float x,float y)
            {
                float tip = .029f*Gaussian(y-.1035f,.0145f)*Gaussian(x,.013f);
                float bridge = .016f*Gaussian(y-.132f,.031f)*Gaussian(x,.0098f);
                float wings = .010f*Gaussian(y-.098f,.007f)*Gaussian(Mathf.Abs(x)-.0117f,.0085f);
                float cheek = .007f*Gaussian(y-.117f,.025f)*Gaussian(Mathf.Abs(x)-.049f,.021f);
                float crease = .0048f*Gaussian(y-.083f,.011f)*Gaussian(Mathf.Abs(x)-.028f,.009f);
                float muzzle = .006f*Gaussian(y-.067f,.015f)*Gaussian(x,.027f);
                float philtrum = .0022f*Gaussian(y-.080f,.009f)*Gaussian(x,.003f);
                return tip+bridge+wings+cheek-crease+muzzle-philtrum;
            }
            float BaseDepth(float y) => .014f-.030f*(y/.243f);
            float FaceAt(float x,float y)
            {
                Vector3 ring = CrossSection(y);
                float rel = x/ring.y;
                float frontal = Mathf.Sqrt(Mathf.Max(0f,1f-rel*rel));
                return BaseDepth(y)+ring.z*frontal+FacialDetail(x,y)*frontal*frontal;
            }

            const int faceBands = 64, faceSides = 72;
            var faceVertices = new System.Collections.Generic.List<Vector3>(faceBands*faceSides+2);
            var faceUv = new System.Collections.Generic.List<Vector2>(faceBands*faceSides+2);
            var faceTriangles = new System.Collections.Generic.List<int>((faceBands+1)*faceSides*6);
            for (int row=0;row<faceBands;row++)
            {
                float y=.002f+.241f*row/(faceBands-1);
                Vector3 ring=CrossSection(y);
                for (int i=0;i<faceSides;i++)
                {
                    float phi = 2f*Mathf.PI*i/faceSides;
                    float front = Mathf.Sin(phi);
                    float x = ring.y*Mathf.Cos(phi);
                    float z = BaseDepth(y)+ring.z*front;
                    if (front > 0f)
                        z += FacialDetail(x,y)*front*front;
                    faceVertices.Add(new Vector3(x,y,z));
                    faceUv.Add(new Vector2(i/(float)faceSides,row/(float)(faceBands-1)));
                    if (row == 0)
                        continue;
                    int p=(row-1)*faceSides+i;
                    int q=(row-1)*faceSides+(i+1)%faceSides;
                    faceTriangles.AddRange(new[]{p,p+faceSides,q,q,p+faceSides,q+faceSides});
                }
            }
            int chin = faceVertices.Count, crown = chin+1;
            faceVertices.Add(new Vector3(0f,.001f,.013f));
            faceVertices.Add(new Vector3(0f,.244f,-.016f));
            for (int i=0;i<faceSides;i++)
            {
                int next=(i+1)%faceSides, last=(faceBands-1)*faceSides;
                faceTriangles.AddRange(new[]{chin,i,next,crown,last+next,last+i});
            }
            faceUv.Add(new Vector2(.25f,0f));
            faceUv.Add(new Vector2(.25f,1f));
            var faceMaterial=new Material(skin) {name="LC Face"};
            var faceDetail=Resources.Load<Texture2D>("Art/LCFaceDetail");
            if(faceDetail)
                faceMaterial.mainTexture=faceDetail;
            ActorArt.Custom("Голова · скулы, нос и челюсть",head,faceVertices,faceTriangles,faceMaterial,faceUv);
            Material lip = ActorArt.Paint(new Color(.450f,.277f,.218f),.31f);
            Material lowerLip = ActorArt.Paint(new Color(.471f,.286f,.223f),.37f);
            Material lipSeam = ActorArt.Paint(new Color(.247f,.120f,.098f),.26f);
            void MouthBand(string name, Material paint, float lower, float upper)
            {
                var vertices = new System.Collections.Generic.List<Vector3>();
                var faces = new System.Collections.Generic.List<int>();
                const int segments=22;
                for (int i=0;i<=segments;i++)
                {
                    float unit=2f*i/segments-1f;
                    float x=unit*.0195f;
                    float weight=1f-unit*unit;
                    float seamY=.066f+.0012f*Mathf.Cos(unit*2f*Mathf.PI)
                        +.0018f*unit*unit;
                    for(int line=0;line<2;line++)
                    {
                        float y=seamY+weight*(line==0 ? lower : upper);
                        vertices.Add(new Vector3(x,y,FaceAt(x,y)+.00135f));
                    }
                    if(i>0)
                    {
                        int p=(i-1)*2;
                        faces.AddRange(new[]{p,p+2,p+1,p+1,p+2,p+3});
                    }
                }
                ActorArt.Custom(name,head,vertices,faces,paint);
            }
            MouthBand("Верхняя губа",lip,.0004f,.0044f);
            MouthBand("Нижняя губа",lowerLip,-.0048f,-.0006f);
            MouthBand("Линия губ",lipSeam,-.00065f,.00055f);
            Material earDepth = ActorArt.Paint(new Color(.449f,.255f,.189f),.31f);
            Material noseShadow = ActorArt.Paint(new Color(.306f,.152f,.112f),.28f);
            for (int i=-1;i<=1;i+=2)
            {
                ActorArt.Form(PrimitiveType.Sphere,"Ушная раковина",head,
                    new Vector3(i*.082f,.103f,-.003f),new Vector3(.027f,.046f,.032f),skin);
                ActorArt.Form(PrimitiveType.Sphere,"Углубление ушной раковины",head,
                    new Vector3(i*.088f,.105f,.009f),new Vector3(.009f,.021f,.009f),earDepth);
                ActorArt.Form(PrimitiveType.Sphere,"Тень под крылом носа",head,
                    new Vector3(i*.011f,.094f,FaceAt(i*.011f,.094f)),
                    new Vector3(.007f,.0023f,.0028f),noseShadow);
            }
        }

        public void SetFirstPerson(bool active)
        {
            firstPerson = active;
            currentState = null;
            if (Visual)
                Visual.gameObject.SetActive(!active);
        }

        public void ApplyWorkUniform(bool active)
        {
            if (!vest || !bag)
                return;
            vest.SetActive(active);
            bag.SetActive(!active);
        }

        public void Work() => workUntil = Time.time+.72f;

        public void Punch() => punchUntil = Time.time + .44f;

        public void Enter() => enterUntil = Time.time + .57f;

        public void Exit() => exitUntil = Time.time + .53f;

        public void OpenDoor() => doorUntil = Time.time + .54f;

        public void Carry(bool enabled)
        {
            if (enabled && !carrying)
                pickUpUntil = Time.time+.74f;
            carrying = enabled;
            if (box)
                box.SetActive(enabled);
        }

        public void Phone(bool enabled)
        {
            onPhone = enabled;
            if (phone)
                phone.SetActive(enabled);
        }

        internal void Animate(float speed, bool grounded, bool crouched, bool driving, float verticalVelocity)
        {
            if (!body || firstPerson)
                return;
            float dt = Time.deltaTime;
            if (!lastGrounded && grounded)
            {
                landAmount = .15f;
                landUntil = Time.time+.25f;
            }
            lastGrounded = grounded;
            if (Animator.runtimeAnimatorController)
            {
                AnimateState(speed,grounded,crouched,driving,verticalVelocity);
                return;
            }
            landAmount = Mathf.MoveTowards(landAmount,0f,dt*.75f);
            animationSpeed = Mathf.MoveTowards(animationSpeed,speed,dt*12f);
            stepAmount = Mathf.MoveTowards(stepAmount,grounded ? Mathf.Clamp01(speed/2f) : 0f,dt*8f);
            phase += dt*Mathf.Lerp(5.7f,13.5f,Mathf.Clamp01(animationSpeed/7.2f));
            float stride = Mathf.Lerp(21f,54f,Mathf.Clamp01(animationSpeed/7.2f))*stepAmount;
            float idle = Mathf.Sin(Time.time*1.31f)*.0022f;
            float crouchHeight = crouched ? .57f : .95f;
            float height = driving ? .41f : crouchHeight + Mathf.Abs(Mathf.Sin(phase))*stepAmount*.042f + idle - landAmount;
            float bend = driving ? 8f : crouched ? 20f : 4f+animationSpeed*1.3f;
            float work = Mathf.Clamp01((workUntil-Time.time)/.72f);
            work = Mathf.Sin(work*Mathf.PI);
            float attack = Mathf.Clamp01((punchUntil-Time.time)/.44f);
            attack = Mathf.Sin(attack*Mathf.PI);
            float boarding = Mathf.Clamp01((enterUntil-Time.time)/.42f);
            bool relaxed = grounded && animationSpeed < .30f && !crouched && !driving &&
                !carrying && !onPhone && work < .02f && attack < .02f && boarding < .02f;
            body.localPosition = Vector3.Lerp(body.localPosition,new Vector3(0,height,0),dt*16f);
            body.localRotation = Quaternion.Euler(bend+work*22f,attack*21f,boarding*12f);
            head.localRotation = Quaternion.Euler(-bend*.58f-work*13f,boarding*21f,0);
            for (var i=0;i<2;i++)
            {
                float side = i == 0 ? -1f : 1f;
                float cycle = Mathf.Sin(phase)*side;
                float hip = cycle*stride;
                float knee = Mathf.Max(0,cycle)*stride*.91f;
                float shoulder = -cycle*stride*.71f;
                float elbow = -12f - Mathf.Max(0,-cycle)*stride*.45f;
                if (crouched) { hip -= 39f; knee += 67f; shoulder -= 13f; }
                if (!grounded)
                {
                    hip = verticalVelocity > 0 ? -29f*side : -16f;
                    knee = 28f;
                    shoulder = 23f*side;
                    elbow = -38f;
                }
                if (carrying || work > .02f) { shoulder = -50f-work*25f; elbow = -45f-work*16f; }
                if (onPhone && i == 1) { shoulder = -32f; elbow = -120f; }
                if (i == 1) { shoulder -= 86f*attack; elbow *= 1f-attack; }
                if (driving) { hip = -80f; knee = 70f; shoulder = -52f; elbow = -35f; }
                float softness = dt*17f;
                Pose(hips[i],Quaternion.Euler(hip,0f,side*2f),softness);
                Pose(knees[i],Quaternion.Euler(knee,0f,0f),softness);
                Pose(ankles[i],Quaternion.Euler(Mathf.Clamp(-hip-knee,-28f,28f),side*2f,0f),softness);
                Pose(shoulders[i],relaxed ? Quaternion.Euler(-2.8f,-side*5f,side*5f) :
                    Quaternion.Euler(shoulder,-side*12f,side*(carrying ? 14f : 7f)),softness);
                Pose(elbows[i],relaxed ? Quaternion.Euler(-23f,side*4f,-side*21f) :
                    Quaternion.Euler(elbow,0f,0f),softness);
            }
        }

        private void AnimateState(float speed,bool grounded,bool crouched,bool driving,float verticalVelocity)
        {
            float now = Time.time;
            string state;
            if (enterUntil > now) state = "Enter Vehicle";
            else if (exitUntil > now) state = "Exit Vehicle";
            else if (driving) state = "Drive";
            else if (!grounded) state = verticalVelocity > 0f ? "Jump" : "Fall";
            else if (landUntil > now) state = "Land";
            else if (punchUntil > now) state = "Punch";
            else if (pickUpUntil > now) state = "Pick Up";
            else if (workUntil > now) state = "Work";
            else if (doorUntil > now) state = "Open Door";
            else if (carrying) state = "Carry";
            else if (onPhone) state = "Phone";
            else if (crouched) state = "Crouch";
            else if (speed > 5.1f) state = "Sprint";
            else if (speed > 2.8f) state = "Run";
            else if (speed > .30f) state = "Walk";
            else state = Animator.HasState(0,RelaxedIdle) ? "Relaxed Idle" : "Idle";
            if (currentState == state)
                return;
            currentState = state;
            Animator.CrossFadeInFixedTime("Base Layer."+state,.12f);
        }

        private static void Pose(Transform joint, Quaternion target, float fraction)
            => joint.localRotation = Quaternion.Slerp(joint.localRotation,target,fraction);
    }
}
