using UnityEngine;

// 定義管理員種類與狀態
public enum ManagerType { Arbitration, ArbitrationWithBlending, WeightDriven }

public abstract class BaseBehaviorManager
{
    protected Agent agent;
    protected AgentLocomotion locomotion => agent.locomotion;
    protected AgentNavigator navigator => agent.navigator;
    private bool warnedMissingSteeringRefs = false;

    public BaseBehaviorManager(Agent agent) { this.agent = agent; }
    public abstract Vector3 Compute(Transform target, Vector3 targetVelocity, float dt);
    
    protected Vector3 Limit(Vector3 vector, float maxVal)
    {
        if (vector.sqrMagnitude > maxVal * maxVal) return vector.normalized * maxVal;
        return vector;
    }

    // 計算目標導向力 (共用邏輯)
    private int lastEvaluatedIndex = -1; // 記錄上次評估是哪一格
    private int lastEvaluatedPathHash = 0;
    protected Vector3 ComputeGoalSteering(Transform target, Vector3 targetVelocity)
    {
        if (!CanComputeSteering())
            return Vector3.zero;

        Vector3 targetPos = target != null ? target.position : agent.transform.position;
        Vector3 goalSteering = Vector3.zero;

        if (navigator.isNavigating)
        {
            if (navigator.currentPath == null || navigator.currentPath.Length == 0)
            {
                if (target == null)
                {
                    if (agent.velocity.sqrMagnitude > 0.01f && Time.deltaTime > 0f)
                        return -agent.velocity / Time.deltaTime;

                    return Vector3.zero;
                }

                if (agent.HasState(AgentState.PURSUE))
                    return Behaviors.Pursue(agent.transform.position, targetPos, targetVelocity, agent.velocity, agent.maxSpeed);

                if (agent.HasState(AgentState.ARRIVE))
                    return Behaviors.Arrive(agent.transform.position, targetPos, agent.velocity, agent.maxSpeed, agent.navigator.arriveTargetRadius, agent.navigator.arriveSlowRadius);

                return Behaviors.Seek(agent.transform.position, targetPos, agent.velocity, agent.maxSpeed);
            }

            if (navigator.currentWaypointIndex < 0 || navigator.currentWaypointIndex >= navigator.currentPath.Length)
                navigator.currentWaypointIndex = 0;

            // 如果正在導航，則使用 FollowPath 行為
            int currentPathHash = navigator.currentPath.GetHashCode();
            if (navigator.currentWaypointIndex != lastEvaluatedIndex || currentPathHash != lastEvaluatedPathHash)
            {
                FollowPathMode dynamicMode = Behaviors.EvaluatePathComplexity(agent.transform.position, navigator.currentPath, navigator.currentWaypointIndex,navigator.ObstacleLayers, locomotion.JumpHeight);
                navigator.pathMode = dynamicMode; // 更新 Agent 的 pathMode 屬性
                lastEvaluatedIndex = navigator.currentWaypointIndex; // 更新最後評估的 waypoint index
                lastEvaluatedPathHash = currentPathHash;
                LogDebug($"切換到節點 {navigator.currentWaypointIndex}，使用模式: {dynamicMode}");
            }

            goalSteering = Behaviors.FollowPath(
                agent.transform.position, 
                agent.velocity, 
                navigator.currentPath, 
                ref navigator.currentWaypointIndex, 
                agent.maxSpeed, 
                navigator.waypointThreshold, 
                locomotion.JumpHeight,
                navigator.ObstacleLayers, 
                agent,
                navigator.pathMode,
                navigator.predictTime
            );
            return goalSteering;
        }

        // 使用當前多重狀態計算目標導向力
        if (agent.HasState(AgentState.PURSUE))
        {
            goalSteering = Behaviors.Pursue(agent.transform.position, targetPos, targetVelocity, agent.velocity, agent.maxSpeed);
        }
        if (agent.HasState(AgentState.SEEK))
        {
            goalSteering = Behaviors.Seek(agent.transform.position, targetPos, agent.velocity, agent.maxSpeed);
        }
        if (agent.HasState(AgentState.ARRIVE))
        {
            goalSteering = Behaviors.Arrive(agent.transform.position, targetPos, agent.velocity, agent.maxSpeed, agent.navigator.arriveTargetRadius, agent.navigator.arriveSlowRadius);
        }
        if (agent.HasState(AgentState.FLEE))
        {
            goalSteering += Behaviors.Flee(agent.transform.position, targetPos, agent.velocity, agent.maxSpeed);
        }
        
        return goalSteering;
    }

    protected bool CanComputeSteering()
    {
        if (agent != null && agent.navigator != null && agent.locomotion != null)
            return true;

        if (!warnedMissingSteeringRefs)
        {
            warnedMissingSteeringRefs = true;
            string agentName = agent != null ? agent.name : "Unknown Agent";
            Debug.LogWarning($"[BehaviorManager] {agentName} is missing AgentNavigator or AgentLocomotion; steering output defaults to zero.");
        }

        return false;
    }

