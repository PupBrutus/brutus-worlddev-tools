using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DoggoBrutus.WorldDev.Editor
{
    public class VRChatNetworkProfiler : EditorWindow
    {
        private int totalUdonBehaviours = 0;
        private int continuousSyncBehaviours = 0;
        private int manualSyncBehaviours = 0;
        private int noneSyncBehaviours = 0;
        private int unknownSyncBehaviours = 0;
        private int vrcManagedSyncObjects = 0;
        
        private int totalSyncedVariables = 0;
        private int totalVRCComponents = 0;
        
        private Dictionary<string, int> componentTypeCounts = new Dictionary<string, int>();
        private Dictionary<int, NetworkObjectInfo> networkObjectsById = new Dictionary<int, NetworkObjectInfo>();
        private List<NetworkObjectInfo> networkObjects = new List<NetworkObjectInfo>();
        
        private Vector2 scrollPosition;
        private bool showDetailedList = true;
        private SyncModeFilter syncModeFilter = SyncModeFilter.All;
        private ViewMode viewMode = ViewMode.Summary;
        private DetailsView detailsView = DetailsView.ByObject;
        private Dictionary<string, int> scriptSyncSelections = new Dictionary<string, int>();
        private bool pendingSyncApply = false;
        private string pendingSyncKey = string.Empty;
        private string pendingSyncMode = string.Empty;

        private static readonly string[] SyncModeOptions = { "None", "Manual", "Continuous" };

        private const float ContinuousSyncRateHz = 10f;
        private const float ManualSyncRateHz = 0.2f;
        private const float BuiltInSyncRateHz = 5f;
        private const float ManagedSyncRateHz = 1f;
        private const float BaseBytesPerUpdate = 24f;
        private const float BytesPerSyncedVar = 12f;

        private class NetworkObjectInfo
        {
            public string objectName;
            public string hierarchyPath;
            public GameObject gameObject;
            public List<NetworkComponentInfo> components = new List<NetworkComponentInfo>();
        }

        private class NetworkComponentInfo
        {
            public string componentType;
            public string syncMode;
            public int syncedVariableCount;
            public List<string> syncedVariables;
            public UnityEngine.Object programSourceAsset;
            public string programSourcePath;
            public MonoBehaviour componentInstance;
        }

        [Flags]
        private enum SyncModeFilter
        {
            None = 0,
            Continuous = 1 << 0,
            Manual = 1 << 1,
            SyncNone = 1 << 2,
            VrcManaged = 1 << 3,
            BuiltIn = 1 << 4,
            Unknown = 1 << 5,
            All = Continuous | Manual | SyncNone | VrcManaged | BuiltIn | Unknown
        }

        private enum ViewMode
        {
            Summary = 0,
            Details = 1
        }

        private enum DetailsView
        {
            ByObject = 0,
            ByScript = 1
        }

        [MenuItem("Tools/DB's worlddev-tools/Udon Behavior Profiler")]
        public static void ShowWindow()
        {
            VRChatNetworkProfiler window = GetWindow<VRChatNetworkProfiler>("Udon Behavior Profiler");
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Udon Behavior Profiler", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Analyzes UdonBehaviours and related components for behavior and network sync usage.", MessageType.Info);
            EditorGUILayout.Space();

            if (GUILayout.Button("Analyze Behaviors", GUILayout.Height(30)))
            {
                AnalyzeNetworkObjects();
            }

            EditorGUILayout.Space();

            if (totalUdonBehaviours > 0 || totalVRCComponents > 0)
            {
                viewMode = (ViewMode)GUILayout.Toolbar((int)viewMode, new[] { "Summary", "Details" });
                EditorGUILayout.Space();

                if (viewMode == ViewMode.Summary)
                {
                    // Udon Behaviour Summary
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("Udon Behaviour Summary", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Total UdonBehaviours:", totalUdonBehaviours.ToString());
                    EditorGUILayout.Space();
                    
                    EditorGUILayout.LabelField("By Sync Mode:", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("  Continuous:", continuousSyncBehaviours.ToString());
                    EditorGUILayout.LabelField("  Manual:", manualSyncBehaviours.ToString());
                    EditorGUILayout.LabelField("  None:", noneSyncBehaviours.ToString());
                    EditorGUILayout.LabelField("  VRC Managed:", vrcManagedSyncObjects.ToString());
                    EditorGUILayout.LabelField("  Unknown:", unknownSyncBehaviours.ToString());
                    EditorGUILayout.Space();
                    
                    EditorGUILayout.LabelField("Total Synced Variables:", totalSyncedVariables.ToString());
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space();

                    // VRC Component Summary
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("VRChat Component Summary", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Total VRC Components:", totalVRCComponents.ToString());
                    
                    if (componentTypeCounts.Count > 0)
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField("Component Breakdown:", EditorStyles.boldLabel);
                        foreach (var kvp in componentTypeCounts.OrderByDescending(x => x.Value))
                        {
                            EditorGUILayout.LabelField($"  {kvp.Key}:", kvp.Value.ToString());
                        }
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space();

                    // Network Performance Estimate
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("Network Intensity Estimate", EditorStyles.boldLabel);
                    
                    float intensityScore = CalculateNetworkIntensity();
                    string intensityRating = GetIntensityRating(intensityScore);
                    Color originalColor = GUI.color;
                    
                    if (intensityScore > 75) GUI.color = Color.red;
                    else if (intensityScore > 40) GUI.color = Color.yellow;
                    else GUI.color = Color.green;
                    
                    EditorGUILayout.LabelField($"Intensity Score: {intensityScore:F1}/100 ({intensityRating})", EditorStyles.boldLabel);
                    GUI.color = originalColor;
                    
                    EditorGUILayout.Space();
                    float estimatedKbps = EstimateBandwidthKbps();
                    EditorGUILayout.LabelField($"Estimated Bandwidth: {estimatedKbps:F1} kbps (approx.)", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("Assumes Continuous~10Hz, Manual~0.2Hz; 24B base + 12B/var", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();

                    // Performance Warnings
                    if (continuousSyncBehaviours > 10)
                    {
                        EditorGUILayout.HelpBox($"Warning: {continuousSyncBehaviours} continuous sync behaviours detected. This may cause high network traffic.", MessageType.Warning);
                    }
                }
                else
                {
                    // Detailed Object List
                    showDetailedList = EditorGUILayout.Foldout(showDetailedList, "Detailed Network Object List", true);
                    
                    if (showDetailedList && networkObjects.Count > 0)
                    {
                        detailsView = (DetailsView)GUILayout.Toolbar((int)detailsView, new[] { "By Object", "By Script" });
                        EditorGUILayout.Space();
                        syncModeFilter = (SyncModeFilter)EditorGUILayout.EnumFlagsField("Sync Mode Filter", syncModeFilter);
                        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

                        if (detailsView == DetailsView.ByObject)
                        {
                            var orderedObjects = networkObjects
                                .OrderByDescending(EstimateObjectBandwidthKbps)
                                .ToList();

                            foreach (var obj in orderedObjects)
                            {
                                bool hasVisibleComponent = false;
                                foreach (var component in obj.components)
                                {
                                    if (!IsComponentVisible(component))
                                    {
                                        continue;
                                    }

                                    hasVisibleComponent = true;
                                    break;
                                }

                                if (!hasVisibleComponent)
                                {
                                    continue;
                                }

                                EditorGUILayout.BeginVertical("box");
                                EditorGUILayout.BeginHorizontal();
                                float objectKbps = EstimateObjectBandwidthKbps(obj);
                                float objectScore = EstimateObjectIntensityScore(obj);
                                if (GUILayout.Button(new GUIContent(obj.objectName, obj.hierarchyPath), GUILayout.Width(200)))
                                {
                                    SelectObject(obj);
                                }
                                EditorGUILayout.LabelField($"Components: {obj.components.Count}", GUILayout.Width(150));
                                EditorGUILayout.LabelField($"Score: {objectScore:F1}  Kbps: {objectKbps:F1}", GUILayout.Width(160));
                                if (GUILayout.Button("Select", GUILayout.Width(60)))
                                {
                                    SelectObject(obj);
                                }
                                EditorGUILayout.EndHorizontal();

                                foreach (var component in obj.components)
                                {
                                    if (!IsComponentVisible(component))
                                    {
                                        continue;
                                    }

                                    EditorGUILayout.LabelField($"[{component.componentType}] Sync: {component.syncMode}", EditorStyles.miniLabel);

                                    if (component.syncedVariableCount > 0)
                                    {
                                        EditorGUILayout.LabelField($"Synced Variables ({component.syncedVariableCount}):", EditorStyles.miniLabel);
                                        foreach (var variable in component.syncedVariables)
                                        {
                                            EditorGUILayout.LabelField($"  • {variable}", EditorStyles.miniLabel);
                                        }
                                    }

                                    if (component.programSourceAsset != null)
                                    {
                                        EditorGUILayout.ObjectField("Program Source", component.programSourceAsset, typeof(UnityEngine.Object), false);
                                        if (!string.IsNullOrEmpty(component.programSourcePath))
                                        {
                                            EditorGUILayout.LabelField($"Program Path: {component.programSourcePath}", EditorStyles.miniLabel);
                                        }
                                    }
                                }
                                EditorGUILayout.EndVertical();
                                EditorGUILayout.Space(2);
                            }
                        }
                        else
                        {
                            List<ScriptSummary> summaries = BuildScriptSummaries();
                            foreach (var summary in summaries)
                            {
                                bool canAdjust = CanAdjustSyncMode(summary);
                                int selectionIndex = GetOrCreateScriptSyncSelection(summary.key);
                                EditorGUILayout.BeginVertical("box");
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField(new GUIContent(summary.displayName, summary.tooltip), GUILayout.Width(240));
                                EditorGUILayout.LabelField($"Instances: {summary.instanceCount}", GUILayout.Width(120));
                                EditorGUILayout.LabelField($"Kbps: {summary.bandwidthKbps:F1}", GUILayout.Width(120));
                                if (canAdjust)
                                {
                                    selectionIndex = EditorGUILayout.Popup(selectionIndex, SyncModeOptions, GUILayout.Width(110));
                                    scriptSyncSelections[summary.key] = selectionIndex;
                                    if (GUILayout.Button("Set All", GUILayout.Width(70)))
                                    {
                                        string targetMode = SyncModeOptions[selectionIndex];
                                        string message = $"Set sync mode to {targetMode} for all {summary.instanceCount} instances of '{summary.displayName}' in the scene?";
                                        if (EditorUtility.DisplayDialog("Confirm Sync Mode Change", message, "Apply", "Cancel"))
                                        {
                                            pendingSyncApply = true;
                                            pendingSyncKey = summary.key;
                                            pendingSyncMode = targetMode;
                                        }
                                    }
                                }
                                EditorGUILayout.EndHorizontal();

                                if (!string.IsNullOrEmpty(summary.tooltip))
                                {
                                    EditorGUILayout.LabelField($"Path: {summary.tooltip}", EditorStyles.miniLabel);
                                }

                                EditorGUILayout.EndVertical();
                                EditorGUILayout.Space(2);
                            }

                            if (pendingSyncApply)
                            {
                                ScriptSummary targetSummary = summaries.FirstOrDefault(item => item.key == pendingSyncKey);
                                if (targetSummary != null)
                                {
                                    ApplySyncModeToSummary(targetSummary, pendingSyncMode);
                                    AnalyzeNetworkObjects();
                                    GUI.FocusControl(null);
                                }

                                pendingSyncApply = false;
                                pendingSyncKey = string.Empty;
                                pendingSyncMode = string.Empty;
                            }
                        }

                        EditorGUILayout.EndScrollView();
                    }
                }
            }
        }

        private void AnalyzeNetworkObjects()
        {
            // Reset counters
            totalUdonBehaviours = 0;
            continuousSyncBehaviours = 0;
            manualSyncBehaviours = 0;
            noneSyncBehaviours = 0;
            unknownSyncBehaviours = 0;
            vrcManagedSyncObjects = 0;
            totalSyncedVariables = 0;
            totalVRCComponents = 0;
            componentTypeCounts.Clear();
            networkObjectsById.Clear();
            networkObjects.Clear();

            // Find all MonoBehaviours in scene (including inactive objects)
            MonoBehaviour[] allBehaviours = GetAllSceneBehaviours();

            foreach (MonoBehaviour behaviour in allBehaviours)
            {
                if (behaviour == null) continue;

                string typeName = behaviour.GetType().Name;

                // Check for UdonBehaviour
                if (typeName == "UdonBehaviour" || behaviour.GetType().FullName.Contains("UdonBehaviour"))
                {
                    totalUdonBehaviours++;
                    AnalyzeUdonBehaviour(behaviour);
                }
                // Check for VRC components
                else if (typeName.StartsWith("VRC") || behaviour.GetType().Namespace != null && behaviour.GetType().Namespace.Contains("VRC"))
                {
                    totalVRCComponents++;
                    AnalyzeVRCComponent(behaviour);
                }
            }

            // Log summary
            Debug.Log($"[VRC Network Profiler] UdonBehaviours: {totalUdonBehaviours} (Cont:{continuousSyncBehaviours}, Manual:{manualSyncBehaviours}) | VRC Components: {totalVRCComponents} | Synced Vars: {totalSyncedVariables}");
            
            Repaint();
        }

        private void AnalyzeUdonBehaviour(MonoBehaviour udonBehaviour)
        {
            SerializedObject serializedObject = new SerializedObject(udonBehaviour);
            NetworkObjectInfo networkInfo = GetOrCreateNetworkObjectInfo(udonBehaviour.gameObject);
            var componentInfo = new NetworkComponentInfo
            {
                componentType = "UdonBehaviour",
                syncMode = "Unknown",
                syncedVariableCount = 0,
                syncedVariables = new List<string>(),
                programSourceAsset = GetUdonProgramSourceAsset(serializedObject, out string programSourcePath),
                programSourcePath = programSourcePath,
                componentInstance = udonBehaviour
            };

            // Use Unity serialized data for sync mode instead of heuristic property checks.
            try
            {
                if (TryGetUdonSyncMode(serializedObject, out string syncMode))
                {
                    componentInfo.syncMode = syncMode;
                    if (IsSyncMode(syncMode, "none"))
                    {
                        noneSyncBehaviours++;
                    }
                    else if (IsSyncMode(syncMode, "manual"))
                    {
                        manualSyncBehaviours++;
                    }
                    else if (IsSyncMode(syncMode, "continuous"))
                    {
                        continuousSyncBehaviours++;
                    }
                    else
                    {
                        unknownSyncBehaviours++;
                    }
                }
                else
                {
                    unknownSyncBehaviours++;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not fully analyze UdonBehaviour on {udonBehaviour.gameObject.name}: {e.Message}");
                componentInfo.syncMode = "Unknown";
                unknownSyncBehaviours++;
            }

            if (!TryPopulateUdonSharpMetadata(udonBehaviour, componentInfo))
            {
                TryPopulateGraphMetadata(serializedObject, componentInfo);
            }

            if (componentInfo.syncedVariableCount > 0)
            {
                totalSyncedVariables += componentInfo.syncedVariableCount;
            }

            networkInfo.components.Add(componentInfo);
        }

        private void AnalyzeVRCComponent(MonoBehaviour vrcComponent)
        {
            string componentType = vrcComponent.GetType().Name;
            
            if (!componentTypeCounts.ContainsKey(componentType))
            {
                componentTypeCounts[componentType] = 0;
            }
            componentTypeCounts[componentType]++;

            NetworkObjectInfo networkInfo = GetOrCreateNetworkObjectInfo(vrcComponent.gameObject);
            var componentInfo = new NetworkComponentInfo
            {
                componentType = componentType,
                syncMode = "VRC Managed",
                syncedVariableCount = 0,
                syncedVariables = new List<string>(),
                componentInstance = vrcComponent
            };

            // Some VRC components are known to sync
            if (componentType.Contains("Pickup") || 
                componentType.Contains("ObjectSync") ||
                componentType.Contains("PlayerAudio") ||
                componentType.Contains("Station"))
            {
                componentInfo.syncMode = "Built-in Sync";
            }
            else
            {
                vrcManagedSyncObjects++;
            }

            networkInfo.components.Add(componentInfo);
        }

        private float CalculateNetworkIntensity()
        {
            float estimatedKbps = EstimateBandwidthKbps();
            return Mathf.Clamp(estimatedKbps * 2f, 0f, 100f);
        }

        private float EstimateBandwidthKbps()
        {
            if (networkObjects == null || networkObjects.Count == 0)
            {
                return 0f;
            }

            float totalBytesPerSecond = 0f;
            foreach (var obj in networkObjects)
            {
                foreach (var component in obj.components)
                {
                    float rateHz = GetSyncRateHz(component);
                    int varCount = GetEstimatedSyncedVariableCount(component);
                    float bytesPerUpdate = BaseBytesPerUpdate + (varCount * BytesPerSyncedVar);
                    totalBytesPerSecond += rateHz * bytesPerUpdate;
                }
            }

            return totalBytesPerSecond / 1024f;
        }

        private float EstimateObjectBandwidthKbps(NetworkObjectInfo obj)
        {
            if (obj == null)
            {
                return 0f;
            }

            float totalBytesPerSecond = 0f;
            foreach (var component in obj.components)
            {
                float rateHz = GetSyncRateHz(component);
                int varCount = GetEstimatedSyncedVariableCount(component);
                float bytesPerUpdate = BaseBytesPerUpdate + (varCount * BytesPerSyncedVar);
                totalBytesPerSecond += rateHz * bytesPerUpdate;
            }

            return totalBytesPerSecond / 1024f;
        }

        private float EstimateObjectIntensityScore(NetworkObjectInfo obj)
        {
            float estimatedKbps = EstimateObjectBandwidthKbps(obj);
            return Mathf.Clamp(estimatedKbps * 2f, 0f, 100f);
        }

        private float EstimateComponentBandwidthKbps(NetworkComponentInfo component)
        {
            if (component == null)
            {
                return 0f;
            }

            float rateHz = GetSyncRateHz(component);
            int varCount = GetEstimatedSyncedVariableCount(component);
            float bytesPerUpdate = BaseBytesPerUpdate + (varCount * BytesPerSyncedVar);
            return (rateHz * bytesPerUpdate) / 1024f;
        }

        private static float GetSyncRateHz(NetworkComponentInfo component)
        {
            if (IsSyncMode(component.syncMode, "continuous"))
            {
                return ContinuousSyncRateHz;
            }

            if (IsSyncMode(component.syncMode, "manual"))
            {
                return ManualSyncRateHz;
            }

            if (string.Equals(component.syncMode, "Built-in Sync", StringComparison.OrdinalIgnoreCase))
            {
                return BuiltInSyncRateHz;
            }

            if (IsSyncMode(component.syncMode, "none"))
            {
                return 0f;
            }

            return ManagedSyncRateHz;
        }

        private static int GetEstimatedSyncedVariableCount(NetworkComponentInfo component)
        {
            if (component.syncedVariableCount > 0)
            {
                return component.syncedVariableCount;
            }

            if (component.componentType == "UdonBehaviour")
            {
                return 1;
            }

            if (string.Equals(component.syncMode, "Built-in Sync", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 1;
        }

        private string GetIntensityRating(float score)
        {
            if (score < 20) return "Very Low";
            if (score < 40) return "Low";
            if (score < 60) return "Moderate";
            if (score < 80) return "High";
            return "Very High";
        }

        private static void SelectObject(NetworkObjectInfo info)
        {
            if (info?.gameObject == null)
            {
                return;
            }

            Selection.activeGameObject = info.gameObject;
            EditorGUIUtility.PingObject(info.gameObject);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private List<ScriptSummary> BuildScriptSummaries()
        {
            Dictionary<string, ScriptSummary> summaries = new Dictionary<string, ScriptSummary>();

            foreach (var obj in networkObjects)
            {
                foreach (var component in obj.components)
                {
                    if (!IsComponentVisible(component))
                    {
                        continue;
                    }

                    string key = GetScriptSummaryKey(component);
                    if (!summaries.TryGetValue(key, out ScriptSummary summary))
                    {
                        summary = new ScriptSummary
                        {
                            key = key,
                            displayName = GetScriptDisplayName(component),
                            tooltip = component.programSourcePath ?? string.Empty,
                            instanceCount = 0,
                            bandwidthKbps = 0f,
                            components = new List<NetworkComponentInfo>()
                        };
                        summaries[key] = summary;
                    }

                    summary.instanceCount++;
                    summary.bandwidthKbps += EstimateComponentBandwidthKbps(component);
                    summary.components.Add(component);
                }
            }

            return summaries.Values
                .OrderByDescending(summary => summary.bandwidthKbps)
                .ToList();
        }

        private static string GetScriptSummaryKey(NetworkComponentInfo component)
        {
            if (component == null)
            {
                return "Unknown";
            }

            if (component.programSourceAsset != null)
            {
                return component.programSourceAsset.GetInstanceID().ToString();
            }

            if (!string.IsNullOrEmpty(component.programSourcePath))
            {
                return component.programSourcePath;
            }

            return component.componentType ?? "Unknown";
        }

        private static string GetScriptDisplayName(NetworkComponentInfo component)
        {
            if (component == null)
            {
                return "Unknown";
            }

            if (component.programSourceAsset != null)
            {
                return component.programSourceAsset.name;
            }

            if (!string.IsNullOrEmpty(component.programSourcePath))
            {
                return component.programSourcePath;
            }

            return component.componentType ?? "Unknown";
        }

        private bool CanAdjustSyncMode(ScriptSummary summary)
        {
            if (summary?.components == null)
            {
                return false;
            }

            foreach (var component in summary.components)
            {
                if (component == null || component.componentInstance == null)
                {
                    continue;
                }

                if (component.componentType == "UdonBehaviour")
                {
                    return true;
                }
            }

            return false;
        }

        private int GetOrCreateScriptSyncSelection(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return 0;
            }

            if (!scriptSyncSelections.TryGetValue(key, out int selection))
            {
                selection = 0;
                scriptSyncSelections[key] = selection;
            }

            return selection;
        }

        private void ApplySyncModeToSummary(ScriptSummary summary, string targetMode)
        {
            if (summary?.components == null)
            {
                return;
            }

            foreach (var component in summary.components)
            {
                if (component == null || component.componentInstance == null)
                {
                    continue;
                }

                if (component.componentType != "UdonBehaviour")
                {
                    continue;
                }

                TrySetUdonSyncMode(component.componentInstance, targetMode);
            }
        }

        private class ScriptSummary
        {
            public string key;
            public string displayName;
            public string tooltip;
            public int instanceCount;
            public float bandwidthKbps;
            public List<NetworkComponentInfo> components;
        }

        private NetworkObjectInfo GetOrCreateNetworkObjectInfo(GameObject gameObject)
        {
            int instanceId = gameObject.GetInstanceID();
            if (networkObjectsById.TryGetValue(instanceId, out NetworkObjectInfo existing))
            {
                return existing;
            }

            var networkInfo = new NetworkObjectInfo
            {
                objectName = gameObject.name,
                hierarchyPath = GetHierarchyPath(gameObject.transform),
                gameObject = gameObject
            };

            networkObjectsById[instanceId] = networkInfo;
            networkObjects.Add(networkInfo);
            return networkInfo;
        }

        private static bool TryGetUdonSyncMode(SerializedObject serializedObject, out string syncMode)
        {
            syncMode = "Unknown";
            SerializedProperty property = FindSerializedProperty(serializedObject, "syncMethod", "SyncMethod", "Synchronization", "synchronization");
            if (property == null)
            {
                return TryDiscoverUdonSyncMode(serializedObject, out syncMode);
            }

            if (property.propertyType == SerializedPropertyType.Enum &&
                property.enumDisplayNames != null &&
                property.enumDisplayNames.Length > property.enumValueIndex)
            {
                syncMode = property.enumDisplayNames[property.enumValueIndex];
                return true;
            }

            return false;
        }

        private static bool TryDiscoverUdonSyncMode(SerializedObject serializedObject, out string syncMode)
        {
            syncMode = "Unknown";
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyType != SerializedPropertyType.Enum)
                {
                    continue;
                }

                if (!iterator.name.ToLowerInvariant().Contains("sync"))
                {
                    continue;
                }

                string discovered = ExtractSyncModeFromEnum(iterator);
                if (!string.IsNullOrEmpty(discovered))
                {
                    syncMode = discovered;
                    return true;
                }
            }

            return false;
        }

        private static string ExtractSyncModeFromEnum(SerializedProperty property)
        {
            if (property.enumDisplayNames == null || property.enumDisplayNames.Length == 0)
            {
                return null;
            }

            if (property.enumValueIndex < 0 || property.enumValueIndex >= property.enumDisplayNames.Length)
            {
                return null;
            }

            string current = property.enumDisplayNames[property.enumValueIndex];
            if (IsSyncMode(current, "none") || IsSyncMode(current, "manual") || IsSyncMode(current, "continuous"))
            {
                return current;
            }

            return null;
        }

        private static bool IsSyncMode(string syncMode, string expected)
        {
            if (string.IsNullOrEmpty(syncMode))
            {
                return false;
            }

            return syncMode.ToLowerInvariant().Contains(expected);
        }

        private static bool TrySetUdonSyncMode(MonoBehaviour udonBehaviour, string targetMode)
        {
            if (udonBehaviour == null || string.IsNullOrEmpty(targetMode))
            {
                return false;
            }

            SerializedObject serializedObject = new SerializedObject(udonBehaviour);
            SerializedProperty property = FindSerializedProperty(serializedObject, "syncMethod", "SyncMethod", "Synchronization", "synchronization");
            if (property == null)
            {
                property = FindSyncModeProperty(serializedObject);
            }

            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                return false;
            }

            int targetIndex = FindEnumIndex(property, targetMode);
            if (targetIndex < 0)
            {
                return false;
            }

            Undo.RecordObject(udonBehaviour, "Set Udon Sync Mode");
            property.enumValueIndex = targetIndex;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(udonBehaviour);
            if (udonBehaviour.gameObject != null && udonBehaviour.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(udonBehaviour.gameObject.scene);
            }
            return true;
        }

        private bool IsComponentVisible(NetworkComponentInfo component)
        {
            if (component == null)
            {
                return false;
            }

            SyncModeFilter match = GetSyncModeFilter(component.syncMode);
            return (syncModeFilter & match) != 0;
        }

        private static SyncModeFilter GetSyncModeFilter(string syncMode)
        {
            if (IsSyncMode(syncMode, "continuous"))
            {
                return SyncModeFilter.Continuous;
            }

            if (IsSyncMode(syncMode, "manual"))
            {
                return SyncModeFilter.Manual;
            }

            if (IsSyncMode(syncMode, "none"))
            {
                return SyncModeFilter.SyncNone;
            }

            if (string.Equals(syncMode, "VRC Managed", StringComparison.OrdinalIgnoreCase))
            {
                return SyncModeFilter.VrcManaged;
            }

            if (string.Equals(syncMode, "Built-in Sync", StringComparison.OrdinalIgnoreCase))
            {
                return SyncModeFilter.BuiltIn;
            }

            return SyncModeFilter.Unknown;
        }

        private static SerializedProperty FindSerializedProperty(SerializedObject serializedObject, params string[] names)
        {
            foreach (string name in names)
            {
                SerializedProperty property = serializedObject.FindProperty(name);
                if (property != null)
                {
                    return property;
                }
            }

            return null;
        }

        private static int FindEnumIndex(SerializedProperty property, string targetMode)
        {
            if (property.enumDisplayNames == null || property.enumDisplayNames.Length == 0)
            {
                return -1;
            }

            for (int i = 0; i < property.enumDisplayNames.Length; i++)
            {
                string value = property.enumDisplayNames[i];
                if (string.Equals(value, targetMode, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }

                if (value != null && value.ToLowerInvariant().Contains(targetMode.ToLowerInvariant()))
                {
                    return i;
                }
            }

            return -1;
        }

        private static SerializedProperty FindSyncModeProperty(SerializedObject serializedObject)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyType != SerializedPropertyType.Enum)
                {
                    continue;
                }

                if (!iterator.name.ToLowerInvariant().Contains("sync"))
                {
                    continue;
                }

                if (HasSyncModeOptions(iterator))
                {
                    return iterator.Copy();
                }
            }

            return null;
        }

        private static bool HasSyncModeOptions(SerializedProperty property)
        {
            if (property.enumDisplayNames == null || property.enumDisplayNames.Length == 0)
            {
                return false;
            }

            bool hasNone = false;
            bool hasManual = false;
            bool hasContinuous = false;

            foreach (string name in property.enumDisplayNames)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                string lower = name.ToLowerInvariant();
                hasNone |= lower.Contains("none");
                hasManual |= lower.Contains("manual");
                hasContinuous |= lower.Contains("continuous");
            }

            return hasNone && hasManual && hasContinuous;
        }

        private static UnityEngine.Object GetUdonProgramSourceAsset(SerializedObject serializedObject, out string assetPath)
        {
            assetPath = string.Empty;
            SerializedProperty property = FindSerializedProperty(serializedObject, "programSource", "m_ProgramSource", "serializedProgramAsset", "m_SerializedProgramAsset");
            if (property == null)
            {
                return null;
            }

            UnityEngine.Object asset = property.objectReferenceValue;
            if (asset == null)
            {
                return null;
            }

            assetPath = AssetDatabase.GetAssetPath(asset);
            return asset;
        }

        private static MonoBehaviour[] GetAllSceneBehaviours()
        {
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(behaviour => behaviour != null)
                .Where(behaviour => behaviour.gameObject != null)
                .Where(behaviour => behaviour.gameObject.scene.IsValid())
                .Where(behaviour => behaviour.gameObject.scene.isLoaded)
                .Where(behaviour => (behaviour.hideFlags & HideFlags.NotEditable) == 0)
                .Where(behaviour => (behaviour.hideFlags & HideFlags.HideAndDontSave) == 0)
                .ToArray();
        }

        private static bool TryPopulateUdonSharpMetadata(MonoBehaviour udonBehaviour, NetworkComponentInfo componentInfo)
        {
            if (udonBehaviour == null || componentInfo == null)
            {
                return false;
            }

            Type udonSharpBehaviourType = FindTypeByName("UdonSharp.UdonSharpBehaviour");
            if (udonSharpBehaviourType == null)
            {
                return false;
            }

            MonoBehaviour proxy = null;
            foreach (var component in udonBehaviour.gameObject.GetComponents<MonoBehaviour>())
            {
                if (component != null && udonSharpBehaviourType.IsAssignableFrom(component.GetType()))
                {
                    proxy = component;
                    break;
                }
            }

            if (proxy == null)
            {
                return false;
            }

            Type syncedAttributeType = FindTypeByName("UdonSharp.UdonSyncedAttribute");
            if (syncedAttributeType == null)
            {
                return false;
            }

            FieldInfo[] fields = proxy.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            List<string> syncedVariables = new List<string>();
            foreach (FieldInfo field in fields)
            {
                if (!field.IsDefined(syncedAttributeType, true))
                {
                    continue;
                }

                string typeName = GetFriendlyTypeName(field.FieldType);
                syncedVariables.Add($"{field.Name} : {typeName}");
            }

            if (syncedVariables.Count == 0)
            {
                return false;
            }

            componentInfo.syncedVariableCount = syncedVariables.Count;
            componentInfo.syncedVariables = syncedVariables;
            return true;
        }

        private static bool TryPopulateGraphMetadata(SerializedObject udonBehaviourObject, NetworkComponentInfo componentInfo)
        {
            if (udonBehaviourObject == null || componentInfo == null)
            {
                return false;
            }

            SerializedProperty property = FindSerializedProperty(udonBehaviourObject, "syncedVariables", "syncedVariableNames", "syncedVariableTable");
            if (property == null || !property.isArray)
            {
                return false;
            }

            List<string> syncedVariables = new List<string>();
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                if (element != null && element.propertyType == SerializedPropertyType.String)
                {
                    syncedVariables.Add(element.stringValue);
                }
            }

            if (syncedVariables.Count == 0)
            {
                return false;
            }

            componentInfo.syncedVariableCount = syncedVariables.Count;
            componentInfo.syncedVariables = syncedVariables;
            return true;
        }

        private static Type FindTypeByName(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
            {
                return "Unknown";
            }

            if (type.IsGenericType)
            {
                string genericName = type.GetGenericTypeDefinition().Name;
                int tickIndex = genericName.IndexOf('`');
                if (tickIndex >= 0)
                {
                    genericName = genericName.Substring(0, tickIndex);
                }

                string[] args = type.GetGenericArguments().Select(GetFriendlyTypeName).ToArray();
                return $"{genericName}<{string.Join(", ", args)}>";
            }

            return type.Name;
        }
    }
}
