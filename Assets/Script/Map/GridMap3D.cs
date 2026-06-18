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

    // private GameObject hitObject;
    
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
                // 計算射線的起點 (從天上往下)
                // Vector3 rayStart = new Vector3(x * cellSize + cellSize / 2, scanHeight, z * cellSize + cellSize / 2);
                Vector3 rayStart = new Vector3(
                    gridCenter.x + x * cellSize + cellSize / 2,
                    scanHeight,
                    gridCenter.z + z * cellSize + cellSize / 2
                );

                
                // ✨ 核心改變：使用 RaycastAll 一次貫穿所有圖層
                RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, scanHeight + 50f, walkableLayer);

                // 因為 RaycastAll 回傳的陣列是沒有順序的，我們依照 Y 軸高度從高到低排序
                hits = hits.OrderByDescending(h => h.point.y).ToArray();

                // 遍歷每一個打到的表面 (一樓、二樓、三樓...)
                foreach (RaycastHit hit in hits)
                {
                    Vector3 hitPoint = hit.point;
                    bool walkable = true;

                    // ✨ 核心改變：改用 OverlapBox 來取得具體撞到的碰撞體
                    Vector3 checkCenter = hitPoint + Vector3.up * (cellSize * 0.5f);
                    Vector3 halfExtents = new Vector3(cellSize * 0.4f, cellSize * 0.4f, cellSize * 0.4f);
                    
                    // 找出這個節點上方所有屬於 unwalkableLayer 的碰撞體
                    Collider[] overlappingColliders = Physics.OverlapBox(checkCenter, halfExtents, Quaternion.identity, unwalkableLayer);

                    foreach (Collider col in overlappingColliders)
                    {
                        // 如果上方撞到的不可行走物件，「不是」我們腳底下踩著的這個物件
                        // 代表這格的頭頂真的被別的障礙物 (或是另一個箱子) 擋住了
                        if (col != hit.collider)
                        {
                            walkable = false;
                            break; // 只要有一個東西擋住，這格就不能走，直接跳出檢查
                        }
                    }

                    // 創建節點並加入到這個 (x, z) 專屬的 List 裡面
                    grid[arrayX, arrayZ].Add(new GridNode(walkable, hitPoint, x, z));
                }

                // 如果這個座標從頭到尾什麼都沒打到，給他一個預設的底層節點
                if (grid[arrayX, arrayZ].Count == 0)
                {
                    grid[arrayX, arrayZ].Add(new GridNode(false, new Vector3(rayStart.x, 0, rayStart.z), x, z));
                }
            }
        }
        Debug.Log("多層 3D Grid Map 生成完畢！");
    }
    // 取得某個格子的所有層級(輸入是座標索引，例如 (0,0) 代表中心格子)
    public List<GridNode> GetNodesAt(int gridX, int gridZ)
    {
        // 需要把世界座標的網格索引轉換為陣列的索引 (處理負數)
        int arrayX = gridX + (cols / 2);
        int arrayZ = gridZ + (rows / 2);

        if (arrayX >= 0 && arrayX < cols && arrayZ >= 0 && arrayZ < rows)
        {
            return grid[arrayX, arrayZ];
        }
        return null;
    }

    // 找尋離給定世界座標最近且可行走的節點
    public GridNode GetClosestNode(Vector3 worldPos)
    {
        int gridX = Mathf.RoundToInt((worldPos.x - gridCenter.x) / cellSize);
        int gridZ = Mathf.RoundToInt((worldPos.z - gridCenter.z) / cellSize);

        List<GridNode> nodesAtPos = GetNodesAt(gridX, gridZ);
        if (nodesAtPos == null || nodesAtPos.Count == 0) return null;

        GridNode closestNode = null;
        float minHeightDiff = float.MaxValue;

        // 找同一格中，高度與指定座標最接近的那個節點
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
        return closestNode;
    }

    // 視覺化：在編輯器中畫出高低起伏的網格
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
                        Gizmos.color = new Color(0, 1, 0, 0.4f); // 可行走：半透明綠色
                        Gizmos.DrawCube(node.worldPosition, size);
                    }
                    else
                    {
                        Gizmos.color = new Color(1, 0, 0, 0.4f); // 不可行走：半透明紅色
                        Gizmos.DrawCube(node.worldPosition, size);
                    }
                }
            }
        }
    }
}
