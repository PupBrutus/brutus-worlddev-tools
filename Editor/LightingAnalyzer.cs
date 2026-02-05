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
        
        private bool lightmapDataAvailable = false;
        private int lightmapCount = 0;
        private long totalLightmapSize = 0;
        
        private Vector2 scrollPosition;
        private List<LightInfo> lightInfoList = new List<LightInfo>();

        private class LightInfo
        {
            public string objectName;
            public LightType lightType;
            public LightmapBakeType bakeType;
            public float intensity;
            public float range;
            public Color color;
        }

        [MenuItem("Tools/DB's worlddev-tools/Lighting Analyzer")]
        public static void ShowWindow()
        {
            LightingAnalyzer window = GetWindow<LightingAnalyzer>("Lighting Analyzer");
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Scene Lighting Statistics", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Analyze Lighting", GUILayout.Height(30)))
            {
                AnalyzeLighting();
            }

            EditorGUILayout.Space();

            if (totalLights > 0 || lightProbeGroups > 0)
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
            lightInfoList.Clear();

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

            // Analyze Light Probes
            LightProbeGroup[] probeGroups = FindObjectsOfType<LightProbeGroup>();
            lightProbeGroups = probeGroups.Length;
            foreach (LightProbeGroup group in probeGroups)
            {
                if (group.probePositions != null)
                {
                    totalLightProbes += group.probePositions.Length;
                }
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
    }
}
