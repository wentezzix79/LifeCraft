using UnityEditor;
using UnityEngine;

namespace LifeCraftRP.Editor
{
    internal sealed class AvatarTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.EndsWith("/LCLeatherDetailNormal.png") &&
                !assetPath.EndsWith("/LCDenimDetailNormal.png"))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.NormalMap;
            importer.wrapMode=TextureWrapMode.Repeat;
            importer.maxTextureSize=512;
        }
    }
}
