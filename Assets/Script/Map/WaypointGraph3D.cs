using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class WaypointGraph3D : MonoBehaviour
{

    [Header("Generation Mode")]
    public bool generateOnStart = false;

    [Header("Agent Settings")]
    public float agentRadius = 0.5f;
    public float agentHeight = 2.0f;     // Agent 的身高，用來算頭頂空間與視線高度
    public float maxJumpHeight = 1.5f;   // 容許的最大高低差連線

    [Header("Environment Settings")]
    public float scanHeight = 50f;
    public LayerMask walkableLayers;
    public LayerMask unwalkableLayers;
    public float maxConnectionDist = 20f; // 避免整張地圖的點互相連線，節省效能

    // ✨ 新增填充設定
    [Header("Space Filling")]
    public bool fillOpenSpace = true;
    public float fillSpacing = 5.0f; // 數值越小，空曠處的點越密 (建議 3~5)

    // 儲存所有節點與邊
    [HideInInspector] public List<Vector3> nodes = new List<Vector3>();
    
    // 儲存圖的連接關係，格式對應你 Python 的 self.edges: edges[node_index] = [(neighbor_idx, cost), ...]
    public Dictionary<int, List<KeyValuePair<int, float>>> edges = new Dictionary<int, List<KeyValuePair<int, float>>>();

    void Start()
    {
        if (generateOnStart)
        {
            GenerateGraph();
        }
    }
    
    [ContextMenu("Generate Waypoint Graph")]
    public void GenerateGraph()
    {
        nodes.Clear();
        edges.Clear();

        // 步驟 1：收集所有潛在的 XZ 角落座標
        HashSet<Vector2> xzPoints = new HashSet<Vector2>();
        Collider[] allColliders = FindObjectsOfType<Collider>();

        // 為了空曠填充，我們需要記錄整個可行走區域的最大邊界
        // Bounds sceneBounds = new Bounds(Vector3.zero, Vector3.zero);
        // bool hasBounds = false;

        foreach (Collider col in allColliders)
        {
            // 檢查該物件屬於哪個 Layer
            bool isWalkable = ((1 << col.gameObject.layer) & walkableLayers) != 0;
            bool isUnwalkable = ((1 << col.gameObject.layer) & unwalkableLayers) != 0;

            if (isWalkable || isUnwalkable)
            {
                Bounds b = col.bounds;


                // 擴充整體邊界紀錄
                // if (isWalkable)
                // {
                //     if (!hasBounds) { sceneBounds = b; hasBounds = true; }
                //     else { sceneBounds.Encapsulate(b); }
                // }

                // 【外擴角落】：供 Agent 在平地上繞過這個障礙物
                Bounds outer = b;
                outer.Expand(new Vector3(agentRadius * 3, 0, agentRadius * 3));
                xzPoints.Add(new Vector2(outer.min.x, outer.min.z));
                xzPoints.Add(new Vector2(outer.min.x, outer.max.z));
                xzPoints.Add(new Vector2(outer.max.x, outer.min.z));
                xzPoints.Add(new Vector2(outer.max.x, outer.max.z));

                // 【內縮角落】：如果這個物件是「可站立」的，我們需要在它頂部內側也建立路徑點
                if (isWalkable)
                {
                    Bounds inner = b;
                    // 往內縮一點，確保生成的點踩得穩，不會剛好卡在邊緣掉下去
                    inner.Expand(new Vector3(-agentRadius * 3f, 0, -agentRadius * 3f));
                    if (inner.size.x > 0 && inner.size.z > 0)
                    {
                        xzPoints.Add(new Vector2(inner.min.x, inner.min.z));
                        xzPoints.Add(new Vector2(inner.min.x, inner.max.z));
                        xzPoints.Add(new Vector2(inner.max.x, inner.min.z));
                        xzPoints.Add(new Vector2(inner.max.x, inner.max.z));
                    }
                }
            }
        }

        // 空曠區域均勻填充 (Sparse Grid Fill)
        // if (fillOpenSpace && hasBounds && fillSpacing > 0)
        // {
        //     // 以設定的間距，在整個場景邊界內均勻撒點
        //     for (float x = sceneBounds.min.x; x <= sceneBounds.max.x; x += fillSpacing)
        //     {
        //         for (float z = sceneBounds.min.z; z <= sceneBounds.max.z; z += fillSpacing)
        //         {
        //             xzPoints.Add(new Vector2(x, z));
        //         }
        //     }
        // }

        // 步驟 2：從天空打射線，建立 3D 多層次路徑點 (套用 GridMap 的過濾技術)
        foreach (Vector2 xz in xzPoints)
        {
            Vector3 rayStart = new Vector3(xz.x, scanHeight, xz.y);
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, scanHeight + 50f, walkableLayers);
            
            // 從高到低排序
            hits = hits.OrderByDescending(h => h.point.y).ToArray();

            foreach (RaycastHit hit in hits)
            {
                Vector3 hitPoint = hit.point;
                bool isValidNode = true;
                // 只有在walkableLayers上打到的點才有資格成為節點
                if (((1 << hit.collider.gameObject.layer) & unwalkableLayers) != 0)
                {
                    isValidNode = false;
                    continue; // 這個點不是可行走的表面，跳過
                }

                // 檢查該點上方是否有足夠的頭部空間，並排除自己踩著的物件
                Vector3 checkCenter = hitPoint + Vector3.up * (agentHeight * 0.5f);
                Vector3 halfExtents = new Vector3(agentRadius * 0.8f, agentHeight * 0.5f, agentRadius * 0.8f);
                Collider[] overlappingColliders = Physics.OverlapBox(checkCenter, halfExtents, Quaternion.identity, unwalkableLayers);

                foreach (Collider col in overlappingColliders)
                {
                    if (col != hit.collider) // 排除掉自己踩著的那塊木箱/地板
                    {
                        isValidNode = false;
                        break;
                    }
                }

                if (isValidNode)
                {
                    // 防止加入距離太近的重複點
                    bool isDuplicate = false;
                    foreach (Vector3 existingNode in nodes)
                    {
                        if (Vector3.Distance(existingNode, hitPoint) < 0.2f)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }

                    if (!isDuplicate)
                    {
                        nodes.Add(hitPoint);
                    }
                }
            }
        }

        // 步驟 3：檢查兩兩節點的視線，建立 Edge 連線
        BuildEdges();
        Debug.Log($"Waypoint Graph 生成完畢！共生成 {nodes.Count} 個節點。");
    }

    private void BuildEdges()
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            edges[i] = new List<KeyValuePair<int, float>>();
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            for (int j = i + 1; j < nodes.Count; j++)
            {
                Vector3 n1 = nodes[i];
                Vector3 n2 = nodes[j];

                float dist = Vector3.Distance(n1, n2);

                // 條件 A：距離太遠的不連線，節省 A* 搜尋時間
                if (dist > maxConnectionDist) continue;

                // 條件 B：高低差太大（跳不上去）的不連線
                if (Mathf.Abs(n1.y - n2.y) > maxJumpHeight) continue;
                
                // ✨ 條件 B.2：如果是往上、往掉的連線，可以考慮增加額外成本，讓 A* 優先選擇不掉落的路徑（但仍然保留這條路徑以供必要時使用）
                if (Mathf.Abs(n1.y - n2.y) > maxJumpHeight * 0.3f) dist += 4.0f; // 往下掉的路徑增加 200% 的成本懲罰



                // 條件 C：視線沒有被擋住 (Line of Sight)
                if (CheckLineOfSight(n1, n2))
                {
                    // 雙向圖連線
                    edges[i].Add(new KeyValuePair<int, float>(j, dist));
                    edges[j].Add(new KeyValuePair<int, float>(i, dist));
                }
            }
        }
    }

    /// <summary>
    /// 具有「體積」的 3D 視線檢查
    /// </summary>
    public bool CheckLineOfSight(Vector3 p1, Vector3 p2)
    {
        // 為了避免射線在地上摩擦，我們將起終點抬高到 Agent 身體中心 (腰部/胸口)
        Vector3 start = p1 + Vector3.up * (agentHeight * 0.5f);
        Vector3 end = p2 + Vector3.up * (agentHeight * 0.5f);
        Vector3 dir = end - start;
        float distance = dir.magnitude;

        // 使用 SphereCast (球體投射)，半徑設為 AgentRadius 的 80% (保留一點容錯)
        // 只要這顆球在飛行的途中沒有撞到 unwalkableLayers，就代表這條路走得通
        return !Physics.SphereCast(start, agentRadius * 0.8f, dir.normalized, out RaycastHit hit, distance, unwalkableLayers);
    }

    /// <summary>
    /// 取得距離給定世界座標最近的節點 Index (並優先確保視線無阻擋)
    /// </summary>
    public int GetClosestNode(Vector3 worldPos, float maxJumpHeight)
    {
        int bestIndex = -1;
        float minSqDist = float.MaxValue;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (Mathf.Abs(nodes[i].y - worldPos.y) > maxJumpHeight) continue; // 先過濾掉高低差太大的點
            // 使用 sqrMagnitude 效能更好 (不需要開根號)
            float sqDist = (worldPos - nodes[i]).sqrMagnitude;

            if (sqDist < minSqDist)
            {
                // 核心防呆：確認從當前位置「看得到」該節點，避免隔著牆壁選到背後的點
                if (CheckLineOfSight(worldPos, nodes[i]))
                {
                    minSqDist = sqDist;
                    bestIndex = i;
                }
            }
        }

        // 如果真的找不到可視的點 (可能角色剛好卡在奇怪的死角或牆壁內)
        // 退而求其次，找絕對距離最近的點
        if (bestIndex == -1)
        {
            minSqDist = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (Mathf.Abs(nodes[i].y - worldPos.y) > maxJumpHeight) continue; // 仍然過濾高低差太大的點
                
                float sqDist = (worldPos - nodes[i]).sqrMagnitude;
                if (sqDist < minSqDist)
                {
                    minSqDist = sqDist;
                    bestIndex = i;
                }
            }
        }

        return bestIndex;
    }


    /// <summary>
    /// 取得某個節點的真實世界座標
    /// </summary>
    public Vector3 GetNodePosition(int nodeIndex)
    {
        if (nodeIndex >= 0 && nodeIndex < nodes.Count)
        {
            return nodes[nodeIndex];
        }
        return Vector3.zero; // 錯誤處理
    }

    /// <summary>
    /// 取得相鄰節點與移動成本 (相當於 GridMap 的 GetNeighbors)
    /// </summary>
    public List<KeyValuePair<int, float>> GetNeighbors(int nodeIndex)
    {
        if (edges.ContainsKey(nodeIndex))
        {
            return edges[nodeIndex];
        }
        return new List<KeyValuePair<int, float>>();
    }

    /// <summary>
    // 編輯器視覺化
    /// </summary>
    void OnDrawGizmos()
    {
        if (nodes == null || nodes.Count == 0) return;

        // 畫出節點
        Gizmos.color = new Color(1, 0, 0, 0.8f);
        foreach (Vector3 node in nodes)
        {
            Gizmos.DrawSphere(node, 0.2f);
        }

        // 畫出連線
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        foreach (var kvp in edges)
        {
            Vector3 start = nodes[kvp.Key];
            foreach (var neighbor in kvp.Value)
            {
                Vector3 end = nodes[neighbor.Key];
                Gizmos.DrawLine(start, end);
            }
        }
    }
}