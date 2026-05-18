using UnityEngine;

public enum FollowPathMode { Strict, LookAhead, LookAheadPredictive }
public static class Behaviors
{
    public static Vector3 Seek(Vector3 currentPos, Vector3 targetPos, Vector3 currentVelocity, float maxSpeed)
    {
        // 【修正】只取水平方向的目標
        Vector3 direction = new Vector3(targetPos.x, currentPos.y, targetPos.z) - currentPos;
        
        if (direction.sqrMagnitude > 0.01f)
        {
            Vector3 desired = direction.normalized * maxSpeed;
            return desired - currentVelocity;
        }
        return Vector3.zero;
    }

    public static Vector3 Pursue(Vector3 currentPos, Vector3 targetPos, Vector3 targetVelocity, Vector3 currentVelocity, float maxSpeed)
    {
        float distance = Vector3.Distance(new Vector3(currentPos.x, 0, currentPos.z), 
                                        new Vector3(targetPos.x, 0, targetPos.z));
        float lookAheadTime = distance / maxSpeed;
        
        // 預測位置也需要考慮高度對齊
        Vector3 futurePos = targetPos + targetVelocity * lookAheadTime;
        return Seek(currentPos, futurePos, currentVelocity, maxSpeed);
    }

    //抵達 (Arrive)
    public static Vector3 Arrive(Vector3 currentPos, Vector3 targetPos, Vector3 currentVelocity, float maxSpeed, float targetRadius = 1.5f, float slowRadius = 15f)
    {
        // 【修正】將目標點高度對齊目前位置，消除垂直干擾
        Vector3 flatTarget = new Vector3(targetPos.x, currentPos.y, targetPos.z);
        Vector3 direction = flatTarget - currentPos;
        float distance = direction.magnitude;

        if (distance < 0.01f) return -currentVelocity / Time.deltaTime;

        // 進入停止區（只判斷水平距離）
        if (distance < targetRadius)
        {
            return -currentVelocity / Time.deltaTime; 
        }

        float desiredSpeed = maxSpeed;
        if (distance < slowRadius)
        {
            desiredSpeed = maxSpeed * (distance / slowRadius);
        }

        Vector3 desiredVelocity = direction.normalized * desiredSpeed;
        return (desiredVelocity - currentVelocity) / 0.1f;
    }
    public static Vector3 Flee(Vector3 currentPos, Vector3 targetPos, Vector3 currentVelocity, float maxSpeed)
    {
        // 取得從目標指向自己的向量 (與 Seek 相反)
        Vector3 direction = currentPos - new Vector3(targetPos.x, currentPos.y, targetPos.z);
        
        if (direction.sqrMagnitude > 0.01f)
        {
            Vector3 desired = direction.normalized * maxSpeed;
            return desired - currentVelocity;
        }
        return Vector3.zero;
    }

    public static Vector3 Evade(Vector3 currentPos, Vector3 targetPos, Vector3 targetVelocity, Vector3 currentVelocity, float maxSpeed)
    {
        float distance = Vector3.Distance(new Vector3(currentPos.x, 0, currentPos.z), 
                                        new Vector3(targetPos.x, 0, targetPos.z));
        
        // 預測時間 (距離越遠，預測越遠)
        float lookAheadTime = distance / maxSpeed;
        
        // 預測目標未來的落點
        Vector3 futurePos = targetPos + targetVelocity * lookAheadTime;
        
        // 遠離那個未來落點
        return Flee(currentPos, futurePos, currentVelocity, maxSpeed);
    }

