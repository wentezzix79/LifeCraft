using UnityEditor;
using UnityEngine;

namespace LifeCraftRP.Editor
{
    internal sealed class OmskMapTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            bool districtLayer = assetPath == "Assets/Resources/Omsk/ui-districts-map.png";
            bool city = assetPath == "Assets/Resources/Omsk/city-map.png" ||
                        assetPath == "Assets/Resources/Omsk/ui-city-map.png" || districtLayer;
            if (!city && assetPath != "Assets/Resources/Omsk/east-map.png" &&
                assetPath != "Assets/Resources/Omsk/mega-map.png" &&
                assetPath != "Assets/Resources/Omsk/ui-starter-map.png" &&
                assetPath != "Assets/Resources/Omsk/ui-east-map.png" &&
                assetPath != "Assets/Resources/Omsk/ui-mega-map.png")
                return;

            var importer = (TextureImporter)assetImporter;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = city ? 4096 : 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (districtLayer)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
            }
        }
    }
}
