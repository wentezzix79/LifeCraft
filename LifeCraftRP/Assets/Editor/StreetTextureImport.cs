using System;
using UnityEditor;
using UnityEngine;

namespace LifeCraftRP.Editor
{
    internal sealed class StreetTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;
            if (assetPath == "Assets/Resources/Omsk/starter-map.png")
            {
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (!assetPath.StartsWith("Assets/Resources/Art/Streets/", StringComparison.Ordinal))
                return;
            if (assetPath.EndsWith("Normal.png", StringComparison.Ordinal))
                importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
