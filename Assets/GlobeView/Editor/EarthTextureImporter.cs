using UnityEditor;
using UnityEngine;

namespace Tycoon.EditorTools
{
    /// <summary>Import settings for the globe textures in Resources/Earth (big, mipmapped, equirectangular).</summary>
    public class EarthTextureImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 5;   // bump to force reimport after changing the settings below

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Earth/")) return;

            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 8192;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.sRGBTexture = !(assetPath.Contains("watermask") || assetPath.Contains("normal") || assetPath.Contains("waves") || assetPath.Contains("coast"));
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = assetPath.Contains("waves") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.isReadable = false;
        }
    }
}
