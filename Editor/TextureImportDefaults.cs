using UnityEngine;
using UnityEditor;

namespace DoggoBrutus.WorldDev.Editor
{
    [CreateAssetMenu(fileName = "TextureImportDefaults", menuName = "DB's worlddev-tools/Texture Import Defaults")]
    public class TextureImportDefaults : ScriptableObject
    {
        [System.Serializable]
        public class PlatformPreset
        {
            public bool overrideEnabled = false;
            public int maxTextureSize = 2048;
            public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            public bool useExplicitFormat = false;
            public TextureImporterFormat format = TextureImporterFormat.Automatic;
            public TextureImporterCompression compression = TextureImporterCompression.Compressed;
            // Crunch only shrinks download size, not VRAM, and can break across Unity versions.
            public bool crunchedCompression = false;
            [Range(0, 100)] public int compressionQuality = 50;
        }

        [Tooltip("Applied to the importer's Default (fallback) platform settings.")]
        public PlatformPreset defaultPreset = new PlatformPreset
        {
            maxTextureSize = 2048,
            compression = TextureImporterCompression.Compressed
        };

        [Tooltip("Per-platform override for Standalone (PC).")]
        public PlatformPreset standalonePreset = new PlatformPreset
        {
            overrideEnabled = true,
            maxTextureSize = 2048,
            compression = TextureImporterCompression.CompressedHQ
        };

        [Tooltip("Per-platform override for Android (Quest). VRChat recommends textures <= 1024.")]
        public PlatformPreset androidPreset = new PlatformPreset
        {
            overrideEnabled = true,
            maxTextureSize = 1024,
            compression = TextureImporterCompression.Compressed
        };
    }
}
