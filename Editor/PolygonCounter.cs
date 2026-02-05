using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace DoggoBrutus.WorldDev.Editor
{
    public class PolygonCounter : EditorWindow
    {
        private int totalTriangles = 0;
        private int totalVertices = 0;
        private int meshFilterCount = 0;
        private int skinnedMeshCount = 0;
        private Vector2 scrollPosition;
        private List<MeshInfo> meshInfoList = new List<MeshInfo>();
        private Dictionary<Mesh, MeshMetrics> meshMetricsDict = new Dictionary<Mesh, MeshMetrics>();
        private bool viewByMesh = false;

        private class MeshInfo
        {
            public string objectName;
            public string hierarchyPath;
            public int triangles;
            public int vertices;
            public string meshType;
            public GameObject gameObject;
        }

        private class MeshMetrics
        {
            public Mesh mesh;
            public string meshName;
            public int triangles;
            public int vertices;
            public int usageCount = 0;
            public int totalTrianglesUsed => triangles * usageCount;
            public int totalVerticesUsed => vertices * usageCount;
            public List<GameObject> usedByObjects = new List<GameObject>();
        }

        [MenuItem("Tools/DB's worlddev-tools/Polygon Counter")]
        public static void ShowWindow()
        {
            PolygonCounter window = GetWindow<PolygonCounter>("Polygon Counter");
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Scene Polygon Statistics", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Calculate Total Polygons", GUILayout.Height(30)))
            {
                CalculatePolygons();
            }

            EditorGUILayout.Space();

            // Toggle between view modes
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("View Mode:", GUILayout.Width(80));
            bool newViewByMesh = GUILayout.Toggle(viewByMesh, "By Mesh", GUILayout.Width(80));
            if (newViewByMesh != viewByMesh)
            {
                viewByMesh = newViewByMesh;
            }
            GUILayout.Toggle(!viewByMesh, "By Object", GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (totalTriangles > 0 || totalVertices > 0)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Total Triangles (Polygons):", totalTriangles.ToString("N0"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Total Vertices:", totalVertices.ToString("N0"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("MeshFilters:", meshFilterCount.ToString());
                EditorGUILayout.LabelField("SkinnedMeshRenderers:", skinnedMeshCount.ToString());
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                if (viewByMesh && meshMetricsDict.Count > 0)
                {
                    DrawMeshMetricsView();
                }
                else if (!viewByMesh && meshInfoList.Count > 0)
                {
                    DrawObjectBreakdownView();
                }
            }
        }

        private void DrawMeshMetricsView()
        {
            EditorGUILayout.LabelField("Mesh Usage Analysis:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Sorted by total impact (triangles per mesh × usage count)", MessageType.Info);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));

            var sortedMeshes = meshMetricsDict.Values.OrderByDescending(m => m.totalTrianglesUsed);

            foreach (var metrics in sortedMeshes)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(metrics.meshName, EditorStyles.boldLabel, GUILayout.Width(250));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Per Instance: {metrics.triangles:N0} tris | {metrics.vertices:N0} verts", GUILayout.Width(250));
                EditorGUILayout.LabelField($"Used: {metrics.usageCount}x", GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Total Impact: {metrics.totalTrianglesUsed:N0} tris | {metrics.totalVerticesUsed:N0} verts", EditorStyles.boldLabel, GUILayout.Width(250));
                EditorGUILayout.LabelField($"({(metrics.totalTrianglesUsed / (float)totalTriangles * 100):F1}% of scene)", GUILayout.Width(150));
                EditorGUILayout.EndHorizontal();

                // Show objects using this mesh
                if (metrics.usedByObjects.Count > 0 && metrics.usageCount <= 10)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Used by:", GUILayout.Width(100));
                    EditorGUILayout.LabelField(string.Join(", ", metrics.usedByObjects.Select(o => o.name)), GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawObjectBreakdownView()
        {
            EditorGUILayout.LabelField("Detailed Breakdown:", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));

            foreach (var info in meshInfoList.OrderByDescending(m => m.triangles))
            {
                EditorGUILayout.BeginHorizontal("box");
                if (GUILayout.Button(new GUIContent(info.objectName, info.hierarchyPath), GUILayout.Width(200)))
                {
                    SelectObject(info);
                }
                EditorGUILayout.LabelField($"[{info.meshType}]", GUILayout.Width(100));
                EditorGUILayout.LabelField($"Tris: {info.triangles:N0}", GUILayout.Width(100));
                EditorGUILayout.LabelField($"Verts: {info.vertices:N0}", GUILayout.Width(100));
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    SelectObject(info);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void CalculatePolygons()
        {
            totalTriangles = 0;
            totalVertices = 0;
            meshFilterCount = 0;
            skinnedMeshCount = 0;
            meshInfoList.Clear();
            meshMetricsDict.Clear();

            // Find all MeshFilters in the scene
            MeshFilter[] meshFilters = FindObjectsOfType<MeshFilter>();
            meshFilterCount = meshFilters.Length;

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh != null)
                {
                    Mesh mesh = meshFilter.sharedMesh;
                    int triangles = mesh.triangles.Length / 3;
                    int vertices = mesh.vertexCount;

                    totalTriangles += triangles;
                    totalVertices += vertices;

                    meshInfoList.Add(new MeshInfo
                    {
                        objectName = meshFilter.gameObject.name,
                        hierarchyPath = GetHierarchyPath(meshFilter.transform),
                        triangles = triangles,
                        vertices = vertices,
                        meshType = "MeshFilter",
                        gameObject = meshFilter.gameObject
                    });

                    // Track mesh metrics
                    TrackMeshMetrics(mesh, meshFilter.gameObject, triangles, vertices);
                }
            }

            // Find all SkinnedMeshRenderers in the scene
            SkinnedMeshRenderer[] skinnedMeshRenderers = FindObjectsOfType<SkinnedMeshRenderer>();
            skinnedMeshCount = skinnedMeshRenderers.Length;

            foreach (SkinnedMeshRenderer smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh != null)
                {
                    Mesh mesh = smr.sharedMesh;
                    int triangles = mesh.triangles.Length / 3;
                    int vertices = mesh.vertexCount;

                    totalTriangles += triangles;
                    totalVertices += vertices;

                    meshInfoList.Add(new MeshInfo
                    {
                        objectName = smr.gameObject.name,
                        hierarchyPath = GetHierarchyPath(smr.transform),
                        triangles = triangles,
                        vertices = vertices,
                        meshType = "SkinnedMesh",
                        gameObject = smr.gameObject
                    });

                    // Track mesh metrics
                    TrackMeshMetrics(mesh, smr.gameObject, triangles, vertices);
                }
            }

            Debug.Log($"[Polygon Counter] Total Triangles: {totalTriangles:N0} | Total Vertices: {totalVertices:N0} | Unique Meshes: {meshMetricsDict.Count}");
            Repaint();
        }

        private void TrackMeshMetrics(Mesh mesh, GameObject gameObject, int triangles, int vertices)
        {
            if (meshMetricsDict.ContainsKey(mesh))
            {
                meshMetricsDict[mesh].usageCount++;
                if (!meshMetricsDict[mesh].usedByObjects.Contains(gameObject))
                {
                    meshMetricsDict[mesh].usedByObjects.Add(gameObject);
                }
            }
            else
            {
                meshMetricsDict[mesh] = new MeshMetrics
                {
                    mesh = mesh,
                    meshName = mesh.name,
                    triangles = triangles,
                    vertices = vertices,
                    usageCount = 1,
                    usedByObjects = new List<GameObject> { gameObject }
                };
            }
        }

        private static void SelectObject(MeshInfo info)
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
    }
}
