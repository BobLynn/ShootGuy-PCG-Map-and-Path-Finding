using UnityEngine;

public class GridMap : MonoBehaviour
{
    public float width = 170f;
    public float length = 170f; // 對應原本的 height
    public float cellSize = 1f;
    public LayerMask obstacleLayer; // 在 Inspector 中設定哪些圖層算障礙物

    private int[,] grid;
    private int cols, rows;

    void Start()
    {
        GenerateGrid();
    }

    void GenerateGrid()
    {
        cols = Mathf.FloorToInt(width / cellSize) /2;
        rows = Mathf.FloorToInt(length / cellSize) /2;
        grid = new int[cols, rows];

        // 碰撞盒的一半大小 (BoxCenter 的參數需求)
        Vector3 halfExtents = new Vector3(cellSize / 2, 2f, cellSize / 2);

        for (int x = -cols/2; x < cols/2; x++)
        {
            for (int z = -rows/2; z < rows/2; z++)
            {
                // 計算每一格的 3D 中心點 (假設平面在 Y = 0)
                Vector3 center = new Vector3(x * cellSize + cellSize / 2, 0, z * cellSize + cellSize / 2);
                
                // 檢查這個格子有沒有碰到障礙物圖層
                bool isHit = Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleLayer);
                
                grid[x + cols/2, z + rows/2] = isHit ? 1 : 0;
            }
        }
        Debug.Log("Grid Map 生成完畢！");
    }

    // 類似你 Python 裡的 draw()，能在 Unity Scene 視窗直接畫出網格
    void OnDrawGizmos()
    {
        if (grid == null) return;

        for (int x = -cols/2; x < cols/2; x++)
        {
            for (int z = -rows/2; z < rows/2; z++)
            {
                Vector3 center = new Vector3(x * cellSize + cellSize / 2, 0, z * cellSize + cellSize / 2);
                Vector3 size = new Vector3(cellSize, 0.1f, cellSize);

                if (grid[x+ cols/2, z+ rows/2] == 1)
                {
                    Gizmos.color = new Color(1, 0, 0, 0.5f); // 障礙物畫半透明紅色
                    Gizmos.DrawCube(center, size);
                }
                else
                {
                    Gizmos.color = new Color(0.8f, 0.8f, 0.8f, 0.3f); // 可行走路徑畫淡灰色
                    Gizmos.DrawWireCube(center, size);
                }
            }
        }
    }
}