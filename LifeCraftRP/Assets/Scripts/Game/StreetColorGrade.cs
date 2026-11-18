using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LifeCraftRP
{
    // A neutral tone curve compresses bright daylight without changing the color of city materials.
    public sealed class StreetColorGrade : IDisposable
    {
        public const string ResourcePath = "Art/StreetColorGrade";

        private readonly Camera camera;
        private UniversalAdditionalCameraData cameraData;
        private GameObject volumeObject;
        private VolumeProfile ownedProfile;

        internal StreetColorGrade(Camera camera)
        {
            this.camera = camera;
        }

        internal void SetEnabled(bool enabled)
        {
            if (enabled && volumeObject == null)
                CreateVolume();

            if (cameraData != null)
                cameraData.renderPostProcessing = enabled;

            if (volumeObject != null)
                volumeObject.SetActive(enabled);
        }

        private void CreateVolume()
        {
            cameraData = camera.GetUniversalAdditionalCameraData();
            VolumeProfile profile = Resources.Load<VolumeProfile>(ResourcePath);
            if (profile == null)
            {
                ownedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                Configure(ownedProfile);
                profile = ownedProfile;
            }

            volumeObject = new GameObject("LC · нейтральная цветопередача", typeof(Volume));
            var volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1;
            volume.sharedProfile = profile;
            cameraData.volumeLayerMask = cameraData.volumeLayerMask.value | 1;
        }

        public static void Configure(VolumeProfile profile)
        {
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);
            var grading = profile.Add<ColorAdjustments>();
            grading.postExposure.Override(.06f);
            grading.contrast.Override(4f);
            grading.saturation.Override(-3f);
        }

        public void Dispose()
        {
            if (cameraData != null)
                cameraData.renderPostProcessing = false;
            if (volumeObject != null)
                Object.Destroy(volumeObject);
            if (ownedProfile != null)
            {
                foreach (VolumeComponent component in ownedProfile.components)
                    Object.Destroy(component);
                Object.Destroy(ownedProfile);
            }
        }
    }
}
