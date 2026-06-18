using UnityEngine;
using UnityEngine.AI;

public class AgentController : MonoBehaviour
{
    private NavMeshAgent agent;

    void Start()
    {
        // 抓取角色身上的 NavMeshAgent 元件
        agent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        // 偵測滑鼠左鍵點擊
        // if (Input.GetMouseButtonDown(0))
        // {
        //     // 從攝影機朝著滑鼠點擊的螢幕位置發射一條射線
        //     Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        //     RaycastHit hit;

        //     // 如果射線打中任何有 Collider 的物件 (通常是地板)
        //     if (Physics.Raycast(ray, out hit))
        //     {
        //         // 核心 API：告訴 Agent 目的地座標，他就會自動算出路徑並走過去！
        //         agent.SetDestination(hit.point);
        //     }
        // }
        agent.SetDestination(new Vector3(0, 0, 0)); // 測試用：持續移動到原點
    }
}