    protected void LogDebug(string message)
    {
        if (agent == null || !agent.showDebugLogs)
            return;

        Debug.Log($"[BehaviorManager] {message}");
    }
}

// 1. 絕對仲裁 (Strict Arbitration) - 避障優先，若觸發避障則完全忽略目標
public class SteeringArbitrationManager : BaseBehaviorManager
{
    public SteeringArbitrationManager(Agent agent) : base(agent) {}

    public override Vector3 Compute(Transform target, Vector3 targetVelocity, float dt)
    {
        if (!CanComputeSteering())
            return Vector3.zero;

        float epsilon = 0.1f;
        
        // 優先級 1：避障
        Vector3 avoidForce = Behaviors.RaycastAvoidance(agent.transform, agent.velocity, agent.maxSpeed, navigator.ObstacleLayers, drawDebug: agent.showDebugGizmos);
        if (avoidForce.magnitude > epsilon)
        {
            return Limit(avoidForce, agent.maxForce);
        }
        Vector3 dynamicAvoidForce = Behaviors.DynamicAvoidance(agent.transform, agent.maxSpeed, navigator.DynamicAvoidanceRadius, navigator.DynamicObstacleLayers);
        if (dynamicAvoidForce.magnitude > epsilon)       
        {
            return Limit(dynamicAvoidForce, agent.maxForce);
        }

        // 優先級 3：目標導向
        Vector3 goalSteering = ComputeGoalSteering(target, targetVelocity);
        if (goalSteering.magnitude > epsilon)
        {
            return Limit(goalSteering, agent.maxForce);
        }

        return Vector3.zero;
    }
}

// 2. 混合仲裁 (Arbitration with Blending) - 避障為主，但會混入少量目標導向力避免卡死
public class BlendingArbitrationManager : BaseBehaviorManager
{
    public BlendingArbitrationManager(Agent agent) : base(agent) {}

    public override Vector3 Compute(Transform target, Vector3 targetVelocity, float dt)
    {
        if (!CanComputeSteering())
            return Vector3.zero;

        float epsilon = 0.1f;
        Vector3 goalSteering = ComputeGoalSteering(target, targetVelocity);
        Vector3 avoidForce = Behaviors.RaycastAvoidance(agent.transform, agent.velocity, agent.maxSpeed, navigator.ObstacleLayers, drawDebug: agent.showDebugGizmos);
        Vector3 dynamicAvoidForce = Behaviors.DynamicAvoidance(agent.transform, agent.maxSpeed, navigator.DynamicAvoidanceRadius, navigator.DynamicObstacleLayers);

        if (avoidForce.magnitude > epsilon)
        {
            // 將避障與目標導向力混合 (30% 目標導向力)
            Vector3 blendedSteering = avoidForce + dynamicAvoidForce + goalSteering * 0.5f;
            return Limit(blendedSteering, agent.maxForce);
        }

        return Limit(goalSteering, agent.maxForce);
    }
}

// 3. 權重驅動 (Weight Driven) - 同時計算所有行為並依照權重加總
public class WeightDrivenManager : BaseBehaviorManager
{
    public WeightDrivenManager(Agent agent) : base(agent) {}

    public override Vector3 Compute(Transform target, Vector3 targetVelocity, float dt)
    {
        if (!CanComputeSteering())
            return Vector3.zero;

        Vector3 steering = Vector3.zero;

        // 計算避障 (動態權重：此處簡化為固定高權重，可另行擴充 WeightAdapter)
        float avoidWeight = 2.0f;
        Vector3 avoidForce = Behaviors.RaycastAvoidance(agent.transform, agent.velocity, agent.maxSpeed, navigator.ObstacleLayers, drawDebug: agent.showDebugGizmos);
        steering += avoidForce * avoidWeight;

        float dynamicAvoidWeight = 1.5f;
        Vector3 dynamicAvoidForce = Behaviors.DynamicAvoidance(agent.transform, agent.maxSpeed, navigator.DynamicAvoidanceRadius, navigator.DynamicObstacleLayers);
        steering += dynamicAvoidForce * dynamicAvoidWeight;

        // 計算目標導向
        float goalWeight = 1.0f;
        Vector3 goalSteering = ComputeGoalSteering(target, targetVelocity);
        steering += goalSteering * goalWeight;

        // // 如果大腦同時開啟了 FLEE 狀態，疊加直接遠離玩家的驚慌加速力
        // if (agent.HasState(AgentState.FLEE) && agent.threatObject != null)
        // {
        //     float fleeWeight = 1.5f; // 給予較高權重，產生背對玩家時的衝刺加速感
        //     Vector3 fleeForce = Behaviors.Flee(agent.transform.position, agent.threatObject.position, agent.velocity, agent.maxSpeed);
        //     steering += fleeForce * fleeWeight;
        // }

        return Limit(steering, agent.maxForce);
    }
}
