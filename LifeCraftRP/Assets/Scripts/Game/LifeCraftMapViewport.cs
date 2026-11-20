using System;
using LifeCraft.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LifeCraftRP
{
    [RequireComponent(typeof(RectTransform), typeof(Image), typeof(RectMask2D))]
    internal sealed class LifeCraftMapViewport : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        private const float InitialWidth = 15000f;
        private const float MinimumWidth = 300f;
        private RectTransform bounds;
        private RawImage raster;
        private RawImage districts;
        private LifeCraftRouteGraphic route;
        private LifeCraftRouteGraphic busRoute;
        private CanvasGroup markerInput;
        private Rect view;
        private float width = InitialWidth;
        private bool dragging;
        private bool pinching;
        private float clickBlockedUntil;

        private struct AtlasRegion
        {
            public readonly string Name;
            public readonly string Fallback;
            public readonly Rect Bounds;
            private Texture2D texture;
            private bool loaded;

            public AtlasRegion(string name,string fallback,Rect bounds)
            {
                Name = name;
                Fallback = fallback;
                Bounds = bounds;
                texture = null;
                loaded = false;
            }

            public Texture2D Load()
            {
                if (!loaded)
                {
                    texture = Resources.Load<Texture2D>("Omsk/"+Name) ??
                        Resources.Load<Texture2D>("Omsk/"+Fallback);
                    loaded = true;
                }
                return texture;
            }
        }

        private static readonly AtlasRegion[] Atlas =
        {
            new AtlasRegion("ui-starter-map","starter-map",new Rect(-1250,-1250,2500,2500)),
            new AtlasRegion("ui-east-map","east-map",new Rect(400,-1400,2600,2600)),
            new AtlasRegion("ui-mega-map","mega-map",new Rect(-8150,-1400,2600,2600)),
            new AtlasRegion("ui-city-map","city-map",OmskWorld.WorldBounds)
        };

        public RectTransform MarkerLayer { get; private set; }
        public Rect View => view;
        public event Action Changed;
        public event Action<Vector3> MapClicked;

        public static LifeCraftMapViewport Create(RectTransform parent)
        {
            var obj = new GameObject("Поле карты",typeof(RectTransform),typeof(Image),
                typeof(RectMask2D),typeof(LifeCraftMapViewport));
            var viewport = obj.GetComponent<LifeCraftMapViewport>();
            viewport.bounds = obj.GetComponent<RectTransform>();
            viewport.bounds.SetParent(parent,false);
            Stretch(viewport.bounds);
            var background = obj.GetComponent<Image>();
            background.color = new Color(.143f,.163f,.194f);
            viewport.raster = Layer<RawImage>(viewport.bounds,"Картография");
            viewport.raster.raycastTarget = false;
            viewport.districts = Layer<RawImage>(viewport.bounds,"Границы округов");
            viewport.districts.raycastTarget = false;
            viewport.districts.texture = Resources.Load<Texture2D>("Omsk/ui-districts-map");
            viewport.route = Layer<LifeCraftRouteGraphic>(viewport.bounds,"Линия маршрута");
            viewport.route.raycastTarget = false;
            viewport.busRoute = Layer<LifeCraftRouteGraphic>(viewport.bounds,"Линия автобуса");
            viewport.busRoute.color = new Color(.34f,.94f,.79f);
            viewport.busRoute.raycastTarget = false;
            viewport.busRoute.gameObject.SetActive(false);
            viewport.MarkerLayer = Layer<RectTransform>(viewport.bounds,"Метки");
            viewport.markerInput = viewport.MarkerLayer.gameObject.AddComponent<CanvasGroup>();
            viewport.SetDistricts(true);
            viewport.Center(Vector3.zero);
            return viewport;
        }

        public void Center(Vector3 position,float requestedWidth = 0f)
        {
            SetView(new Vector2(position.x,position.z),requestedWidth > 0f ? requestedWidth : width);
        }

        public void Fit() => SetView(OmskWorld.WorldBounds.center,OmskWorld.WorldBounds.width);

        public void Zoom(float factor) => ZoomAt(factor,new Vector2(.5f,.5f));

        public void SetDistricts(bool visible)
        {
            districts.gameObject.SetActive(visible && districts.texture != null);
        }

        public void SetRoute(Vector2[] points) => route.SetRoute(points,view);

        public void SetBusRoute(bool show,Vector2[] points)
        {
            route.gameObject.SetActive(!show);
            busRoute.gameObject.SetActive(show);
            busRoute.SetRoute(points,view);
        }

        public bool Project(Vector3 world,out Vector2 normalized)
        {
            normalized = new Vector2((world.x-view.xMin)/view.width,(world.z-view.yMin)/view.height);
            return normalized.x >= 0f && normalized.x <= 1f && normalized.y >= 0f && normalized.y <= 1f;
        }

        public static void DrawRaster(RawImage image,Rect visible)
        {
            for (int i=0;i<Atlas.Length;i++)
            {
                Rect atlas = Atlas[i].Bounds;
                bool wholeCity = i == Atlas.Length-1;
                if (!wholeCity && (visible.xMin < atlas.xMin || visible.xMax > atlas.xMax ||
                    visible.yMin < atlas.yMin || visible.yMax > atlas.yMax))
                    continue;
                Texture2D texture = Atlas[i].Load();
                if (texture == null && wholeCity)
                    texture = OmskWorld.LoadMapTexture();
                if (texture == null)
                    continue;
                if (image.texture != texture)
                    image.texture = texture;
                image.uvRect = Uv(visible,atlas);
                return;
            }
        }

        private void SetView(Vector2 center,float requestedWidth)
        {
            Vector2 size = Extent(requestedWidth);
            Rect city = OmskWorld.WorldBounds;
            var desired = new Rect(Mathf.Clamp(center.x-size.x*.5f,city.xMin,city.xMax-size.x),
                Mathf.Clamp(center.y-size.y*.5f,city.yMin,city.yMax-size.y),size.x,size.y);
            width = size.x;
            if (desired == view)
                return;
            view = desired;
            DrawRaster(raster,view);
            districts.uvRect = Uv(view,city);
            route.SetView(view);
            busRoute?.SetView(view);
            Changed?.Invoke();
        }

        private Vector2 Extent(float requestedWidth)
        {
            Rect pixels = bounds.rect;
            float aspect = pixels.width > .1f && pixels.height > .1f ? pixels.width/pixels.height : 1.65f;
            Rect city = OmskWorld.WorldBounds;
            float maximum = Mathf.Min(city.width,city.height*aspect);
            float x = Mathf.Clamp(requestedWidth,Mathf.Min(MinimumWidth,maximum),maximum);
            return new Vector2(x,x/aspect);
        }

        private void ZoomAt(float factor,Vector2 anchor)
        {
            if (factor <= 0 || Mathf.Approximately(factor,1f))
                return;
            Vector2 size = Extent(width/factor);
            Vector2 shift = Vector2.Scale(anchor-Vector2.one*.5f,view.size-size);
            SetView(view.center+shift,size.x);
        }

        public void OnScroll(PointerEventData e)
        {
            if (!ToNormalized(e.position,e.enterEventCamera,out Vector2 anchor))
                return;
            ZoomAt(Mathf.Pow(1.21f,Mathf.Clamp(e.scrollDelta.y,-5f,5f)),anchor);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right)
            {
                dragging = true;
                markerInput.blocksRaycasts = false;
            }
        }

        public void OnDrag(PointerEventData e)
        {
            if (!dragging || pinching || Input.touchCount > 1 ||
                !ToNormalized(e.position,e.pressEventCamera,out Vector2 current) ||
                !ToNormalized(e.position-e.delta,e.pressEventCamera,out Vector2 previous))
                return;
            SetView(view.center-Vector2.Scale(current-previous,view.size),width);
        }

        public void OnEndDrag(PointerEventData e)
        {
            dragging = false;
            clickBlockedUntil = Time.unscaledTime+.075f;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || dragging || pinching ||
                Time.unscaledTime < clickBlockedUntil ||
                !ToNormalized(e.position,e.pressEventCamera,out Vector2 p))
                return;
            Vector2 world = view.min+Vector2.Scale(p,view.size);
            MapClicked?.Invoke(new Vector3(world.x,0f,world.y));
        }

        private void Update()
        {
            if (Input.touchCount != 2)
            {
                if (pinching)
                    clickBlockedUntil = Time.unscaledTime+.075f;
                pinching = false;
                if (!dragging && Time.unscaledTime >= clickBlockedUntil)
                    markerInput.blocksRaycasts = true;
                return;
            }
            Touch one = Input.GetTouch(0), two = Input.GetTouch(1);
            if (one.phase == TouchPhase.Ended || one.phase == TouchPhase.Canceled ||
                two.phase == TouchPhase.Ended || two.phase == TouchPhase.Canceled)
                return;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            if (!pinching &&
                (!RectTransformUtility.RectangleContainsScreenPoint(bounds,one.position,eventCamera) ||
                 !RectTransformUtility.RectangleContainsScreenPoint(bounds,two.position,eventCamera)))
                return;
            pinching = true;
            markerInput.blocksRaycasts = false;
            Vector2 previousOne = one.position-one.deltaPosition, previousTwo = two.position-two.deltaPosition;
            Vector2 oldMid = (previousOne+previousTwo)*.5f, newMid = (one.position+two.position)*.5f;
            float oldSpan = Vector2.Distance(previousOne,previousTwo);
            float newSpan = Vector2.Distance(one.position,two.position);
            if (oldSpan < 8f || newSpan < 8f || !ToNormalized(oldMid,eventCamera,out Vector2 before) ||
                !ToNormalized(newMid,eventCamera,out Vector2 after))
                return;
            Vector2 size = Extent(width/(newSpan/oldSpan));
            Vector2 anchor = view.min+Vector2.Scale(before,view.size);
            SetView(anchor-Vector2.Scale(after-Vector2.one*.5f,size),size.x);
        }

        private void OnDisable()
        {
            dragging = false;
            pinching = false;
            if (markerInput != null)
                markerInput.blocksRaycasts = true;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (route != null)
                SetView(view.center,width);
        }

        private bool ToNormalized(Vector2 point,Camera camera,out Vector2 normalized)
        {
            normalized = default;
            Rect rect = bounds.rect;
            if (rect.width <= 0 || rect.height <= 0 ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds,point,camera,out Vector2 local))
                return false;
            normalized = new Vector2((local.x-rect.xMin)/rect.width,(local.y-rect.yMin)/rect.height);
            return true;
        }

        private static Rect Uv(Rect visible,Rect atlas) => new Rect(
            (visible.x-atlas.x)/atlas.width,(visible.y-atlas.y)/atlas.height,
            visible.width/atlas.width,visible.height/atlas.height);

        private static T Layer<T>(RectTransform parent,string label) where T : Component
        {
            var obj = new GameObject(label,typeof(RectTransform));
            var rect = obj.GetComponent<RectTransform>();
            rect.SetParent(parent,false);
            Stretch(rect);
            return typeof(T) == typeof(RectTransform) ? (T)(Component)rect : obj.AddComponent<T>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    internal sealed class LifeCraftRouteGraphic : MaskableGraphic
    {
        public Vector2[] Points { get; private set; }
        private Rect view;

        public void SetRoute(Vector2[] points,Rect visible)
        {
            if (Points == points && view == visible)
                return;
            Points = points;
            view = visible;
            SetVerticesDirty();
        }

        internal void SetView(Rect visible) => SetRoute(Points,visible);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect ui = rectTransform.rect;
            if (Points == null || Points.Length < 2 || view.width <= 0f || view.height <= 0f ||
                ui.width <= 0f || ui.height <= 0f)
                return;
            var inset = new Color32(57,27,125,255);
            Stroke(mesh,ui,4.3f,inset);
            Color32 ink = color == Color.white ? new Color32(165,109,255,255) : (Color32)color;
            Stroke(mesh,ui,2.55f,ink);
        }

        private void Stroke(VertexHelper mesh,Rect ui,float halfWidth,Color32 tint)
        {
            Vector2 before = ToLocal(Points[0],ui);
            for (int i=1;i<Points.Length;i++)
            {
                Vector2 next = ToLocal(Points[i],ui);
                AddSegment(mesh,ui,before,next,halfWidth,tint);
                before = next;
            }
        }

        private Vector2 ToLocal(Vector2 p,Rect ui) => ui.min+new Vector2(
            (p.x-view.xMin)/view.width*ui.width,(p.y-view.yMin)/view.height*ui.height);

        private static void AddSegment(VertexHelper mesh,Rect rect,Vector2 a,Vector2 b,float width,Color32 tint)
        {
            Vector2 direction = b-a;
            if (direction.sqrMagnitude < .025f || mesh.currentVertCount > 65524)
                return;
            float first=0f,last=1f;
            if (!Clip(-direction.x,a.x-rect.xMin+width,ref first,ref last) ||
                !Clip(direction.x,rect.xMax-a.x+width,ref first,ref last) ||
                !Clip(-direction.y,a.y-rect.yMin+width,ref first,ref last) ||
                !Clip(direction.y,rect.yMax-a.y+width,ref first,ref last))
                return;
            Vector2 start = a+direction*first,end = a+direction*last;
            Vector2 normal = new Vector2(-direction.y,direction.x).normalized*width;
            int at = mesh.currentVertCount;
            mesh.AddVert(start-normal,tint,Vector2.zero);
            mesh.AddVert(start+normal,tint,Vector2.zero);
            mesh.AddVert(end+normal,tint,Vector2.zero);
            mesh.AddVert(end-normal,tint,Vector2.zero);
            mesh.AddTriangle(at,at+1,at+2);
            mesh.AddTriangle(at,at+2,at+3);
        }

        private static bool Clip(float slope,float distance,ref float first,ref float last)
        {
            if (Mathf.Abs(slope) < .0001f)
                return distance >= 0f;
            float t = distance/slope;
            if (slope < 0f)
                first = Mathf.Max(first,t);
            else
                last = Mathf.Min(last,t);
            return first <= last;
        }
    }
}
