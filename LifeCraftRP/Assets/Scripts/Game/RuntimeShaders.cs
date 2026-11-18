using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraftRP
{
    internal static class RuntimeShaders
    {
        private static bool UsesUniversalPipeline =>
            GraphicsSettings.currentRenderPipeline != null &&
            GraphicsSettings.currentRenderPipeline.GetType().FullName ==
            "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";

        internal static Shader Lit()
        {
            return UsesUniversalPipeline
                ? FindSupported("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit",
                    "Universal Render Pipeline/Unlit")
                : FindSupported("Standard", "Diffuse", "Unlit/Color");
        }

        internal static Shader ParticlesUnlit()
        {
            return UsesUniversalPipeline
                ? FindSupported("Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Unlit")
                : FindSupported("Particles/Standard Unlit", "Unlit/Transparent");
        }

        private static Shader FindSupported(params string[] names)
        {
            foreach (string name in names)
            {
                Shader shader = Shader.Find(name);
                if (shader != null && shader.isSupported)
                    return shader;
            }
            throw new InvalidOperationException("LifeCraft: no supported shader found for the active pipeline.");
        }
    }
}
