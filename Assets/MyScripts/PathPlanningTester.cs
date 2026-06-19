using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class PathPlanningTester : MonoBehaviour
{
    public enum SearchMode
    {
        Dijkstra,
        AStar
    }

    [Header("References")]
    public WaypointGraph3D graph;
    public Transform startPoint;
    public Transform goalPoint;

    [Header("Search")]
    public SearchMode searchMode = SearchMode.AStar;
    public bool rebuildGraphBeforeSearch = false;

    [Header("Debug")]
    public bool drawPath = true;
    public Color pathColor = Color.magenta;

    [HideInInspector]
    public List<Vector3> currentPath = new List<Vector3>();
    [HideInInspector]
    public string lastResultMessage = "Not tested yet.";
    [HideInInspector]
    public float lastPathCost = 0f;
    [HideInInspector]
    public int lastExpandedNodeCount = 0;

    [ContextMenu("Find Path")]
    public void FindPath()
    {
        currentPath.Clear();
        lastPathCost = 0f;
        lastExpandedNodeCount = 0;

        if (graph == null)
            AutoAssignGraph();

        if (graph == null)
        {
            ReportError("Graph is null. Assign NaviManager / WaypointGraph3D.");
            return;
        }

        if (startPoint == null || goalPoint == null)
        {
            ReportError("StartPoint or GoalPoint is null.");
            return;
        }

        if (rebuildGraphBeforeSearch)
        {
            Physics.SyncTransforms();
            graph.GenerateGraph();
        }

        if (graph.nodes == null || graph.nodes.Count == 0)
        {
            ReportError("Graph has no nodes. Generate WaypointGraph3D first.");
            return;
        }

        int startNode = graph.GetClosestNode(startPoint.position, graph.maxJumpHeight);
        int goalNode = graph.GetClosestNode(goalPoint.position, graph.maxJumpHeight);

        if (startNode < 0 || goalNode < 0)
        {
            ReportError($"Cannot find closest node. start={startNode}, goal={goalNode}");
            return;
        }

        PathSearchResult searchResult = Search(startNode, goalNode);

        if (searchResult.nodePath == null || searchResult.nodePath.Count == 0)
        {
            lastExpandedNodeCount = searchResult.expandedNodeCount;
            ReportWarning($"No path found. Expanded nodes = {lastExpandedNodeCount}");
            return;
        }

        foreach (int nodeIndex in searchResult.nodePath)
        {
            currentPath.Add(graph.GetNodePosition(nodeIndex));
        }

        lastPathCost = searchResult.totalCost;
        lastExpandedNodeCount = searchResult.expandedNodeCount;
        lastResultMessage = $"{searchMode} path found. Nodes = {currentPath.Count}, Cost = {lastPathCost:0.00}, Expanded = {lastExpandedNodeCount}";
        Debug.Log($"[PathPlanningTester] {lastResultMessage}");
    }

    [ContextMenu("Auto Assign Graph")]
    public void AutoAssignGraph()
    {
        if (graph != null)
            return;

        graph = Object.FindFirstObjectByType<WaypointGraph3D>();
    }

    [ContextMenu("Clear Path")]
    public void ClearPath()
    {
        currentPath.Clear();
        lastPathCost = 0f;
        lastExpandedNodeCount = 0;
        lastResultMessage = "Path cleared.";
    }

    private PathSearchResult Search(int start, int goal)
    {
        int n = graph.nodes.Count;

        float[] gScore = new float[n];
        float[] fScore = new float[n];
        int[] cameFrom = new int[n];
        bool[] closed = new bool[n];

        for (int i = 0; i < n; i++)
        {
            gScore[i] = float.PositiveInfinity;
            fScore[i] = float.PositiveInfinity;
            cameFrom[i] = -1;
            closed[i] = false;
        }

        List<int> open = new List<int>();

        gScore[start] = 0f;
        fScore[start] = Heuristic(start, goal);
        open.Add(start);

        while (open.Count > 0)
        {
            int current = GetLowestFScoreNode(open, fScore);

            if (current == goal)
            {
                return new PathSearchResult(
                    ReconstructPath(cameFrom, current),
                    gScore[current],
                    CountClosedNodes(closed)
                );
            }

            open.Remove(current);
            closed[current] = true;

            List<KeyValuePair<int, float>> neighbors = graph.GetNeighbors(current);

            foreach (KeyValuePair<int, float> edge in neighbors)
            {
                int neighbor = edge.Key;
                float edgeCost = edge.Value;

                if (closed[neighbor])
                    continue;

                float tentativeG = gScore[current] + edgeCost;

                if (tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);

                    if (!open.Contains(neighbor))
                        open.Add(neighbor);
                }
            }
        }

        return new PathSearchResult(new List<int>(), 0f, CountClosedNodes(closed));
    }

    private int CountClosedNodes(bool[] closed)
    {
        int count = 0;

        for (int i = 0; i < closed.Length; i++)
        {
            if (closed[i])
                count++;
        }

        return count;
    }

    private int GetLowestFScoreNode(List<int> open, float[] fScore)
    {
        int best = open[0];
        float bestScore = fScore[best];

        for (int i = 1; i < open.Count; i++)
        {
            int node = open[i];

            if (fScore[node] < bestScore)
            {
                best = node;
                bestScore = fScore[node];
            }
        }

        return best;
    }

    private float Heuristic(int a, int b)
    {
        if (searchMode == SearchMode.Dijkstra)
            return 0f;

        Vector3 pa = graph.GetNodePosition(a);
        Vector3 pb = graph.GetNodePosition(b);

        return Vector3.Distance(pa, pb);
    }

    private List<int> ReconstructPath(int[] cameFrom, int current)
    {
        List<int> path = new List<int>();
        path.Add(current);

        while (cameFrom[current] != -1)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private void ReportError(string message)
    {
        lastResultMessage = message;
        Debug.LogError($"[PathPlanningTester] {message}");
    }

    private void ReportWarning(string message)
    {
        lastResultMessage = message;
        Debug.LogWarning($"[PathPlanningTester] {message}");
    }

    private struct PathSearchResult
    {
        public readonly List<int> nodePath;
        public readonly float totalCost;
        public readonly int expandedNodeCount;

        public PathSearchResult(List<int> nodePath, float totalCost, int expandedNodeCount)
        {
            this.nodePath = nodePath;
            this.totalCost = totalCost;
            this.expandedNodeCount = expandedNodeCount;
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawPath || currentPath == null || currentPath.Count == 0)
            return;

        Gizmos.color = pathColor;

        for (int i = 0; i < currentPath.Count; i++)
        {
            Gizmos.DrawSphere(currentPath[i] + Vector3.up * 0.3f, 0.25f);

            if (i < currentPath.Count - 1)
            {
                Gizmos.DrawLine(
                    currentPath[i] + Vector3.up * 0.3f,
                    currentPath[i + 1] + Vector3.up * 0.3f
                );
            }
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(PathPlanningTester))]
public class PathPlanningTesterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PathPlanningTester tester = (PathPlanningTester)target;
        int pathNodeCount = tester.currentPath != null ? tester.currentPath.Count : 0;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Path Debug", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(tester.lastResultMessage, MessageType.Info);
        EditorGUILayout.LabelField("Current Path Nodes", pathNodeCount.ToString());
        EditorGUILayout.LabelField("Last Path Cost", tester.lastPathCost.ToString("0.00"));
        EditorGUILayout.LabelField("Expanded Nodes", tester.lastExpandedNodeCount.ToString());

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Auto Assign Graph"))
                RunTesterAction(tester, "Auto Assign Path Graph", t => t.AutoAssignGraph());

            if (GUILayout.Button("Find Path"))
                RunTesterAction(tester, "Find Debug Path", t => t.FindPath());
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rebuild Graph + Find"))
                RunTesterAction(tester, "Rebuild Graph And Find Path", RebuildGraphAndFindPath);

            if (GUILayout.Button("Clear Path"))
                RunTesterAction(tester, "Clear Debug Path", t => t.ClearPath());
        }
    }

    static void RebuildGraphAndFindPath(PathPlanningTester tester)
    {
        bool previousRebuildSetting = tester.rebuildGraphBeforeSearch;
        tester.rebuildGraphBeforeSearch = true;

        try
        {
            tester.FindPath();
        }
        finally
        {
            tester.rebuildGraphBeforeSearch = previousRebuildSetting;
        }
    }

    static void RunTesterAction(PathPlanningTester tester, string undoName, System.Action<PathPlanningTester> action)
    {
        Undo.RecordObject(tester, undoName);
        action(tester);
        EditorUtility.SetDirty(tester);
        SceneView.RepaintAll();
    }
}
#endif
