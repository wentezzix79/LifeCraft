using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LifeCraft.World
{
    internal sealed class OmskLandmarks
    {
        [Serializable]
        private sealed class Catalog
        {
            public Site[] sites;
        }

        [Serializable]
        internal sealed class Site
        {
            public string id;
            public string name;
            public string address;
            public double lon;
            public double lat;
            public double arrivalLon;
            public double arrivalLat;
            public float[] footprint;
            public float[] bounds;
            public float heading;

            public Vector3 Center => OmskWorld.PositionAt(lon,lat);
            public Vector3 Arrival => OmskWorld.PositionAt(arrivalLon,arrivalLat,.22f);

            internal GameObject Root;
            internal OmskPark Park;
            internal bool Unavailable;
        }

        private readonly Transform owner;
        private readonly Site[] sites;

        internal OmskLandmarks(Transform owner)
        {
            this.owner = owner;
            var asset = Resources.Load<TextAsset>("Omsk/landmarks");
            if (!asset)
            {
                sites = Array.Empty<Site>();
                return;
            }
            try
            {
                sites = JsonUtility.FromJson<Catalog>(asset.text)?.sites ?? Array.Empty<Site>();
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
        }

        internal IReadOnlyList<Site> Sites => sites;

        internal void Refresh(Vector3 focus, bool reducedDetail)
        {
            float visibleRange = reducedDetail ? 590f : 1040f;
            foreach (Site site in sites)
            {
                Vector3 offset = focus-site.Center;
                offset.y = 0f;
                if (site.bounds?.Length == 4)
                {
                    offset.x -= Mathf.Clamp(offset.x,site.bounds[0],site.bounds[2]);
                    offset.z -= Mathf.Clamp(offset.z,site.bounds[1],site.bounds[3]);
                }
                bool near = offset.sqrMagnitude < visibleRange*visibleRange;
                if (near)
                {
                    if (Show(site) && site.Park)
                        site.Park.RefreshVegetation(focus,reducedDetail);
                }
                else if (site.Root && site.Root.activeSelf)
                    site.Root.SetActive(false);
            }
        }

        internal bool Show(Site site)
        {
            if (site.Unavailable)
                return false;
            if (site.Root)
            {
                if (!site.Root.activeSelf)
                    site.Root.SetActive(true);
                return true;
            }

            site.Root = new GameObject(site.name);
            site.Root.transform.SetParent(owner,false);
            site.Root.transform.localPosition = site.Center;
            try
            {
                switch (site.id)
                {
                    case "mega":
                        OmskMalls.BuildMega(site.Root.transform,Footprint(site));
                        break;
                    case "park-mall":
                        OmskMalls.BuildPark(site.Root.transform,Footprint(site));
                        break;
                    case "culture-park":
                        site.Park = OmskPark.Build(site.Root.transform);
                        break;
                    default:
                        throw new ArgumentException("Unknown Omsk landmark ID " + site.id);
                }
                OmskLandmarkStreets.Build(site.Root.transform,site.id);
                return true;
            }
            catch (Exception error)
            {
                Release(site);
                site.Unavailable = true;
                Debug.LogError("Омск · "+site.name+": "+error.Message);
                return false;
            }
        }

        internal void Dispose()
        {
            foreach (Site site in sites)
                Release(site);
        }

        private static void Release(Site site)
        {
            if (!site.Root)
                return;
            // The park owns its own batches. Mall meshes are created by their builders.
            if (site.id == "mega" || site.id == "park-mall")
                foreach (var filter in site.Root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh)
                        Object.Destroy(filter.sharedMesh);
            Object.Destroy(site.Root);
            site.Root = null;
            site.Park = null;
        }

        private static Vector2[] Footprint(Site site)
        {
            if (site.footprint == null || site.footprint.Length < 6 || site.footprint.Length%2 != 0)
                throw new ArgumentException("Landmark footprint is incomplete.");
            var ring = new Vector2[site.footprint.Length/2];
            for (int i=0;i<ring.Length;i++)
                ring[i] = new Vector2(site.footprint[2*i],site.footprint[2*i+1]);
            return ring;
        }
    }
}
