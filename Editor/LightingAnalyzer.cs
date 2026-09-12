using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace DoggoBrutus.WorldDev.Editor
{
    public class LightingAnalyzer : EditorWindow
    {
        private int totalLights = 0;
        private int realtimeLights = 0;
        private int bakedLights = 0;
        private int mixedLights = 0;
        private int directionalLights = 0;
        private int pointLights = 0;
        private int spotLights = 0;
        private int areaLights = 0;
        
        private int lightProbeGroups = 0;
        private int totalLightProbes = 0;
        private int reflectionProbes = 0;

        private float duplicateProbeDistance = 0.1f;
        private int duplicateProbeCount = 0;
        private bool duplicateAnalysisDone = false;

        private bool lightmapDataAvailable = false;
        private int lightmapCount = 0;
        private long totalLightmapSize = 0;

        private Vector2 windowScrollPosition;
        private Vector2 scrollPosition;
        private Vector2 probeObjectsScrollPosition;
        private List<LightInfo> lightInfoList = new List<LightInfo>();
        private List<LightProbeObjectInfo> probeObjectList = new List<LightProbeObjectInfo>();

        private class LightInfo
        {
            public string objectName;
            public LightType lightType;
            public LightmapBakeType bakeType;
            public float intensity;
            public float range;
            public Color color;
        }

        private class LightProbeObjectInfo
        {
            public GameObject gameObject;
            public string objectName;
            public string hierarchyPath;
            public string componentType;
            public int probeCount;
            public bool isEnabled;
        }

        [MenuItem("Tools/DB's worlddev-tools/Lighting Analyzer")]
        public static void ShowWindow()
        {
            LightingAnalyzer window = GetWindow<LightingAnalyzer>("Lighting Analyzer");
            window.Show();
        }

        private void OnGUI()
        {
            windowScrollPosition = EditorGUILayout.BeginScrollView(windowScrollPosition);

            GUILayout.Label("Scene Lighting Statistics", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Analyze Lighting", GUILayout.Height(30)))
            {
                AnalyzeLighting();
            }

            EditorGUILayout.Space();

            if (totalLights > 0 || lightProbeGroups > 0 || probeObjectList.Count > 0)
            {
                // Light Summary
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Light Summary", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Total Lights:", totalLights.ToString());
                EditorGUILayout.Space();
                
                EditorGUILayout.LabelField("By Baking Mode:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("  Realtime Lights:", realtimeLights.ToString());
                EditorGUILayout.LabelField("  Baked Lights:", bakedLights.ToString());
                EditorGUILayout.LabelField("  Mixed Lights:", mixedLights.ToString());
                EditorGUILayout.Space();
                
                EditorGUILayout.LabelField("By Type:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("  Directional:", directionalLights.ToString());
                EditorGUILayout.LabelField("  Point:", pointLights.ToString());
                EditorGUILayout.LabelField("  Spot:", spotLights.ToString());
                EditorGUILayout.LabelField("  Area:", areaLights.ToString());
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                // Light Probe & Reflection Probe Summary
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Probe Information", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Light Probe Groups:", lightProbeGroups.ToString());
                EditorGUILayout.LabelField("Total Light Probes:", totalLightProbes.ToString());
                EditorGUILayout.LabelField("Reflection Probes:", reflectionProbes.ToString());
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                // Duplicate Light Probe Cleanup
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Duplicate Light Probe Cleanup", EditorStyles.boldLabel);
                duplicateProbeDistance = EditorGUILayout.FloatField("Merge Distance (m):", duplicateProbeDistance);
                if (duplicateProbeDistance < 0f)
                {
                    duplicateProbeDistance = 0f;
                }

                if (GUILayout.Button("Find Duplicate Probes"))
                {
                    FindDuplicateProbes();
                }

                if (duplicateAnalysisDone)
                {
                    if (duplicateProbeCount > 0)
                    {
                        EditorGUILayout.HelpBox($"Found {duplicateProbeCount} duplicate probe(s) within {duplicateProbeDistance:F2}m.", MessageType.Warning);
                        if (GUILayout.Button($"Remove {duplicateProbeCount} Duplicate Probe(s)"))
                        {
                            RemoveDuplicateProbes();
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("No duplicate probes found.", MessageType.Info);
                    }
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                // Light Probe Objects in Hierarchy
                if (probeObjectList.Count > 0)
                {
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Light Probe Objects in Hierarchy ({probeObjectList.Count}):", EditorStyles.boldLabel);
                    if (GUILayout.Button("Select All", GUILayout.Width(80)))
                    {
                        Selection.objects = probeObjectList.Where(p => p.gameObject != null).Select(p => (Object)p.gameObject).ToArray();
                    }
                    EditorGUILayout.EndHorizontal();

                    probeObjectsScrollPosition = EditorGUILayout.BeginScrollView(probeObjectsScrollPosition, GUILayout.Height(150));
                    foreach (var info in probeObjectList)
                    {
                        if (info.gameObject == null) continue;

                        EditorGUILayout.BeginHorizontal("box");
                        if (GUILayout.Button(new GUIContent(info.objectName, info.hierarchyPath), GUILayout.Width(180)))
                        {
                            Selection.activeGameObject = info.gameObject;
                            EditorGUIUtility.PingObject(info.gameObject);
                        }
                        EditorGUILayout.LabelField($"[{info.componentType}]", GUILayout.Width(140));
                        if (info.componentType == "LightProbeGroup")
                        {
                            EditorGUILayout.LabelField($"{info.probeCount:N0} probes", GUILayout.Width(90));
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Proxy Volume", GUILayout.Width(90));
                        }
                        EditorGUILayout.LabelField(info.isEnabled ? "Active" : "Inactive", GUILayout.Width(60));
                        if (GUILayout.Button("Select", GUILayout.Width(60)))
                        {
                            Selection.activeGameObject = info.gameObject;
                            EditorGUIUtility.PingObject(info.gameObject);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space();
                }

                // Lightmap Information
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Lightmap Data", EditorStyles.boldLabel);
                if (lightmapDataAvailable)
                {
                    EditorGUILayout.LabelField("Lightmap Count:", lightmapCount.ToString());
                    EditorGUILayout.LabelField("Total Lightmap Size:", FormatBytes(totalLightmapSize));
                }
                else
                {
                    EditorGUILayout.LabelField("No lightmap data available", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                // Performance Warning
                if (realtimeLights > 4)
                {
                    EditorGUILayout.HelpBox($"Warning: {realtimeLights} realtime lights detected. Consider baking lights for better performance.", MessageType.Warning);
                }

                // Detailed Light List
                if (lightInfoList.Count > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Detailed Light List:", EditorStyles.boldLabel);
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

                    foreach (var info in lightInfoList)
                    {
                        EditorGUILayout.BeginHorizontal("box");
                        EditorGUILayout.LabelField(info.objectName, GUILayout.Width(150));
                        EditorGUILayout.LabelField(info.lightType.ToString(), GUILayout.Width(80));
                        EditorGUILayout.LabelField(info.bakeType.ToString(), GUILayout.Width(80));
                        EditorGUILayout.LabelField($"Int: {info.intensity:F1}", GUILayout.Width(70));
                        if (info.lightType != LightType.Directional)
                        {
                            EditorGUILayout.LabelField($"Range: {info.range:F1}", GUILayout.Width(80));
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void AnalyzeLighting()
        {
            // Reset counters
            totalLights = 0;
            realtimeLights = 0;
            bakedLights = 0;
            mixedLights = 0;
            directionalLights = 0;
            pointLights = 0;
            spotLights = 0;
            areaLights = 0;
            lightProbeGroups = 0;
            totalLightProbes = 0;
            reflectionProbes = 0;
            duplicateProbeCount = 0;
            duplicateAnalysisDone = false;
            lightInfoList.Clear();
            probeObjectList.Clear();

            // Analyze Lights
            Light[] lights = FindObjectsOfType<Light>();
            totalLights = lights.Length;

            foreach (Light light in lights)
            {
                // Count by bake type
                switch (light.lightmapBakeType)
                {
                    case LightmapBakeType.Realtime:
                        realtimeLights++;
                        break;
                    case LightmapBakeType.Baked:
                        bakedLights++;
                        break;
                    case LightmapBakeType.Mixed:
                        mixedLights++;
                        break;
                }

                // Count by type
                switch (light.type)
                {
                    case LightType.Directional:
                        directionalLights++;
                        break;
                    case LightType.Point:
                        pointLights++;
                        break;
                    case LightType.Spot:
                        spotLights++;
                        break;
                    case LightType.Area:
                        areaLights++;
                        break;
                }

                // Add to detailed list
                lightInfoList.Add(new LightInfo
                {
                    objectName = light.gameObject.name,
                    lightType = light.type,
                    bakeType = light.lightmapBakeType,
                    intensity = light.intensity,
                    range = light.range,
                    color = light.color
                });
            }

            // Analyze Light Probes & Objects storing light probe data
            LightProbeGroup[] probeGroups = FindObjectsOfType<LightProbeGroup>();
            lightProbeGroups = probeGroups.Length;
            foreach (LightProbeGroup group in probeGroups)
            {
                int count = 0;
                if (group.probePositions != null)
                {
                    count = group.probePositions.Length;
                    totalLightProbes += count;
                }

                probeObjectList.Add(new LightProbeObjectInfo
                {
                    gameObject = group.gameObject,
                    objectName = group.gameObject.name,
                    hierarchyPath = GetHierarchyPath(group.transform),
                    componentType = "LightProbeGroup",
                    probeCount = count,
                    isEnabled = group.enabled && group.gameObject.activeInHierarchy
                });
            }

            LightProbeProxyVolume[] proxyVolumes = FindObjectsOfType<LightProbeProxyVolume>();
            foreach (LightProbeProxyVolume proxy in proxyVolumes)
            {
                probeObjectList.Add(new LightProbeObjectInfo
                {
                    gameObject = proxy.gameObject,
                    objectName = proxy.gameObject.name,
                    hierarchyPath = GetHierarchyPath(proxy.transform),
                    componentType = "LightProbeProxyVolume",
                    probeCount = 0,
                    isEnabled = proxy.enabled && proxy.gameObject.activeInHierarchy
                });
            }

            // Analyze Reflection Probes
            ReflectionProbe[] reflectionProbeArray = FindObjectsOfType<ReflectionProbe>();
            reflectionProbes = reflectionProbeArray.Length;

            // Analyze Lightmap Data
            AnalyzeLightmaps();

            // Log summary
            Debug.Log($"[Lighting Analyzer] Lights: {totalLights} (RT:{realtimeLights}, Baked:{bakedLights}, Mixed:{mixedLights}) | Light Probes: {totalLightProbes} | Reflection Probes: {reflectionProbes}");
            
            Repaint();
        }

        // Collects duplicate probe indices per group. A probe is a duplicate when it lies within
        // duplicateProbeDistance (world space) of an earlier-kept probe.
        private Dictionary<LightProbeGroup, HashSet<int>> CollectDuplicateProbes()
        {
            var duplicatesByGroup = new Dictionary<LightProbeGroup, HashSet<int>>();
            var keptWorldPositions = new List<Vector3>();
            float sqrThreshold = duplicateProbeDistance * duplicateProbeDistance;

            LightProbeGroup[] groups = FindObjectsOfType<LightProbeGroup>();
            foreach (LightProbeGroup group in groups)
            {
                Vector3[] positions = group.probePositions;
                if (positions == null)
                {
                    continue;
                }

                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 world = group.transform.TransformPoint(positions[i]);

                    bool isDuplicate = false;
                    for (int k = 0; k < keptWorldPositions.Count; k++)
                    {
                        if ((keptWorldPositions[k] - world).sqrMagnitude <= sqrThreshold)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }

                    if (isDuplicate)
                    {
                        if (!duplicatesByGroup.TryGetValue(group, out HashSet<int> set))
                        {
                            set = new HashSet<int>();
                            duplicatesByGroup[group] = set;
                        }
                        set.Add(i);
                    }
                    else
                    {
                        keptWorldPositions.Add(world);
                    }
                }
            }

            return duplicatesByGroup;
        }

        private void FindDuplicateProbes()
        {
            var duplicatesByGroup = CollectDuplicateProbes();
            duplicateProbeCount = duplicatesByGroup.Values.Sum(set => set.Count);
            duplicateAnalysisDone = true;

            Debug.Log($"[Lighting Analyzer] Found {duplicateProbeCount} duplicate light probe(s) within {duplicateProbeDistance:F2}m.");
            Repaint();
        }

        private void RemoveDuplicateProbes()
        {
            var duplicatesByGroup = CollectDuplicateProbes();
            int removed = 0;

            foreach (var pair in duplicatesByGroup)
            {
                LightProbeGroup group = pair.Key;
                HashSet<int> duplicates = pair.Value;
                Vector3[] positions = group.probePositions;
                if (positions == null || duplicates.Count == 0)
                {
                    continue;
                }

                var kept = new List<Vector3>(positions.Length - duplicates.Count);
                for (int i = 0; i < positions.Length; i++)
                {
                    if (!duplicates.Contains(i))
                    {
                        kept.Add(positions[i]);
                    }
                }

                Undo.RecordObject(group, "Remove Duplicate Light Probes");
                group.probePositions = kept.ToArray();
                EditorUtility.SetDirty(group);
                removed += duplicates.Count;
            }

            Debug.Log($"[Lighting Analyzer] Removed {removed} duplicate light probe(s).");

            duplicateProbeCount = 0;
            duplicateAnalysisDone = false;
            AnalyzeLighting();
        }

        private void AnalyzeLightmaps()
        {
            lightmapDataAvailable = LightmapSettings.lightmaps != null && LightmapSettings.lightmaps.Length > 0;
            
            if (lightmapDataAvailable)
            {
                lightmapCount = LightmapSettings.lightmaps.Length;
                totalLightmapSize = 0;

                foreach (LightmapData lightmap in LightmapSettings.lightmaps)
                {
                    if (lightmap.lightmapColor != null)
                    {
                        totalLightmapSize += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(lightmap.lightmapColor);
                    }
                    if (lightmap.lightmapDir != null)
                    {
                        totalLightmapSize += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(lightmap.lightmapDir);
                    }
                }
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F2} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F2} MB";
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
