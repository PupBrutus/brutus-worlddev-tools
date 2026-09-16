using UnityEngine;
using UnityEngine.Profiling;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace DoggoBrutus.WorldDev.Editor
{
    public class MaterialAnalyzer : EditorWindow
    {
        private enum ViewMode { Shaders, Textures, Impact }
        private ViewMode viewMode = ViewMode.Shaders;
        private static readonly string[] viewModeLabels = { "By Shader", "Textures", "Impact" };

        private int totalMaterialSlots = 0;
        private int uniqueMaterialCount = 0;
        private int uniqueShaderCount = 0;
        private int rendererCount = 0;
        private int missingMaterialSlots = 0;

        // Texture / memory totals
        private int uniqueTextureCount = 0;
        private long uniqueTextureMemory = 0;   // deduplicated (actual VRAM footprint)
        private long summedTextureMemory = 0;    // per-material sum (ignores sharing)
        private int texturesWithoutMipmaps = 0;
        private int uncompressedTextureCount = 0;
        private long uncompressedTextureMemory = 0;
        private int nonPowerOfTwoCount = 0;
        private int largeTextureCount = 0;       // >= 2048 on either axis

        // Impact totals
        private int transparentMaterialCount = 0;
        private float avgSamplersPerMaterial = 0f;
        private float avgKeywordsPerMaterial = 0f;
        private int maxShaderPasses = 0;

        private Vector2 windowScrollPosition;
        private Vector2 scrollPosition;

        private List<ShaderGroup> shaderGroups = new List<ShaderGroup>();
        private Dictionary<Material, MaterialUsage> materialUsage = new Dictionary<Material, MaterialUsage>();
        private Dictionary<Texture, TextureUsage> textureUsage = new Dictionary<Texture, TextureUsage>();
        private List<TextureUsage> sortedTextures = new List<TextureUsage>();
        private HashSet<string> expandedShaders = new HashSet<string>();

        private class MaterialUsage
        {
            public Material material;
            public string materialName;
            public string shaderName;
            public int usageCount = 0;
            public List<GameObject> usedByObjects = new List<GameObject>();

            // Impact metrics
            public long textureMemory = 0;   // sum of unique textures referenced by this material
            public int samplerCount = 0;     // assigned texture properties
            public int passCount = 0;
            public int keywordCount = 0;
            public bool isTransparent = false;
            public int renderQueue = 0;
            public int ComplexityScore => samplerCount + (passCount * 2) + keywordCount + (isTransparent ? 3 : 0);
        }

        private class TextureUsage
        {
            public Texture texture;
            public string textureName;
            public int width;
            public int height;
            public string format;
            public bool hasMipmaps;
            public bool isCompressed;
            public bool isPowerOfTwo;
            public long memoryBytes;
            public int usageCount = 0;   // number of material slots referencing it
            public List<Material> usedByMaterials = new List<Material>();
        }

        private class ShaderGroup
        {
            public string shaderName;
            public List<MaterialUsage> materials = new List<MaterialUsage>();
            public int TotalUsage => materials.Sum(m => m.usageCount);
        }

        [MenuItem("Tools/DB's worlddev-tools/Material Analyzer")]
        public static void ShowWindow()
        {
            MaterialAnalyzer window = GetWindow<MaterialAnalyzer>("Material Analyzer");
            window.Show();
        }

        private void OnGUI()
        {
            windowScrollPosition = EditorGUILayout.BeginScrollView(windowScrollPosition);

            GUILayout.Label("Scene Material Analysis", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Analyze Materials", GUILayout.Height(30)))
            {
                AnalyzeMaterials();
            }

            EditorGUILayout.Space();

            if (uniqueMaterialCount > 0 || missingMaterialSlots > 0)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Unique Materials:", uniqueMaterialCount.ToString("N0"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Unique Shaders:", uniqueShaderCount.ToString("N0"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Unique Textures:", uniqueTextureCount.ToString("N0"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Texture Memory (VRAM):", FormatBytes(uniqueTextureMemory), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Material Slots:", totalMaterialSlots.ToString("N0"));
                EditorGUILayout.LabelField("Renderers:", rendererCount.ToString("N0"));
                if (missingMaterialSlots > 0)
                {
                    EditorGUILayout.LabelField("Missing Materials:", missingMaterialSlots.ToString("N0"));
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                viewMode = (ViewMode)GUILayout.Toolbar((int)viewMode, viewModeLabels);
                EditorGUILayout.Space();

                switch (viewMode)
                {
                    case ViewMode.Shaders:
                        DrawShaderGroups();
                        break;
                    case ViewMode.Textures:
                        DrawTextures();
                        break;
                    case ViewMode.Impact:
                        DrawImpact();
                        break;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawShaderGroups()
        {
            EditorGUILayout.LabelField("Materials Grouped by Shader:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Materials are grouped by their shader. Click a shader header to expand its materials.", MessageType.Info);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(400));

            foreach (var group in shaderGroups)
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                bool expanded = expandedShaders.Contains(group.shaderName);
                bool newExpanded = EditorGUILayout.Foldout(expanded, GetShaderDisplayName(group.shaderName), true, EditorStyles.foldout);
                if (newExpanded != expanded)
                {
                    if (newExpanded)
                    {
                        expandedShaders.Add(group.shaderName);
                    }
                    else
                    {
                        expandedShaders.Remove(group.shaderName);
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{group.materials.Count} materials | {group.TotalUsage} uses", GUILayout.Width(180));
                EditorGUILayout.EndHorizontal();

                if (newExpanded)
                {
                    foreach (var usage in group.materials.OrderByDescending(m => m.usageCount))
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(16);
                        if (GUILayout.Button(usage.materialName, EditorStyles.linkLabel, GUILayout.Width(240)))
                        {
                            SelectMaterial(usage.material);
                        }
                        EditorGUILayout.LabelField($"Used: {usage.usageCount}x", GUILayout.Width(80));
                        if (GUILayout.Button("Select Users", GUILayout.Width(100)))
                        {
                            SelectObjects(usage.usedByObjects);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTextures()
        {
            EditorGUILayout.LabelField("Textures by Memory Footprint:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("VRAM estimated from the runtime memory profiler (accounts for format, resolution and mipmaps). Deduplicated \u2014 a shared texture is counted once.", MessageType.Info);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Deduplicated VRAM:", FormatBytes(uniqueTextureMemory), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("If Not Shared:", FormatBytes(summedTextureMemory));
            long saved = summedTextureMemory - uniqueTextureMemory;
            if (saved > 0)
            {
                EditorGUILayout.LabelField("Saved by Sharing:", FormatBytes(saved));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(380));

            foreach (TextureUsage tex in sortedTextures)
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(tex.textureName, EditorStyles.linkLabel, GUILayout.Width(240)))
                {
                    SelectObject(tex.texture);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(FormatBytes(tex.memoryBytes), EditorStyles.boldLabel, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{tex.width}x{tex.height} | {tex.format}", GUILayout.Width(240));
                EditorGUILayout.LabelField($"Used: {tex.usageCount}x", GUILayout.Width(70));
                List<string> flags = new List<string>();
                if (!tex.hasMipmaps) flags.Add("No Mips");
                if (!tex.isCompressed) flags.Add("Uncompressed");
                if (!tex.isPowerOfTwo) flags.Add("NPOT");
                if (tex.width >= 2048 || tex.height >= 2048) flags.Add("Large");
                if (flags.Count > 0)
                {
                    EditorGUILayout.LabelField(string.Join(", ", flags), GUILayout.ExpandWidth(true));
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawImpact()
        {
            EditorGUILayout.LabelField("Performance Impact Estimate:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Heuristic estimates based on measurable factors: texture memory, texture samplers, shader passes, keyword variants and transparency (overdraw). These are relative indicators, not a substitute for GPU profiling.", MessageType.Info);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Memory", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Texture VRAM (deduplicated):", FormatBytes(uniqueTextureMemory));
            EditorGUILayout.LabelField("Uncompressed Textures:", $"{uncompressedTextureCount} ({FormatBytes(uncompressedTextureMemory)})");
            EditorGUILayout.LabelField("Textures Without Mipmaps:", texturesWithoutMipmaps.ToString("N0"));
            EditorGUILayout.LabelField("Large Textures (>=2048):", largeTextureCount.ToString("N0"));
            EditorGUILayout.LabelField("Non-Power-of-Two:", nonPowerOfTwoCount.ToString("N0"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Shading / Fill Rate", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Transparent Materials:", $"{transparentMaterialCount} of {uniqueMaterialCount}");
            EditorGUILayout.LabelField("Avg Texture Samplers / Material:", avgSamplersPerMaterial.ToString("F1"));
            EditorGUILayout.LabelField("Avg Shader Keywords / Material:", avgKeywordsPerMaterial.ToString("F1"));
            EditorGUILayout.LabelField("Max Shader Passes:", maxShaderPasses.ToString("N0"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            EmitWarnings();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Heaviest Materials (by complexity score):", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(240));

            foreach (MaterialUsage usage in materialUsage.Values.OrderByDescending(m => m.ComplexityScore).ThenByDescending(m => m.textureMemory))
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(usage.materialName, EditorStyles.linkLabel, GUILayout.Width(220)))
                {
                    SelectObject(usage.material);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"Score: {usage.ComplexityScore}", EditorStyles.boldLabel, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(
                    $"{usage.samplerCount} tex | {usage.passCount} pass | {usage.keywordCount} kw | {(usage.isTransparent ? "Transparent" : "Opaque")} | {FormatBytes(usage.textureMemory)} | x{usage.usageCount}");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void EmitWarnings()
        {
            List<string> warnings = new List<string>();
            if (uncompressedTextureCount > 0)
            {
                warnings.Add($"{uncompressedTextureCount} uncompressed texture(s) using {FormatBytes(uncompressedTextureMemory)} \u2014 consider a compressed format (BC7/DXT/ASTC).");
            }
            if (texturesWithoutMipmaps > 0)
            {
                warnings.Add($"{texturesWithoutMipmaps} texture(s) without mipmaps \u2014 can cause aliasing and cache misses on distant surfaces.");
            }
            if (largeTextureCount > 0)
            {
                warnings.Add($"{largeTextureCount} texture(s) at 2048+ \u2014 verify the resolution is warranted, especially for VR.");
            }
            if (nonPowerOfTwoCount > 0)
            {
                warnings.Add($"{nonPowerOfTwoCount} non-power-of-two texture(s) \u2014 may skip compression/mips.");
            }
            if (transparentMaterialCount > 0)
            {
                warnings.Add($"{transparentMaterialCount} transparent material(s) \u2014 overdraw is a common VR fill-rate bottleneck.");
            }

            if (warnings.Count == 0)
            {
                EditorGUILayout.HelpBox("No common material/texture red flags detected.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(string.Join("\n\n", warnings), MessageType.Warning);
        }

        {
            totalMaterialSlots = 0;
            uniqueMaterialCount = 0;
            uniqueShaderCount = 0;
            rendererCount = 0;
            missingMaterialSlots = 0;
            uniqueTextureCount = 0;
            uniqueTextureMemory = 0;
            summedTextureMemory = 0;
            texturesWithoutMipmaps = 0;
            uncompressedTextureCount = 0;
            uncompressedTextureMemory = 0;
            nonPowerOfTwoCount = 0;
            largeTextureCount = 0;
            transparentMaterialCount = 0;
            avgSamplersPerMaterial = 0f;
            avgKeywordsPerMaterial = 0f;
            maxShaderPasses = 0;
            shaderGroups.Clear();
            materialUsage.Clear();
            textureUsage.Clear();
            sortedTextures.Clear();

            Renderer[] renderers = FindObjectsOfType<Renderer>();
            rendererCount = renderers.Length;

            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                totalMaterialSlots += materials.Length;

                foreach (Material material in materials)
                {
                    if (material == null)
                    {
                        missingMaterialSlots++;
                        continue;
                    }

                    TrackMaterial(material, renderer.gameObject);
                }
            }

            AnalyzeTexturesAndImpact();
            BuildShaderGroups();

            uniqueMaterialCount = materialUsage.Count;
            uniqueShaderCount = shaderGroups.Count;
            uniqueTextureCount = textureUsage.Count;

            Debug.Log($"[Material Analyzer] Materials: {uniqueMaterialCount} | Shaders: {uniqueShaderCount} | Textures: {uniqueTextureCount} | VRAM: {FormatBytes(uniqueTextureMemory)}");
            Repaint();
        }

        private void TrackMaterial(Material material, GameObject gameObject)
        {
            if (materialUsage.ContainsKey(material))
            {
                materialUsage[material].usageCount++;
                if (!materialUsage[material].usedByObjects.Contains(gameObject))
                {
                    materialUsage[material].usedByObjects.Add(gameObject);
                }
            }
            else
            {
                materialUsage[material] = new MaterialUsage
                {
                    material = material,
                    materialName = material.name,
                    shaderName = material.shader != null ? material.shader.name : "(No Shader)",
                    usageCount = 1,
                    usedByObjects = new List<GameObject> { gameObject }
                };
            }
        }

        // Walks each material's texture properties, deduplicates textures, estimates VRAM
        // via the runtime memory profiler, and derives per-material impact metrics.
        private void AnalyzeTexturesAndImpact()
        {
            int samplerTotal = 0;
            int keywordTotal = 0;

            foreach (MaterialUsage usage in materialUsage.Values)
            {
                Material material = usage.material;
                Shader shader = material.shader;

                usage.renderQueue = material.renderQueue;
                usage.isTransparent = material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent;
                usage.keywordCount = material.shaderKeywords != null ? material.shaderKeywords.Length : 0;
                usage.passCount = material.passCount;
                maxShaderPasses = Mathf.Max(maxShaderPasses, usage.passCount);

                if (usage.isTransparent)
                {
                    transparentMaterialCount++;
                }

                if (shader == null)
                {
                    continue;
                }

                int propCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        continue;
                    }

                    string propName = ShaderUtil.GetPropertyName(shader, i);
                    Texture tex = material.GetTexture(propName);
                    if (tex == null)
                    {
                        continue;
                    }

                    usage.samplerCount++;
                    TextureUsage texInfo = TrackTexture(tex, material);
                    usage.textureMemory += texInfo.memoryBytes;
                    summedTextureMemory += texInfo.memoryBytes;
                }

                samplerTotal += usage.samplerCount;
                keywordTotal += usage.keywordCount;
            }

            foreach (TextureUsage tex in textureUsage.Values)
            {
                uniqueTextureMemory += tex.memoryBytes;
                if (!tex.hasMipmaps)
                {
                    texturesWithoutMipmaps++;
                }
                if (!tex.isCompressed)
                {
                    uncompressedTextureCount++;
                    uncompressedTextureMemory += tex.memoryBytes;
                }
                if (!tex.isPowerOfTwo)
                {
                    nonPowerOfTwoCount++;
                }
                if (tex.width >= 2048 || tex.height >= 2048)
                {
                    largeTextureCount++;
                }
            }

            int matCount = Mathf.Max(1, materialUsage.Count);
            avgSamplersPerMaterial = samplerTotal / (float)matCount;
            avgKeywordsPerMaterial = keywordTotal / (float)matCount;

            sortedTextures = textureUsage.Values.OrderByDescending(t => t.memoryBytes).ToList();
        }

        private TextureUsage TrackTexture(Texture tex, Material material)
        {
            if (textureUsage.TryGetValue(tex, out TextureUsage existing))
            {
                existing.usageCount++;
                if (!existing.usedByMaterials.Contains(material))
                {
                    existing.usedByMaterials.Add(material);
                }
                return existing;
            }

            bool hasMips = false;
            bool isCompressed = false;
            string format = tex.GetType().Name;
            if (tex is Texture2D tex2D)
            {
                hasMips = tex2D.mipmapCount > 1;
                format = tex2D.format.ToString();
                isCompressed = IsCompressedFormat(tex2D.format);
            }
            else if (tex is Cubemap cube)
            {
                hasMips = cube.mipmapCount > 1;
                format = cube.format.ToString();
                isCompressed = IsCompressedFormat(cube.format);
            }

            TextureUsage info = new TextureUsage
            {
                texture = tex,
                textureName = tex.name,
                width = tex.width,
                height = tex.height,
                format = format,
                hasMipmaps = hasMips,
                isCompressed = isCompressed,
                isPowerOfTwo = Mathf.IsPowerOfTwo(tex.width) && Mathf.IsPowerOfTwo(tex.height),
                memoryBytes = Profiler.GetRuntimeMemorySizeLong(tex),
                usageCount = 1,
                usedByMaterials = new List<Material> { material }
            };
            textureUsage[tex] = info;
            return info;
        }

        private void BuildShaderGroups()
        {
            shaderGroups = materialUsage.Values
                .GroupBy(m => m.shaderName)
                .Select(g => new ShaderGroup
                {
                    shaderName = g.Key,
                    materials = g.ToList()
                })
                .OrderByDescending(g => g.TotalUsage)
                .ToList();
        }

        private static string GetShaderDisplayName(string shaderName)
        {
            int slash = shaderName.LastIndexOf('/');
            string leaf = slash >= 0 && slash < shaderName.Length - 1 ? shaderName.Substring(slash + 1) : shaderName;
            return $"{leaf}  ({shaderName})";
        }

        private static void SelectMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
        }

        private static void SelectObject(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        private static bool IsCompressedFormat(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT5:
                case TextureFormat.BC4:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC2_RGB:
                case TextureFormat.ETC2_RGBA1:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_5x5:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                case TextureFormat.ASTC_10x10:
                case TextureFormat.ASTC_12x12:
                    return true;
                default:
                    return false;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }

        private static void SelectObjects(List<GameObject> objects)
        {
            if (objects == null || objects.Count == 0)
            {
                return;
            }

            GameObject[] valid = objects.Where(o => o != null).ToArray();
            if (valid.Length == 0)
            {
                return;
            }

            Selection.objects = valid;
            EditorGUIUtility.PingObject(valid[0]);
        }
    }
}
