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

    [Header("Stuck Recovery")]
    public float stuckSpeedThreshold = 0.15f;
    public float stuckProgressEpsilon = 0.2f;
    public float stuckTimeBeforeAnchorAdvance = 1.0f;
    private float stuckTimer = 0f;
    private float lastDistanceToAnchor = Mathf.Infinity;
    private Vector3 lastRecoveryPosition;

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
    private bool warnedMissingNavigationRefs = false;

    void Awake()
    {
        agent = GetComponent<Agent>();
        pathfinder = GetComponent<UniversalPathfinder>();
        SetManager(managerType);
        lastRecoveryPosition = transform.position;
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
        if (!CanUpdateNavigation())
            return;

        // 1. 如果大腦下令「發呆/等待」，強制煞車，不計算尋路
        if (agent.isWaiting || agent.currentState == AgentState.NONE)
        {
            // UnityEngine.Debug.Log("Waiting... slowing down... " + agent.brain.waitTimer);
            ApplyBrake(dt, true);
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

            if (agent.targetObject.TryGetComponent(out Agent targetAgent))
                targetVelocity = targetAgent.velocity;
            else if (agent.targetObject.TryGetComponent(out Rigidbody targetRigidbody))
                targetVelocity = targetRigidbody.linearVelocity;
        }

        // 3. 核心導航邏輯 (有路徑時)
        if (isNavigating && agent.targetObject != null && (gridMap != null || waypointGraph != null))
        {
            // 動態重新尋路
            pathRecalculateTimer -= dt;
            if (pathRecalculateTimer <= 0f)
            {
                List<Vector3> newPath = null;

                if (useGridMap && gridMap != null)
                {
                    newPath = pathfinder.FindPath(gridMap, transform.position, agent.targetObject.position);
                }
                else if (waypointGraph != null)
                {
                    newPath = pathfinder.FindPath(waypointGraph, transform.position, agent.targetObject.position);
                }

                if (newPath != null && newPath.Count > 0)
                {
                    currentPath = newPath.ToArray();
                    currentWaypointIndex = FindBestPathAnchorIndex(currentPath);
                    ResetStuckTracking();
                }
                else if (currentPath == null || currentPath.Length == 0)
                {
                    currentPath = null;
                    currentWaypointIndex = 0;
                    ResetStuckTracking();
                }

                pathRecalculateTimer = pathRecalculateInterval;
            }

        }
        agent.steeringForce = behaviorManager.Compute(agent.targetObject, targetVelocity, dt);
        agent.steeringForce.y = 0;

        UpdateStuckRecovery(dt);

        // 4. 強制抵達煞車 (Arrive)
        if (agent.HasState(AgentState.ARRIVE) && distanceToTarget < waypointThreshold)
        {
            ApplyBrake(dt, false);
            LogDebug("Arrived at target, applying strong brake.");
        }
    }

    private bool CanUpdateNavigation()
    {
        if (agent != null && pathfinder != null && behaviorManager != null)
            return true;

        if (!warnedMissingNavigationRefs)
        {
            warnedMissingNavigationRefs = true;
            Debug.LogWarning($"[AgentNavigator] {name} is missing Agent, UniversalPathfinder, or BehaviorManager; navigation update is disabled.");
        }

        return false;
    }

    private void ApplyBrake(float dt, bool smoothBrake)
    {
        if (dt <= 0f)
        {
            agent.steeringForce = Vector3.zero;
            return;
        }

        Vector3 brakeForce = -agent.velocity / dt;
        agent.steeringForce = smoothBrake
            ? Vector3.Lerp(agent.velocity, brakeForce, dt * 2f)
            : brakeForce;
    }

    private void LogDebug(string message)
    {
        if (agent == null || !agent.showDebugLogs)
            return;

        Debug.Log($"[AgentNavigator] {message}");
    }

    private int FindBestPathAnchorIndex(Vector3[] path)
    {
        if (path == null || path.Length == 0)
            return 0;

        Vector3 currentPosition = transform.position;
        int closestVisibleIndex = -1;
        float closestVisibleDistance = Mathf.Infinity;
        int closestIndex = 0;
        float closestDistance = Mathf.Infinity;

        for (int i = 0; i < path.Length; i++)
        {
            Vector3 flatDelta = new Vector3(path[i].x - currentPosition.x, 0f, path[i].z - currentPosition.z);
            float sqrDistance = flatDelta.sqrMagnitude;

            if (sqrDistance < closestDistance)
            {
                closestDistance = sqrDistance;
                closestIndex = i;
            }

            if (!HasLineOfSightToPathPoint(path[i]))
                continue;

            if (sqrDistance < closestVisibleDistance)
            {
                closestVisibleDistance = sqrDistance;
                closestVisibleIndex = i;
            }
        }

        return closestVisibleIndex >= 0 ? closestVisibleIndex : closestIndex;
    }

    private void UpdateStuckRecovery(float dt)
    {
        if (!isNavigating || currentPath == null || currentPath.Length == 0 || agent.targetObject == null)
        {
            ResetStuckTracking();
            return;
        }

        if (currentWaypointIndex < 0 || currentWaypointIndex >= currentPath.Length)
        {
            currentWaypointIndex = FindBestPathAnchorIndex(currentPath);
            ResetStuckTracking();
            return;
        }

        Vector3 anchor = currentPath[currentWaypointIndex];
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatAnchor = new Vector3(anchor.x, 0f, anchor.z);
        float distanceToAnchor = Vector3.Distance(flatPos, flatAnchor);
        Vector3 flatLastPos = new Vector3(lastRecoveryPosition.x, 0f, lastRecoveryPosition.z);
        float actualDisplacement = Vector3.Distance(flatPos, flatLastPos);
        float actualSpeed = dt > 0f ? actualDisplacement / dt : 0f;
        bool madeProgress = distanceToAnchor < lastDistanceToAnchor - stuckProgressEpsilon;
        bool anchorVisible = HasLineOfSightToPathPoint(anchor);

        if (!anchorVisible && TryAdvanceToVisibleAnchor())
        {
            ResetStuckTracking();
            LogDebug($"Advanced blocked path anchor to index {currentWaypointIndex}.");
            return;
        }
        else if (!anchorVisible)
        {
            pathRecalculateTimer = 0f;
            ResetStuckTracking();
            LogDebug("Requested path recalculation because current anchor is blocked.");
            return;
        }

        if (actualSpeed <= stuckSpeedThreshold && !madeProgress && distanceToAnchor > waypointThreshold)
            stuckTimer += dt;
        else
            stuckTimer = 0f;

        lastDistanceToAnchor = distanceToAnchor;
        lastRecoveryPosition = transform.position;

        if (stuckTimer < stuckTimeBeforeAnchorAdvance)
            return;

        if (TryAdvanceToVisibleAnchor())
        {
            LogDebug($"Advanced stuck path anchor to index {currentWaypointIndex}.");
        }
        else
        {
            pathRecalculateTimer = 0f;
            LogDebug("Requested path recalculation after stuck recovery could not find a visible anchor.");
        }

        ResetStuckTracking();
    }

    private bool TryAdvanceToVisibleAnchor()
    {
        if (currentPath == null || currentPath.Length == 0)
            return false;

        for (int i = currentWaypointIndex + 1; i < currentPath.Length; i++)
        {
            if (HasLineOfSightToPathPoint(currentPath[i]))
            {
                currentWaypointIndex = i;
                return true;
            }
        }

        if (currentWaypointIndex < currentPath.Length - 1)
        {
            currentWaypointIndex++;
            return true;
        }

        return false;
    }

    private bool HasLineOfSightToPathPoint(Vector3 point)
    {
        Vector3 rayStart = transform.position + Vector3.up * 1.0f;
        Vector3 rayEnd = point + Vector3.up * 1.0f;
        return !Physics.Linecast(rayStart, rayEnd, ObstacleLayers);
    }

    private void ResetStuckTracking()
    {
        stuckTimer = 0f;
        lastDistanceToAnchor = Mathf.Infinity;
        lastRecoveryPosition = transform.position;
    }

    // 將畫路徑的工作也移交給 Navigator
    void OnDrawGizmos()
    {
        if (agent == null)
            agent = GetComponent<Agent>();

        if (agent != null && !agent.showDebugGizmos)
            return;

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
