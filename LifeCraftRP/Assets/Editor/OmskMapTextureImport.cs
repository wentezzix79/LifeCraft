using UnityEditor;
using UnityEngine;

namespace LifeCraftRP.Editor
{
    internal sealed class OmskMapTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            bool city = assetPath == "Assets/Resources/Omsk/city-map.png";
            if (!city && assetPath != "Assets/Resources/Omsk/east-map.png" &&
                assetPath != "Assets/Resources/Omsk/mega-map.png")
                return;

            var importer = (TextureImporter)assetImporter;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = city ? 4096 : 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
