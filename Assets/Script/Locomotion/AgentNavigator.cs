using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Agent))]
[RequireComponent(typeof(UniversalPathfinder))]
public class AgentNavigator : MonoBehaviour
{
    private Agent agent;
    [HideInInspector] public UniversalPathfinder pathfinder;

    [Header("Pathfinding Maps")]
    public GridMap3D gridMap;
    public WaypointGraph3D waypointGraph;
    public bool useGridMap = true;

    [Header("Navigation State")]
    public bool isNavigating = true;
    public Vector3[] currentPath;
    public int currentWaypointIndex = 0;
    public float waypointThreshold = 1.0f;
    public float pathRecalculateInterval = 2.5f;
    private float pathRecalculateTimer = 0f;

    [Header("Follow Path Settings")]
    public FollowPathMode pathMode = FollowPathMode.LookAheadPredictive;
    public float predictTime = 0.5f;

    [Header("Steering Behaviors")]
    public ManagerType managerType = ManagerType.ArbitrationWithBlending;
    public float arriveTargetRadius = 0.5f;
    public float arriveSlowRadius = 8.0f;
    public float DynamicAvoidanceRadius = 5f;
    public LayerMask ObstacleLayers;
    public LayerMask DynamicObstacleLayers;

    private BaseBehaviorManager behaviorManager;

    void Awake()
    {
        agent = GetComponent<Agent>();
        pathfinder = GetComponent<UniversalPathfinder>();
        SetManager(managerType);
    }

    public void SetManager(ManagerType type)
    {
        managerType = type;
        switch (type)
        {
            case ManagerType.Arbitration:
                behaviorManager = new SteeringArbitrationManager(agent);
                break;
            case ManagerType.ArbitrationWithBlending:
                behaviorManager = new BlendingArbitrationManager(agent);
                break;
            case ManagerType.WeightDriven:
                behaviorManager = new WeightDrivenManager(agent);
                break;
        }
    }

    /// <summary>
    /// 重置導航狀態 (給 Agent 或 Brain 呼叫)
    /// </summary>
    public void ResetNavigation()
    {
        isNavigating = false;
        currentPath = null;
        currentWaypointIndex = 0;
        pathRecalculateTimer = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // 1. 如果大腦下令「發呆/等待」，強制煞車，不計算尋路
        if (agent.isWaiting || agent.currentState == AgentState.NONE)
        {
            // UnityEngine.Debug.Log("Waiting... slowing down... " + agent.brain.waitTimer);
            agent.steeringForce = Vector3.Lerp(agent.velocity, -agent.velocity / dt, dt * 2f);
            return;
        }
        // if (agent.isWaiting || agent.currentState == AgentState.NONE)
        // {
        //     agent.steeringForce = -agent.velocity / dt; // 強大反向力抵消速度
        //     return; // 提早結束，不進行後續尋路
        // }

        // 2. 解析目標的速度與距離
        Vector3 targetVelocity = Vector3.zero;
        Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 flatTarget = Vector3.zero;
        float distanceToTarget = Mathf.Infinity;

        if (agent.targetObject != null)
        {
            flatTarget = new Vector3(agent.targetObject.position.x, 0, agent.targetObject.position.z);
            distanceToTarget = Vector3.Distance(flatPos, flatTarget);

            Agent targetAgent = agent.targetObject.GetComponent<Agent>();
            if (targetAgent != null) targetVelocity = targetAgent.velocity;
            else if (agent.targetObject.GetComponent<Rigidbody>() != null)
                targetVelocity = agent.targetObject.GetComponent<Rigidbody>().linearVelocity;
        }

        // 3. 核心導航邏輯 (有路徑時)
        if (isNavigating && agent.targetObject != null && (gridMap != null || waypointGraph != null))
        {
            // 動態重新尋路
            pathRecalculateTimer -= dt;
            if (pathRecalculateTimer <= 0f)
            {
                List<Vector3> newPath = useGridMap 
                    ? pathfinder.FindPath(gridMap, transform.position, agent.targetObject.position)
                    : pathfinder.FindPath(waypointGraph, transform.position, agent.targetObject.position);

                if (newPath != null && newPath.Count > 0)
                {
                    currentPath = newPath.ToArray();
                    currentWaypointIndex = 0;
                }
                pathRecalculateTimer = pathRecalculateInterval;
            }

        }
        agent.steeringForce = behaviorManager.Compute(agent.targetObject, targetVelocity, dt);
        agent.steeringForce.y = 0;

        // 4. 強制抵達煞車 (Arrive)
        if (agent.HasState(AgentState.ARRIVE) && distanceToTarget < waypointThreshold)
        {
            agent.steeringForce = -agent.velocity / dt; // 強大反向力抵消速度
            UnityEngine.Debug.Log("Arrived at target, applying strong brake.");
        }
    }

    // 將畫路徑的工作也移交給 Navigator
    void OnDrawGizmos()
    {
        if (currentPath != null && currentPath.Length > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < currentPath.Length - 1; i++)
            {
                Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
            }

            if (currentWaypointIndex < currentPath.Length)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(currentPath[currentWaypointIndex], 0.5f);
            }
        }
    }
}