using System.Collections.Generic;
using UnityEngine;

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

    [ContextMenu("Find Path")]
    public void FindPath()
    {
        currentPath.Clear();

        if (graph == null)
        {
            Debug.LogError("[PathPlanningTester] Graph is null. Assign NaviManager / WaypointGraph3D.");
            return;
        }

        if (startPoint == null || goalPoint == null)
        {
            Debug.LogError("[PathPlanningTester] StartPoint or GoalPoint is null.");
            return;
        }

        if (rebuildGraphBeforeSearch)
        {
            Physics.SyncTransforms();
            graph.GenerateGraph();
        }

        if (graph.nodes == null || graph.nodes.Count == 0)
        {
            Debug.LogError("[PathPlanningTester] Graph has no nodes. Generate WaypointGraph3D first.");
            return;
        }

        int startNode = graph.GetClosestNode(startPoint.position, graph.maxJumpHeight);
        int goalNode = graph.GetClosestNode(goalPoint.position, graph.maxJumpHeight);

        if (startNode < 0 || goalNode < 0)
        {
            Debug.LogError($"[PathPlanningTester] Cannot find closest node. start={startNode}, goal={goalNode}");
            return;
        }

        List<int> nodePath = Search(startNode, goalNode);

        if (nodePath == null || nodePath.Count == 0)
        {
            Debug.LogWarning("[PathPlanningTester] No path found.");
            return;
        }

        foreach (int nodeIndex in nodePath)
        {
            currentPath.Add(graph.GetNodePosition(nodeIndex));
        }

        Debug.Log($"[PathPlanningTester] {searchMode} path found. Nodes = {currentPath.Count}");
    }

    private List<int> Search(int start, int goal)
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
                return ReconstructPath(cameFrom, current);
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

        return new List<int>();
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