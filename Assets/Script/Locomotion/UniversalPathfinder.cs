using System.Collections.Generic;
using UnityEngine;


public enum PathfindingAlgo { AStar, Dijkstra }
public enum HeuristicType { Manhattan, Euclidean, Octile }


public class UniversalPathfinder : MonoBehaviour
{
    [Header("Algorithm Settings")]
    public PathfindingAlgo algorithm = PathfindingAlgo.AStar;
    public HeuristicType heuristicType = HeuristicType.Octile;
    
    [Header("Path Parameters")]
    public float heuristicWeight = 1.0f; // 權重
    public bool useTieBreaking = true;   // 是否打破平局
    public float maxJumpHeight = 0.5f;   // 容許的最大高低差 (例如 1.5 代表可以跳上小箱子)
    public float dropPenalty = 5.0f;     // 往下掉落的額外成本 (可選)

    [Header("Dynamic Obstacle Avoidance (Influence Map)")]
    public bool useDynamicObstacles = true;
    public float dynamicPenaltyRadius = 3.0f; // 預測障礙物的影響半徑
    public float maxDynamicPenalty = 15.0f;   // 核心區域的極大懲罰值


    // 取得啟發式代價 (H 值)
    private float GetHeuristic(GridNode current, GridNode goal, Vector3 startPos)
    {
        if (algorithm == PathfindingAlgo.Dijkstra)
            return 0f;

        // 計算 3D 距離差
        float dx = Mathf.Abs(current.worldPosition.x - goal.worldPosition.x);
        float dy = Mathf.Abs(current.worldPosition.y - goal.worldPosition.y); // 加入高度考量
        float dz = Mathf.Abs(current.worldPosition.z - goal.worldPosition.z); // Z 軸對應 Python 的 Y 軸

        float h = 0f;

        switch (heuristicType)
        {
            case HeuristicType.Euclidean:
                h = Vector3.Distance(current.worldPosition, goal.worldPosition);
                break;
            case HeuristicType.Manhattan:
                h = dx + dy + dz;
                break;
            case HeuristicType.Octile:
                h = Mathf.Max(dx, dz) + (Mathf.Sqrt(2) - 1) * Mathf.Min(dx, dz) + dy;
                break;
        }

        h *= heuristicWeight;

        // 平局打破 (Tie-breaking)
        if (useTieBreaking && h > 0)
        {
            float dx1 = current.worldPosition.x - goal.worldPosition.x;
            float dz1 = current.worldPosition.z - goal.worldPosition.z;
            float dx2 = startPos.x - goal.worldPosition.x;
            float dz2 = startPos.z - goal.worldPosition.z;

            float cross = Mathf.Abs(dx1 * dz2 - dx2 * dz1);
            h += cross * 0.001f;
        }

        // 加入高度差的額外成本 
        float heightDiff = current.worldPosition.y - goal.worldPosition.y;

        h += Mathf.Pow(heightDiff, 3); // 高度差過大，增加額外成本
      


        return h;
    }

    // 取得相鄰節點 (包含高低差檢測與防切西瓜)
    private List<KeyValuePair<GridNode, float>> GetNeighbors(GridMap3D map, GridNode node, List<Vector3> predictedObstacles)
    {
        var neighbors = new List<KeyValuePair<GridNode, float>>();
        
        // 8 方向與基本成本
        (int dx, int dz, float cost)[] directions = {
            (0, 1, 1f), (0, -1, 1f), (1, 0, 1f), (-1, 0, 1f),
            (1, 1, 1.414f), (-1, 1, 1.414f), (1, -1, 1.414f), (-1, -1, 1.414f)
        };

        foreach (var dir in directions)
        {
            int nx = node.gridX + dir.dx;
            int nz = node.gridZ + dir.dz; // Python 的 Y 對應 Unity 的 Z

            // 取得該 (X, Z) 座標上「所有層級」的節點
            List<GridNode> potentialNodes = map.GetNodesAt(nx, nz);
            if (potentialNodes == null) continue;

            foreach (GridNode neighborNode in potentialNodes)
            {
                if (!neighborNode.isWalkable) continue;

                // ✨ 核心邏輯：計算高低差
                float heightDiff = neighborNode.worldPosition.y - node.worldPosition.y;

                // 如果太高跳不上去，捨棄這個鄰居
                if (heightDiff > maxJumpHeight) continue;

                float finalCost = dir.cost;
                if (heightDiff > maxJumpHeight * 0.3f) 
                {
                    // 往上跳的路徑增加額外成本，讓 A* 優先選擇不跳躍的路徑（但仍然保留這條路徑以供必要時使用）
                    finalCost += 8.0f; 
                }
                else if (heightDiff < -maxJumpHeight) 
                {
                    // 摔太深可能會受傷，增加路徑成本讓 Agent 盡量不要跳崖
                    finalCost += dropPenalty; 
                }

                // ==========================================
                // ✨ 動態障礙物懲罰 (Influence Map)
                // ==========================================
                if (useDynamicObstacles && predictedObstacles != null && predictedObstacles.Count > 0)
                {
                    float dynamicPenalty = 0f;
                    foreach (Vector3 predPos in predictedObstacles)
                    {
                        float dist = Vector3.Distance(neighborNode.worldPosition, predPos);
                        if (dist < dynamicPenaltyRadius)
                        {
                            // 越靠近預測點，懲罰越高 (線性遞減)
                            dynamicPenalty += maxDynamicPenalty * (1.0f - (dist / dynamicPenaltyRadius));
                        }
                    }
                    finalCost += dynamicPenalty;
                }
                
                neighbors.Add(new KeyValuePair<GridNode, float>(neighborNode, finalCost));
            }
        }
        return neighbors;
    }

