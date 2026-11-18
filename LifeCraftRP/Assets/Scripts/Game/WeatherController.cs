using UnityEngine;

namespace LifeCraftRP
{
    public sealed class WeatherController : MonoBehaviour
    {
        private static readonly string[] names = { "Ясно", "Туман", "Дождь", "Снег" };
        private Transform focus;
        private ParticleSystem particles;
        private ParticleSystemRenderer effect;
        private int weather;
        private bool mobile;

        public string Name => names[weather];

        public static WeatherController Create(Transform focus)
        {
            var go = new GameObject("Осадки · локальная зона");
            go.SetActive(false);
            var controller = go.AddComponent<WeatherController>();
            controller.focus = focus;
            controller.mobile = Application.isMobilePlatform;
            controller.particles = go.AddComponent<ParticleSystem>();
            controller.effect = go.GetComponent<ParticleSystemRenderer>();
            go.transform.rotation = Quaternion.Euler(90,0,0);
            var main = controller.particles.main;
            main.playOnAwake = false;
            main.maxParticles = controller.mobile ? 550 : 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = controller.particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26,26,1);
            Shader shader = RuntimeShaders.ParticlesUnlit();
            if (shader != null)
                controller.effect.sharedMaterial = new Material(shader);
            go.SetActive(true);
            return controller;
        }

        public string Next()
        {
            weather = (weather + 1) % names.Length;
            particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            RenderSettings.fogDensity = new [] { .0011f,.0095f,.0031f,.0027f }[weather];
            if (weather < 2)
                return Name;

            bool snow = weather == 3;
            var main = particles.main;
            main.startColor = snow ? Color.white : new Color(.63f,.78f,.90f);
            main.startSize = snow ? new ParticleSystem.MinMaxCurve(.026f,.073f) :
                new ParticleSystem.MinMaxCurve(.011f,.019f);
            main.startLifetime = snow ? 6.5f : 1.65f;
            main.startSpeed = snow ? new ParticleSystem.MinMaxCurve(1.4f,2.3f) :
                new ParticleSystem.MinMaxCurve(10f,15f);
            var emission = particles.emission;
            emission.rateOverTime = snow ? (mobile ? 88f : 195f) : (mobile ? 235f : 560f);
            effect.renderMode = snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            effect.lengthScale = 3.5f;
            effect.velocityScale = .065f;
            particles.Play();
            return Name;
        }

        private void LateUpdate()
        {
            if (focus != null)
                transform.position = focus.position + Vector3.up * 12f;
        }

        private void OnDestroy()
        {
            if (effect != null && effect.sharedMaterial != null)
                Destroy(effect.sharedMaterial);
        }
    }
}
