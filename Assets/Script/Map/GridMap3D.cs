using System.Collections.Generic;
using UnityEngine;
using System.Linq; // 需要這個來做排序

// 定義網格中的單一節點
public class GridNode
{
    public bool isWalkable;
    public Vector3 worldPosition; // 記錄這個格子「表面」的真實 3D 座標
    public int gridX;
    public int gridZ;

    public GridNode(bool _isWalkable, Vector3 _worldPos, int _gridX, int _gridZ)
    {
        isWalkable = _isWalkable;
        worldPosition = _worldPos;
        gridX = _gridX;
        gridZ = _gridZ;
    }
}

public class GridMap3D : MonoBehaviour
{
    [Header("Grid Settings")]
    public Vector3 gridCenter = Vector3.zero;
    public float width = 50f;
    public float length = 50f;
    public float cellSize = 1f;

    [Header("Generation Mode")]
    public bool generateOnStart = false;

    [Header("Debug Display")]
    public bool showGizmos = true;

    [Header("Height Settings")]
    public float scanHeight = 50f; // 從多高的地方往下掃描 (要比場景中最高的物件還高)
    public LayerMask walkableLayer; // 哪些圖層是可以踩在上面的 (例如 Ground, Box)
    public LayerMask unwalkableLayer; // 哪些圖層是純障礙物 (例如牆壁、水坑)

    private List<GridNode>[,] grid;
    private int cols, rows;
    
    void Start()
    {
        if (generateOnStart)
        {
            GenerateGrid();
        }
    }