    // ==========================================
    // 專門給 GridMap3D 用的尋路演算法
    // ==========================================
    public List<Vector3> FindPath(GridMap3D map, Vector3 startWorldPos, Vector3 goalWorldPos, List<Vector3> predictedObstacles = null)
    {
        // 找到離起點和終點最近的有效網格節點
        GridNode startNode = map.GetClosestNode(startWorldPos);
        GridNode goalNode = map.GetClosestNode(goalWorldPos);

        if (startNode == null || goalNode == null){
            Debug.LogWarning($"找不到起點或終點的有效節點！Start: {startWorldPos}, Goal: {goalWorldPos}");
            return new List<Vector3>(); // 找不到起點或終點
        }

        // 初始化資料結構
        var openSet = new SimplePriorityQueue<GridNode>();
        var cameFrom = new Dictionary<GridNode, GridNode>();
        var gScore = new Dictionary<GridNode, float>();

        gScore[startNode] = 0f;
        openSet.Enqueue(startNode, 0f);

        while (openSet.Count > 0)
        {
            GridNode current = openSet.Dequeue();

            // 抵達終點！回溯路徑
            if (current == goalNode)
            {
                List<Vector3> path = new List<Vector3>();
                while (cameFrom.ContainsKey(current))
                {
                    path.Add(current.worldPosition);
                    current = cameFrom[current];
                }
                path.Add(startNode.worldPosition);
                path.Reverse(); // 反轉路徑
                return path;
            }

            // 展開鄰居
            foreach (var kvp in GetNeighbors(map, current, predictedObstacles))
            {
                GridNode neighbor = kvp.Key;
                float moveCost = kvp.Value;

                float tentativeG = gScore[current] + moveCost;

                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;

                    float h = GetHeuristic(neighbor, goalNode, startNode.worldPosition);
                    float f = tentativeG + h;

                    // 檢查是否已在佇列中並更新 (SimplePriorityQueue 會自動處理或允許重複)
                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        // 走投無路
        return new List<Vector3>();
    }

    // ==========================================
    // 專門給 WaypointGraph3D 用的尋路演算法
    // ==========================================
    public List<Vector3> FindPath(WaypointGraph3D graph, Vector3 startWorldPos, Vector3 goalWorldPos, List<Vector3> predictedObstacles = null)
    {
        // 1. 直達優化：如果起點到終點完全沒有障礙物，直接走直線！(Waypoint Graph 的強大優勢)
        // if (graph.CheckLineOfSight(startWorldPos, goalWorldPos))
        // {
        //     return new List<Vector3> { startWorldPos, goalWorldPos };
        // }

        // 2. 找到起終點的對應節點
        int startNode = graph.GetClosestNode(startWorldPos, maxJumpHeight);
        int goalNode = graph.GetClosestNode(goalWorldPos, maxJumpHeight);

        if (startNode == -1 || goalNode == -1)
            return new List<Vector3>();

        // 3. 初始化資料結構 (此時存的是 int 索引，而不是 GridNode)
        var openSet = new SimplePriorityQueue<int>();
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, float>();

        gScore[startNode] = 0f;
        openSet.Enqueue(startNode, 0f);

        while (openSet.Count > 0)
        {
            int current = openSet.Dequeue();

            // 4. 抵達終點！回溯路徑
            if (current == goalNode)
            {
                List<Vector3> path = new List<Vector3>();
                while (cameFrom.ContainsKey(current))
                {
                    path.Add(graph.GetNodePosition(current));
                    current = cameFrom[current];
                }
                path.Add(graph.GetNodePosition(startNode));
                path.Reverse();
                
                // Waypoint 圖特有的處理：把真實的起點與終點補上，確保精確抵達
                path.Insert(0, startWorldPos);
                path.Add(goalWorldPos);
                return path;
            }

            // 5. 展開鄰居 (呼叫我們剛剛在 Graph 中寫好的 GetNeighbors)
            foreach (var kvp in graph.GetNeighbors(current))
            {
                int neighbor = kvp.Key;
                float moveCost = kvp.Value;

                Vector3 currentPos = graph.GetNodePosition(current);
                Vector3 neighborPos = graph.GetNodePosition(neighbor);

                // ✨ 修正 3：在 Pathfinder 內部動態檢查高低差 (與 GridMap 行為完全統一)
                float heightDiff = neighborPos.y - currentPos.y;
                
                if (heightDiff > maxJumpHeight) continue; // 太高跳不上去，無視這條連線！
                if (heightDiff < -maxJumpHeight) moveCost += dropPenalty; // 往下掉給予額外成本懲罰

                // ==========================================
                // ✨ 動態障礙物懲罰 (Influence Map)
                // ==========================================
                if (useDynamicObstacles && predictedObstacles != null && predictedObstacles.Count > 0)
                {
                    float dynamicPenalty = 0f;
                    foreach (Vector3 predPos in predictedObstacles)
                    {
                        float dist = Vector3.Distance(neighborPos, predPos);
                        if (dist < dynamicPenaltyRadius)
                        {
                            dynamicPenalty += maxDynamicPenalty * (1.0f - (dist / dynamicPenaltyRadius));
                        }
                    }
                    moveCost += dynamicPenalty; // 疊加高昂的成本，讓 A* 嫌棄這條連線
                }


                float tentativeG = gScore[current] + moveCost;

                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;

                    // 計算 H 值
                    float h = (algorithm == PathfindingAlgo.Dijkstra) ? 0f : Vector3.Distance(neighborPos, graph.GetNodePosition(goalNode)) * heuristicWeight;
                    
                    float f = tentativeG + h;
                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        return new List<Vector3>(); // 走投無路
    }

}
