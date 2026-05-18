using UnityEngine;
using System.Collections.Generic;

// --- 資料結構 ---

[System.Serializable]
public class WaypointInfo
{
    public Transform point;          // 座標點
    public float waitTime = 0f;      // 到達後要停留多久
    public string actionAnim = "";   // 到達後要執行的動作 (例如 "Smoke", "LookAround")
}

[System.Serializable]
public class RouteDefinition
{
    public string routeName;         // 路線名稱，例如 "Balcony_Patrol" 或 "Escape_Route"
    public bool isLoop = true;       // 是否循環
    public List<WaypointInfo> waypoints = new List<WaypointInfo>();
}

// --- 管理器本體 ---

public class RouteManager : MonoBehaviour
{
    public static RouteManager Instance { get; private set; }

    [Header("All Game Routes")]
    [Tooltip("在這裡統一設定遊戲中所有的巡邏與逃跑路線")]
    public List<RouteDefinition> allRoutes = new List<RouteDefinition>();

    // 為了讓 Agent 能用 O(1) 的超快速度用名字查到路線，我們用 Dictionary
    private Dictionary<string, RouteDefinition> routeDictionary = new Dictionary<string, RouteDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this);
        else Instance = this;

        // 初始化字典
        foreach (var route in allRoutes)
        {
            if (!routeDictionary.ContainsKey(route.routeName))
            {
                routeDictionary.Add(route.routeName, route);
            }
        }
    }

    /// <summary>
    /// Agent 用這個方法來索取路線資料
    /// </summary>
    public RouteDefinition GetRoute(string name)
    {
        if (routeDictionary.ContainsKey(name))
            return routeDictionary[name];
        
        Debug.LogWarning($"找不到路線：{name}");
        return null;
    }

    // 視覺化：讓關卡設計師在編輯器裡看到所有的路線網
    private void OnDrawGizmos()
    {
        foreach (var route in allRoutes)
        {
            if (route.waypoints == null || route.waypoints.Count < 2) continue;

            // 隨機給每條路線一個顏色方便區分
            Random.InitState(route.routeName.GetHashCode());
            Gizmos.color = new Color(Random.value, Random.value, Random.value, 0.8f);

            for (int i = 0; i < route.waypoints.Count; i++)
            {
                if (route.waypoints[i].point == null) continue;
                Gizmos.DrawSphere(route.waypoints[i].point.position, 0.3f);

                if (i < route.waypoints.Count - 1 && route.waypoints[i+1].point != null)
                {
                    Gizmos.DrawLine(route.waypoints[i].point.position, route.waypoints[i + 1].point.position);
                }
                else if (route.isLoop && route.waypoints[0].point != null)
                {
                    Gizmos.DrawLine(route.waypoints[i].point.position, route.waypoints[0].point.position);
                }
            }
        }
    }
}