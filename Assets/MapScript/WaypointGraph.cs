using System.Collections.Generic;
using UnityEngine;

public class WaypointGraph : MonoBehaviour
{
    public float agentRadius = 0.5f;
    public LayerMask obstacleLayer;
    
    private List<Vector3> nodes = new List<Vector3>();

    void Start()
    {
        GenerateNodes();
    }

    void GenerateNodes()
    {
        nodes.Clear();
        
        // 抓取場景中所有的碰撞體 (這裡簡化處理，實務上可用 OverlapBox 抓特定區域)
        Collider[] allColliders = FindObjectsOfType<Collider>();

        foreach (Collider col in allColliders)
        {
            // 如果這個物件屬於障礙物圖層
            if (((1 << col.gameObject.layer) & obstacleLayer) != 0)
            {
                Bounds bounds = col.bounds;
                
                // 往外膨脹，對應你的 inflate(agent_radius * 2)
                // 注意：這裡直接擴張 Bounds，長寬高都會變大
                bounds.Expand(agentRadius * 2);

                // 抓取底部的四個角落 (固定 Y 軸高度為 0)
                float fixedY = 0f;
                nodes.Add(new Vector3(bounds.min.x, fixedY, bounds.min.z)); // 左下
                nodes.Add(new Vector3(bounds.min.x, fixedY, bounds.max.z)); // 左上
                nodes.Add(new Vector3(bounds.max.x, fixedY, bounds.min.z)); // 右下
                nodes.Add(new Vector3(bounds.max.x, fixedY, bounds.max.z)); // 右上
            }
        }
        
        // TODO: 這裡可以加入過濾邏輯，例如剔除掉落在其他障礙物內部的節點
    }

    // 視線檢查 (Line of Sight)
    public bool CheckLineOfSight(Vector3 p1, Vector3 p2)
    {
        // Linecast 會從 p1 打一條線到 p2，如果撞到 obstacleLayer 就回傳 true
        return !Physics.Linecast(p1, p2, obstacleLayer);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        foreach (Vector3 node in nodes)
        {
            Gizmos.DrawSphere(node, 0.2f); // 將生成的路徑點畫成紅色小圓球
        }
    }
}