    [ContextMenu("Generate Grid Map")]
    public void GenerateGrid()
    {
        cols = Mathf.FloorToInt(width / cellSize);
        rows = Mathf.FloorToInt(length / cellSize);
        grid = new List<GridNode>[cols, rows];
        
        int halfCols = cols / 2;
        int halfRows = rows / 2;

        for (int x = -halfCols; x < halfCols; x++)
        {
            for (int z = -halfRows; z < halfRows; z++)
            {
                int arrayX = x + halfCols;
                int arrayZ = z + halfRows;

                grid[arrayX, arrayZ] = new List<GridNode>();
                Vector3 rayStart = new Vector3(
                    gridCenter.x + x * cellSize + cellSize / 2,
                    scanHeight,
                    gridCenter.z + z * cellSize + cellSize / 2
                );

                RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, scanHeight + 50f, walkableLayer);
                hits = hits.OrderByDescending(h => h.point.y).ToArray();

                foreach (RaycastHit hit in hits)
                {
                    Vector3 hitPoint = hit.point;
                    bool walkable = true;

                    Vector3 checkCenter = hitPoint + Vector3.up * (cellSize * 0.5f);
                    Vector3 halfExtents = new Vector3(cellSize * 0.4f, cellSize * 0.4f, cellSize * 0.4f);
                    
                    Collider[] overlappingColliders = Physics.OverlapBox(checkCenter, halfExtents, Quaternion.identity, unwalkableLayer);

                    foreach (Collider col in overlappingColliders)
                    {
                        if (col != hit.collider)
                        {
                            walkable = false;
                            break; 
                        }
                    }

                    grid[arrayX, arrayZ].Add(new GridNode(walkable, hitPoint, x, z));
                }

                if (grid[arrayX, arrayZ].Count == 0)
                {
                    grid[arrayX, arrayZ].Add(new GridNode(false, new Vector3(rayStart.x, 0, rayStart.z), x, z));
                }
            }
        }
        Debug.Log("多層 3D Grid Map 生成完畢！");
    }

    public List<GridNode> GetNodesAt(int gridX, int gridZ)
    {
        int arrayX = gridX + (cols / 2);
        int arrayZ = gridZ + (rows / 2);

        if (arrayX >= 0 && arrayX < cols && arrayZ >= 0 && arrayZ < rows)
        {
            return grid[arrayX, arrayZ];
        }
        return null;
    }

    // =======================================================
    // ✨ 核心升級：具備「防呆錨定」的 GetClosestNode
    // =======================================================
    /// <param name="worldPos">目標座標</param>
    /// <param name="maxSearchRadius">如果該點不可走，最大允許往外尋找幾格 (預設 15 格)</param>
    public GridNode GetClosestNode(Vector3 worldPos, int maxSearchRadius = 15)
    {
        int centerGridX = Mathf.RoundToInt((worldPos.x - gridCenter.x) / cellSize);
        int centerGridZ = Mathf.RoundToInt((worldPos.z - gridCenter.z) / cellSize);

        // 1. 優先檢查目標所在的「中心格子」
        List<GridNode> nodesAtPos = GetNodesAt(centerGridX, centerGridZ);
        if (nodesAtPos != null && nodesAtPos.Count > 0)
        {
            GridNode closestNode = null;
            float minHeightDiff = float.MaxValue;

            foreach (var node in nodesAtPos)
            {
                if (!node.isWalkable) continue;
                float heightDiff = Mathf.Abs(node.worldPosition.y - worldPos.y);
                if (heightDiff < minHeightDiff)
                {
                    minHeightDiff = heightDiff;
                    closestNode = node;
                }
            }
            // 如果中心格有合法的地板，直接回傳 (最快路徑)
            if (closestNode != null) return closestNode;
        }

        // 2. 如果中心格完全沒有可行走的節點 (例如落在死心牆壁內、水坑上)
        // ✨ 開始「同心圓 (Square Ring)」向外擴展搜尋
        for (int radius = 1; radius <= maxSearchRadius; radius++)
        {
            GridNode bestNode = null;
            float minDistance = float.MaxValue;

            // 走訪當前半徑 (radius) 的正方形邊緣
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    // 略過內部已經檢查過的格子，只檢查「最外圈」
                    if (Mathf.Abs(x) != radius && Mathf.Abs(z) != radius) continue;

                    int checkX = centerGridX + x;
                    int checkZ = centerGridZ + z;

                    List<GridNode> checkNodes = GetNodesAt(checkX, checkZ);
                    if (checkNodes == null) continue;

                    foreach (var node in checkNodes)
                    {
                        if (!node.isWalkable) continue;

                        // 這裡使用 3D 直線距離，確保找到的替代點離玩家預期的目標點「絕對最近」
                        float dist = Vector3.Distance(node.worldPosition, worldPos);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            bestNode = node;
                        }
                    }
                }
            }

            // 只要在「這一圈」有找到任何一個合法的節點，就立刻回傳最好的那個
            // (不需要再往下一圈找，因為下一圈一定更遠)
            if (bestNode != null)
            {
                // 取消註解這行可以幫你 Debug 到底偏移了多遠
                // Debug.Log($"[GridMap3D] 座標 {worldPos} 落入禁區，已自動錨定到 {radius} 格外的合法節點！");
                return bestNode;
            }
        }

        // 3. 找了 15 圈都找不到，代表這目標點真的在世界的盡頭
        return null;
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (grid == null) return;
        int halfCols = cols / 2;
        int halfRows = rows / 2;

        for (int x = -halfCols; x < halfCols; x++)
        {
            for (int z = -halfRows; z < halfRows; z++)
            {
                int arrayX = x + halfCols;
                int arrayZ = z + halfRows;
                List<GridNode> nodesAtThisXZ = grid[arrayX, arrayZ];
                foreach (GridNode node in nodesAtThisXZ)
                {
                    Vector3 size = new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f);
                    if (node.isWalkable)
                    {
                        Gizmos.color = new Color(0, 1, 0, 0.4f); 
                        Gizmos.DrawCube(node.worldPosition, size);
                    }
                    else
                    {
                        Gizmos.color = new Color(1, 0, 0, 0.4f); 
                        Gizmos.DrawCube(node.worldPosition, size);
                    }
                }
            }
        }
    }
}