    //漫遊 (Wander) - 3D 空間 (假設在 XZ 平面上漫遊)
    public static Vector3 Wander(Vector3 currentPos, Vector3 currentVelocity, float maxSpeed, ref float wanderAngle, float circleDistance = 5f, float circleRadius = 2.5f, float angleChange = 15f)
    {
        wanderAngle += Random.Range(-angleChange, angleChange);
        
        Vector3 forward = currentVelocity.sqrMagnitude > 0.1f ? currentVelocity.normalized : Vector3.forward;
        Vector3 circleCenter = forward * circleDistance;
        
        // 將角度轉換為位移向量 (以 Y 軸為旋轉軸)
        Vector3 displacement = Quaternion.Euler(0, wanderAngle, 0) * Vector3.forward * circleRadius;
        Vector3 targetPoint = currentPos + circleCenter + displacement;
        
        return Seek(currentPos, targetPoint, currentVelocity, maxSpeed);
    }

    //射線避障 (Raycast Avoidance) - 使用 Unity 物理系統
    public static Vector3 RaycastAvoidance(Transform agentTransform, Vector3 currentVelocity, float maxSpeed, LayerMask obstacleLayers, float baseLookAhead = 1f, float adaptiveLength = 1f)
    {
        float speedRatio = currentVelocity.magnitude / (maxSpeed + 0.1f);
        float dynamicLength = baseLookAhead + adaptiveLength * speedRatio;
        
        Vector3 forward = currentVelocity.sqrMagnitude > 0.1f ? currentVelocity.normalized : agentTransform.forward;
        
        // 將射線發射點從「腳底」提升到「腰部/胸口」高度
        // 這樣射線才不會貼在地上打不到東西
        Vector3 rayOrigin = agentTransform.position + Vector3.up * 1f; 
        
        Vector3[] rayDirs = new Vector3[] {
            forward,                                       
            Quaternion.Euler(0, 45, 0) * forward,          
            Quaternion.Euler(0, -45, 0) * forward          
        };

        Vector3 combinedNormal = Vector3.zero;
        bool hitAnything = false;
        float minDistRatio = 1.0f;



        foreach (Vector3 dir in rayDirs)
        {
            // 假設障礙物在 "Obstacle" Layer，若不分 Layer 則省略 LayerMask
            if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, dynamicLength, obstacleLayers))
            {
                hitAnything = true;
                float ratio = hit.distance / dynamicLength;
                if (ratio < minDistRatio) minDistRatio = ratio;
                
                float weight = 1.0f - ratio;
                combinedNormal += hit.normal * weight;
                
                // Debug 視覺化射線
                Debug.DrawLine(rayOrigin, hit.point, Color.red);
            }
            else
            {
                Debug.DrawRay(rayOrigin, dir * dynamicLength, Color.green);
            }
        }

        if (hitAnything)
        {
            if (combinedNormal.sqrMagnitude > 0)
            {
                combinedNormal.Normalize();
                // 切線滑動計算
                Vector3 tangent = new Vector3(-combinedNormal.z, 0, combinedNormal.x);
                if (Vector3.Dot(tangent, forward) < 0) tangent = -tangent;
                
                Vector3 avoidDirection = (combinedNormal * 0.4f + tangent * 0.6f).normalized;
                Vector3 desired = avoidDirection * maxSpeed;
                float strength = Mathf.Max(0.2f, 1.0f - minDistRatio);
                
                return (desired - currentVelocity) * strength * 5.0f; // 給予較大的排斥推力
            }
            else
            {
                // 極端抵消情況，直接倒退NAVIGATE"
                return -currentVelocity.normalized * maxSpeed * 5.0f;
            }
        }

        return Vector3.zero;
    }

    // 動態避障 / 分離 (Dynamic Avoidance / Separation)
    public static Vector3 DynamicAvoidance(Transform agentTransform, float maxSpeed, float avoidanceRadius, LayerMask avoidLayers)
    {
        Vector3 currentPos = agentTransform.position;
        Vector3 steeringForce = Vector3.zero;
        int count = 0;

        // 核心：像 Raycast 一樣，直接用 LayerMask 掃描範圍內的所有目標
        Collider[] neighbors = Physics.OverlapSphere(currentPos, avoidanceRadius, avoidLayers);

        foreach (Collider neighbor in neighbors)
        {
            // 防呆：如果掃描到自己，直接跳過
            if (neighbor.transform == agentTransform) continue;

            Vector3 diff = currentPos - neighbor.transform.position;
            diff.y = 0; // 確保只在 XZ 平面上產生推力，避免飛天或遁地
            float dist = diff.magnitude;

            // 避免除以零，並且只處理半徑內的目標 (雖然 OverlapSphere 已經篩選過半徑，但安全第一)
            if (dist > 0.01f && dist < avoidanceRadius)
            {
                // 距離越近，反向推力越強
                steeringForce += diff.normalized / dist;
                count++;
            }
        }

        if (count > 0)
        {
            // 取平均推力
            steeringForce /= count;
            // 正規化並乘上最大速度
            steeringForce = steeringForce.normalized * maxSpeed;
        }

        return steeringForce;
    }

    public static Vector3 FollowPath(
        Vector3 currentPos, 
        Vector3 currentVelocity, 
        Vector3[] path, 
        ref int currentWaypointIndex, // 使用 ref 允許在函數內修改 Agent 紀錄的 Index
        float maxSpeed, 
        float waypointThreshold, 
        float jumpHeight,
        LayerMask obstacleLayers, 
        Agent agent,
        FollowPathMode mode = FollowPathMode.Strict, 
        float predictTime = 0.5f
        )
    {
        // 1. 路徑防呆：如果沒有路徑或已抵達終點，直接停止
        if (path == null || path.Length == 0 || currentWaypointIndex >= path.Length)
        {
            if (currentVelocity.sqrMagnitude > 0.1f)
                return -currentVelocity * 2.0f; // 給予反向阻力快速煞車
            return Vector3.zero;
        }

        Vector3 basePos = currentPos;

        // 1：決定判定基準點 (Look-ahead Predictive)
        if (mode == FollowPathMode.LookAheadPredictive)
        {
            // 預測未來的落點
            basePos = currentPos + currentVelocity * predictTime;

            // 檢查預測點是否已經極度靠近當前目標節點 (只比較 XZ 平面)
            Vector3 flatBase = new Vector3(basePos.x, 0, basePos.z);
            Vector3 flatNode = new Vector3(path[currentWaypointIndex].x, 0, path[currentWaypointIndex].z);
            float heightDiffToCurrentNode = Mathf.Abs(currentPos.y - path[currentWaypointIndex].y);
            
            if (Vector3.Distance(flatBase, flatNode) < waypointThreshold && heightDiffToCurrentNode <= jumpHeight)
            {
                currentWaypointIndex++;
                if (currentWaypointIndex >= path.Length) return Vector3.zero;
            }
        }

        // 2：視線前瞻策略 (Look-ahead) - 嘗試「切西瓜/抄捷徑」
        if (mode == FollowPathMode.LookAhead || mode == FollowPathMode.LookAheadPredictive)
        {
            int lookAheadSteps = 3; // 往下看 3 個點
            int furthestVisibleIdx = currentWaypointIndex;

            for (int i = 1; i <= lookAheadSteps; i++)
            {
                int checkIdx = currentWaypointIndex + i;
                if (checkIdx < path.Length)
                {
                    Vector3 targetPoint = path[checkIdx];

                    // 如果中間「沒有」撞到障礙物 (Line of Sight clear) && 兩點高度差低於jumpHeight/2 (允許有一點高度差，但不允許太大)
                    if (Mathf.Abs(currentPos.y - targetPoint.y) <= jumpHeight / 2f)
                    {
                        Vector3 rayStart = currentPos + Vector3.up * 1.0f;
                        Vector3 rayEnd = targetPoint + Vector3.up * 1.0f;

                        if (!Physics.Linecast(rayStart, rayEnd, obstacleLayers))
                        {
                            furthestVisibleIdx = checkIdx;
                        }
                    }
                }
            }
            currentWaypointIndex = furthestVisibleIdx; // 直接跳到能看見的最遠點
        }

        // 4. 核心差異 3：嚴格抵達判定 (Strict / Look-ahead)
        if (mode == FollowPathMode.Strict || mode == FollowPathMode.LookAhead)
        {
            Vector3 flatBase = new Vector3(basePos.x, 0, basePos.z);
            Vector3 flatNode = new Vector3(path[currentWaypointIndex].x, 0, path[currentWaypointIndex].z);

            if (Vector3.Distance(flatBase, flatNode) < waypointThreshold)
            {
                currentWaypointIndex++;
                if (currentWaypointIndex >= path.Length) return Vector3.zero;
            }
        }

        // 5. 取得最終決定的目標點
        Vector3 targetNode = path[currentWaypointIndex];

        // 最後一個點使用 Arrive 完美煞停，其他的點用 currentState 全速推進
        if (currentWaypointIndex == path.Length - 1)
        {
            // 將 waypointThreshold 當作 targetRadius，並稍微放大當作 slowRadius
            // Debug.Log($"Arriving at final node {currentWaypointIndex}: {targetNode}");
            return Arrive(currentPos, targetNode, currentVelocity, maxSpeed, waypointThreshold, waypointThreshold * 4f);
        }
        else
        {   
            //防呆:如果agent还在arrive状态就切回seek
            if (agent.HasState(AgentState.ARRIVE))
            {
                return Pursue(currentPos, targetNode, Vector3.zero, currentVelocity, maxSpeed);
            }
            else
            {   
                return Seek(currentPos, targetNode, currentVelocity, maxSpeed);
            }
            
        }
    }

    /// <summary>
    /// 根據前方路徑複雜度，動態決定最適合的 FollowPathMode
    /// </summary>
    public static FollowPathMode EvaluatePathComplexity(
        Vector3 currentPos, 
        Vector3[] path, 
        int currentIndex, 
        LayerMask obstacleLayers, 
        float rayHeightOffset = 1.0f)
    {
        // 1. 如果已經是最後一個點，乖乖嚴格走到終點
        if (currentIndex >= path.Length - 1) return FollowPathMode.Strict;

        Vector3 targetPoint = path[currentIndex];
        Vector3 nextPoint = path[currentIndex + 1];

        // 2. ✨ 物理空間感知：我們能安全地直接看到「下下個點」嗎？
        Vector3 rayStart = currentPos + Vector3.up * rayHeightOffset;
        Vector3 rayEnd = nextPoint + Vector3.up * rayHeightOffset;
        bool canSafelyCutCorner = !Physics.Linecast(rayStart, rayEnd, obstacleLayers);

        // 3. 高度檢查：樓梯還是懸崖？
        if (Mathf.Abs(targetPoint.y - currentPos.y) > 0.4f || Mathf.Abs(nextPoint.y - targetPoint.y) > 0.4f)
        {
            // 如果是上樓梯，且頭頂/前方沒有障礙物擋住視線，我們允許它用 LookAhead 滑順走上去
            if (canSafelyCutCorner) return FollowPathMode.LookAhead;
            
            // 如果視線被擋住 (可能是轉角樓梯或高低落差死角)，才必須嚴格到點起跳
            return FollowPathMode.Strict;
        }

        // 4. 轉彎檢查
        Vector3 dirToTarget = (targetPoint - currentPos).normalized;
        Vector3 dirToNext = (nextPoint - targetPoint).normalized;
        float dotProduct = Vector3.Dot(dirToTarget, dirToNext);

        if (dotProduct < 0.3f) // 急轉彎 (小於約 72 度)
        {
            // ✨ 如果是空曠處的急轉彎，大膽切西瓜！
            if (canSafelyCutCorner) return FollowPathMode.LookAhead;
            
            // 視線有阻擋，代表這是「實體牆壁的轉角」，必須嚴格繞過！
            return FollowPathMode.Strict; 
        }
        else if (dotProduct < 0.85f) // 中度彎道
        {
            if (canSafelyCutCorner) return FollowPathMode.LookAheadPredictive;
            return FollowPathMode.LookAhead;
        }

        // 5. 直走且平坦
        return FollowPathMode.LookAheadPredictive;
    }


}