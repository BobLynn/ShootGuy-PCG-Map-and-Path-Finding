using UnityEngine;
using KevinIglesias;
using System.Collections.Generic;

// ✨ 定義行為樹標準回傳狀態
public enum ActionStatus { Running, Success, Failure }

[RequireComponent(typeof(Agent))]
public class AgentActionController : MonoBehaviour
{
    private Agent agent;
    public Vector3 spawnPosition { get; private set; }
    private Vector3 spawnForward;

    [Header("Patrol Schedule")]
    public string defaultRouteName = "TEST_ROUTE";
    public int startIndex = 0;
    private RouteDefinition currentRoute;
    [HideInInspector] public int currentRouteWaypointIndex = 0;
    [HideInInspector] public float waitTimer = 0f;

    [Header("Investigate Strategy")]
    private Vector3 investigatePos;
    private float investigateWaitTimer = 0f;
    private float investigateTimeout = 0f; 
    private int investigatePhase = 0; 
    private GameObject dummyTarget;   

    [Header("Combat & Shooting Settings")]
    public Transform shootPoint;          
    public float attackRange { get; private set; } = 0f;          
    public float aimingTime = 3.0f;       
    public float lockTime = 2.5f;        
    public float attackCooldown = 2.0f;   

    private float combatTimer = 0f;
    [HideInInspector] public int combatPhase = 0;          
    private Vector3 lockedDirection;      
    private LineRenderer aimLaser;
    
    [Header("Chase Settings")]
    private int chasePhase = 0;
    private float chaseTimer = 0f;
    public float reactionTime = 0.3f; 
    [HideInInspector] public Transform realTarget; 

    [Header("RUN / Flee / Dodge Settings")]
    public Transform escapeEndpoint;       
    public bool runToSpecificEndpoint = false; 
    public float fleeReactionTime = 0.5f; 
    public float hideSearchRadius = 25f;    
    public float distanceBehindObstacle = 2.5f; 
    public float coverCrowdRadius = 2.0f;     
    public float crowdPenaltyWeight = 15.0f;  
    private Vector3 currentDodgePoint;     
    private int fleePhase = 0;
    private float fleeTimer = 0f;
    private float flankRecalculateTimer = 0f;

    void Awake()
    {
        agent = GetComponent<Agent>();
        
        if (agent.sensory == null) agent.sensory = GetComponent<SensorySystem>();
        if (agent.navigator == null) agent.navigator = GetComponent<AgentNavigator>();

        ApplyWeaponStats();
        
        dummyTarget = new GameObject($"DummyTarget_{gameObject.name}");
        TryGetComponent(out aimLaser);
        if (aimLaser != null) aimLaser.enabled = false;
    }

    void Start()
    {
        spawnPosition = agent.transform.position;
        spawnForward = agent.transform.forward;

        if (agent.agentType == AgentType.TARGET)
        {
            runToSpecificEndpoint = true;
            if (escapeEndpoint == null)
            {
                Debug.LogWarning($"【AgentActionController】{agent.name} is a TARGET but has no escapeEndpoint assigned!");
            }
        }
    }

    // ==========================================
    // ✨ 執行介面：回傳 ActionStatus 以供大腦做決策
    // ==========================================
    public ActionStatus ExecuteAction(AgentDecision currentDecision)
    {
        ActionStatus status = ActionStatus.Running;

        switch (currentDecision)
        {
            case AgentDecision.PATROL:
                status = HandlePatrol(); break;
            case AgentDecision.SHORTREST:
                status = HandleShortRest(); break;
            case AgentDecision.LONGREST:
                status = HandleLongRest(); break;
            case AgentDecision.INVESTIGATE:
                status = HandleInvestigate(); break;
            case AgentDecision.ATTACK:
                status = HandleAttack(); break;
            case AgentDecision.CHASE:
                status = HandleChase(); break;
            case AgentDecision.RUN:
                status = HandleRun(); break;
        }
        
        attackRange = agent.sensory.viewRadius * 0.6f; 
        UpdateSteeringState(currentDecision);

        return status;
    }

    public void DisableAimLaser()
    {
        if (aimLaser != null) aimLaser.enabled = false;
    }

    public bool IsAtSpawn()
    {
        Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 flatSpawn = new Vector3(spawnPosition.x, 0, spawnPosition.z);
        return Vector3.Distance(flatPos, flatSpawn) < agent.navigator.waypointThreshold * 2f;
    }

    public bool HasSuccessfullyEscaped()
    {
        if (runToSpecificEndpoint && escapeEndpoint != null)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatTarget = new Vector3(escapeEndpoint.position.x, 0, escapeEndpoint.position.z);
            return Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold * 1.2f;
        }
        return false;
    }

    // ==========================================
    // 1. Patrol Logic
    // ==========================================
    public void ChangeRoute(string newRouteName, int forcedStartIndex = -1, bool autoSearchStartPoint = false)
    {
        if (RouteManager.Instance == null) return;

        RouteDefinition fetchedRoute = RouteManager.Instance.GetRoute(newRouteName);
        if (fetchedRoute != null)
        {
            currentRoute = fetchedRoute;
            if (autoSearchStartPoint)
            {
                float closestDist = Mathf.Infinity;
                for (int i = 0; i < currentRoute.waypoints.Count; i++)
                {
                    float dist = Vector3.Distance(transform.position, currentRoute.waypoints[i].point.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        currentRouteWaypointIndex = i;
                    }
                }
            }
            else if (forcedStartIndex >= 0 && forcedStartIndex < currentRoute.waypoints.Count)
                currentRouteWaypointIndex = forcedStartIndex;
            else 
                currentRouteWaypointIndex = 0;
        }
    }

    public bool AdvanceWaypoint()
    {
        currentRouteWaypointIndex++;
        if (currentRouteWaypointIndex >= currentRoute.waypoints.Count)
        {
            if (currentRoute.isLoop) currentRouteWaypointIndex = 0;
            else return false; 
        }
        return true;
    }

    public void StartPatrol()
    {
        if (currentRoute == null || currentRoute.waypoints.Count == 0) return;
        agent.targetObject = currentRoute.waypoints[currentRouteWaypointIndex].point;
        agent.navigator.ResetNavigation();
        agent.navigator.isNavigating = true;
        agent.sensory.viewAngle = 45f;
    }

    private ActionStatus HandlePatrol()
    {
        if (currentRoute == null || currentRoute.waypoints.Count == 0) return ActionStatus.Failure;
        
        Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 flatTarget = agent.targetObject != null ? new Vector3(agent.targetObject.position.x, 0, agent.targetObject.position.z) : Vector3.zero;

        // 條件 1：絕對距離抵達
        if (agent.targetObject != null && Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold * 1.5f)
        {
            return ActionStatus.Success;
        }

        // ✨ 條件 2：如果巡邏點被放置在奇怪的位置，只要走到 A* 路徑盡頭也算抵達！
        if (agent.navigator.currentPath != null && agent.navigator.currentPath.Length > 0)
        {
            if (agent.navigator.currentWaypointIndex >= agent.navigator.currentPath.Length - 1)
            {
                Vector3 pathEnd = agent.navigator.currentPath[agent.navigator.currentPath.Length - 1];
                Vector3 flatPathEnd = new Vector3(pathEnd.x, 0, pathEnd.z);
                
                if (Vector3.Distance(flatPos, flatPathEnd) < agent.navigator.waypointThreshold * 1.5f && agent.velocity.sqrMagnitude < 0.2f)
                {
                    return ActionStatus.Success;
                }
            }
        }

        return ActionStatus.Running;
    }

    // ==========================================
    // 2. Rest Logic
    // ==========================================
    public void StartShortRest()
    {
        if (currentRoute != null && currentRoute.waypoints.Count > 0)
            waitTimer = currentRoute.waypoints[currentRouteWaypointIndex].waitTime;
        else
            waitTimer = 2.0f; 
            
        agent.targetObject = null;
        agent.navigator.ResetNavigation();
    }

    private ActionStatus HandleShortRest()
    {
        waitTimer -= Time.deltaTime;
        if (waitTimer <= 0f) return ActionStatus.Success;
        return ActionStatus.Running;
    }

    public void StartLongRest()
    {
        agent.targetObject = null;
        agent.navigator.ResetNavigation();
    }

    private ActionStatus HandleLongRest()
    {
        Vector3 targetDir = spawnForward;
        targetDir.y = 0;
        
        if (Vector3.Angle(transform.forward, targetDir) > 2f)
        {
            agent.isTurningInPlace = true;
            agent.turnTargetPos = transform.position + targetDir * 5f; 
        }
        else
        {
            agent.isTurningInPlace = false;
        }
        return ActionStatus.Running; 
    }

    // ==========================================
    // 3. Flee / Run Logic
    // ==========================================
    private Vector3 CalculateDodgePoint()
    {
        if (agent.threatObjects == null || agent.threatObjects.Length == 0) return transform.position;

        List<Vector3> validThreats = new List<Vector3>();
        Vector3 threatCenter = Vector3.zero;

        for (int i = 0; i < agent.threatObjects.Length; i++)
        {            
            if (agent.threatObjects[i] != null)
            {
                validThreats.Add(agent.threatObjects[i].position);
                threatCenter += agent.threatObjects[i].position;
            }
        }

        if (validThreats.Count == 0) return transform.position;
        threatCenter /= validThreats.Count; 

        Agent[] allAgents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        Collider[] obstacles = Physics.OverlapSphere(transform.position, hideSearchRadius, agent.navigator.ObstacleLayers);

        Vector3 bestHidePos = transform.position;
        float bestScore = Mathf.Infinity; 
        bool foundValidSpot = false;

        foreach (Collider obs in obstacles)
        {
            if (obs.transform == transform) continue;
            bool isThreat = false;
            foreach (Transform t in agent.threatObjects) { if (t != null && obs.transform == t) isThreat = true; }
            if (isThreat) continue;

            Vector3 obsPos = obs.transform.position;
            Vector3 dirFromThreatCenter = (obsPos - threatCenter).normalized;
            dirFromThreatCenter.y = 0;

            Vector3 potentialHidePos = obsPos + dirFromThreatCenter * distanceBehindObstacle;
            potentialHidePos.y = transform.position.y; 
            Vector3 targetCheckPos = potentialHidePos + Vector3.up * 1.0f; 

            bool isValidForAllThreats = true;
            
            foreach (Vector3 specificThreatPos in validThreats)
            {
                Vector3 eyePos = specificThreatPos + Vector3.up * 1.5f;
                if (!Physics.Linecast(eyePos, targetCheckPos, agent.navigator.ObstacleLayers))
                {
                    isValidForAllThreats = false;
                    break; 
                }
            }

            if (isValidForAllThreats)
            {
                float baseDistance = Vector3.Distance(transform.position, potentialHidePos);
                int reservedCount = 0;
                
                foreach (Agent ally in allAgents)
                {
                    if (ally == this.agent || isThreat) continue;

                    if (ally.targetObject != null)
                    {
                        if (Vector3.Distance(ally.targetObject.position, potentialHidePos) < coverCrowdRadius) reservedCount++;
                    }
                    else
                    {
                        if (Vector3.Distance(ally.transform.position, potentialHidePos) < coverCrowdRadius) reservedCount++;
                    }
                }

                float score = baseDistance + (reservedCount * crowdPenaltyWeight);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestHidePos = potentialHidePos;
                    foundValidSpot = true;
                }
            }
        }

        if (!foundValidSpot)
        {
            Vector3 panicDir = (transform.position - threatCenter).normalized;
            panicDir.y = 0;
            bestHidePos = transform.position + panicDir * 12f;
        }

        return bestHidePos;
    }

    public void StartFlee(Transform threat, int forceStartPhase = 0)
    {
        agent.animator.SetTrigger("Sprint");
        agent.threatObjects = new Transform[] { threat }; 
        
        fleePhase = forceStartPhase;
        fleeTimer = 0f;
        agent.navigator.ResetNavigation();

        if (runToSpecificEndpoint && escapeEndpoint != null)
        {
            fleePhase = -1; 
            agent.targetObject = escapeEndpoint;
            agent.navigator.isNavigating = true;
        }
        else
        {
            currentDodgePoint = CalculateDodgePoint();
            dummyTarget.transform.position = currentDodgePoint;
            agent.targetObject = dummyTarget.transform; 
            agent.navigator.isNavigating = true;
        }

        if (forceStartPhase == 1) fleePhase = 1;
    }

    private ActionStatus HandleRun()
    {
        if (runToSpecificEndpoint && escapeEndpoint != null)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatTarget = new Vector3(escapeEndpoint.position.x, 0, escapeEndpoint.position.z);
            if (Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold)
            {
                agent.navigator.ResetNavigation();
                return ActionStatus.Success; 
            }
            return ActionStatus.Running;
        }

        if (agent.threatObjects == null || agent.threatObjects.Length == 0) return ActionStatus.Failure;
        
        switch (fleePhase)
        {
            case 0: 
                fleeTimer += Time.deltaTime;
                if (fleeTimer >= fleeReactionTime)
                {
                    fleePhase = 1;
                    agent.navigator.isNavigating = true;
                    fleeTimer = 0f;
                }
                break;

            case 1: 
                fleeTimer += Time.deltaTime;
                if (fleeTimer >= 0.5f)
                {
                    currentDodgePoint = CalculateDodgePoint();
                    dummyTarget.transform.position = currentDodgePoint;
                    fleeTimer = 0f;
                }

                float closestThreatDist = Mathf.Infinity;
                bool hasActiveThreat = false;
                foreach (Transform threat in agent.threatObjects)
                {
                    if (threat != null)
                    {
                        hasActiveThreat = true;
                        float dist = Vector3.Distance(transform.position, threat.position);
                        if (dist < closestThreatDist) closestThreatDist = dist;
                    }
                }

                if (!hasActiveThreat) return ActionStatus.Success;

                float distToDodgePoint = Vector3.Distance(transform.position, currentDodgePoint);
                bool isSuccessfullyHidden = distToDodgePoint < agent.navigator.waypointThreshold;
                bool isOutOfRange = closestThreatDist > agent.sensory.viewRadius;

                if (isSuccessfullyHidden || isOutOfRange)
                {
                    agent.threatObjects = null;
                    agent.navigator.ResetNavigation();
                    return ActionStatus.Success; 
                }
                break;
        }
        return ActionStatus.Running;
    }

    // ==========================================
    // 4. Chase Logic
    // ==========================================
    public void StartChase(Transform target, int forceStartPhase = 0)
    {
        agent.animator.ResetTrigger("Shoot03");
        agent.animator.SetTrigger("Sprint");

        realTarget = target; 
        agent.targetObject = target;

        chasePhase = forceStartPhase;
        chaseTimer = 0f;
        agent.sensory.viewAngle = 45f; 
        agent.navigator.ResetNavigation();

        if (forceStartPhase == 1)
        {
            chasePhase = 1;
            agent.isTurningInPlace = false;
            agent.navigator.isNavigating = true;
        }

        if (agent.brain.enableAdvancedTactics) agent.brain.AlertAllies(target); 
    }

    private ActionStatus HandleChase()
    {
        if (realTarget == null) return ActionStatus.Failure;

        switch (chasePhase)
        {
            case 0: 
                agent.isTurningInPlace = true;
                agent.turnTargetPos = realTarget.position;
                chaseTimer += Time.deltaTime;
                
                if (chaseTimer >= reactionTime)
                {
                    chasePhase = 1;
                    agent.isTurningInPlace = false;
                    agent.navigator.isNavigating = true;
                }
                break;

            case 1: 
                if (agent.brain.enableAdvancedTactics)
                {
                    flankRecalculateTimer -= Time.deltaTime;
                    if (flankRecalculateTimer <= 0f)
                    {
                        flankRecalculateTimer = 0.5f; 
                        Collider[] allies = Physics.OverlapSphere(transform.position, 20f, agent.brain.allyLayer);
                        int chasingCount = 1;
                        int myIndex = 0;
                        
                        foreach (Collider col in allies)
                        {
                            if (col.transform == this.transform) continue;
                            AgentBrain allyBrain = col.GetComponent<AgentBrain>();
                            if (allyBrain != null && allyBrain.currentDecision == AgentDecision.CHASE)
                            {
                                chasingCount++;
                                if (col.transform.GetInstanceID() < this.transform.GetInstanceID())
                                    myIndex++; 
                            }
                        }

                        if (chasingCount > 1)
                        {
                            Vector3 dirToTarget = (realTarget.position - transform.position).normalized;
                            Vector3 rightDir = Vector3.Cross(Vector3.up, dirToTarget).normalized;
                            
                            float flankSpacing = 2.0f; 
                            Vector3 offset = Vector3.zero;
                            
                            if (myIndex == 1) offset = rightDir * flankSpacing;
                            else if (myIndex == 2) offset = -rightDir * flankSpacing;
                            else if (myIndex == 3) offset = rightDir * flankSpacing * 2f;
                            else if (myIndex == 4) offset = -rightDir * flankSpacing * 2f;
                            
                            dummyTarget.transform.position = realTarget.position + offset;
                            agent.targetObject = dummyTarget.transform; 
                        }
                        else
                        {
                            agent.targetObject = realTarget; 
                        }
                    }
                }
                else
                {
                    agent.targetObject = realTarget;
                }
                break;
        }
        return ActionStatus.Running; 
    }

    // ==========================================
    // 5. Attack & Shooting Logic
    // ==========================================
    private void ApplyWeaponStats()
    {
        switch (agent.currentWeapon)
        {
            case SoldierWeapons.AssaultRifle: 
                attackRange = agent.sensory.viewRadius * 0.7f; aimingTime = 1.5f; lockTime = 0.7f; attackCooldown = 0.7f;
                shootPoint.localPosition = new Vector3(0.116f, 1.255f, 0.711f); break;
            case SoldierWeapons.Rifle: 
                attackRange = agent.sensory.viewRadius * 0.9f; aimingTime = 3.0f; lockTime = 1.0f; attackCooldown = 2.5f;
                shootPoint.localPosition = new Vector3(0.139f, 1.336f, 1.144f); break;
            case SoldierWeapons.Gun: 
                attackRange = agent.sensory.viewRadius * 0.5f; aimingTime = 1.0f; lockTime = 0.6f; attackCooldown = 1.0f;
                shootPoint.localPosition = new Vector3(0.19f, 1.23f, 0.667f); break;
            case SoldierWeapons.Bazooka: 
                attackRange = agent.sensory.viewRadius * 0.8f; aimingTime = 3.5f; lockTime = 1.5f; attackCooldown = 4.0f;
                shootPoint.localPosition = new Vector3(0.191f, 1.405f, 0.842f); break;
            default:
                attackRange = agent.sensory.viewRadius * 0.7f; aimingTime = 2.0f; lockTime = 0.8f; attackCooldown = 1.5f;
                shootPoint.localPosition = new Vector3(0, 1.23f, 0.47f); break;
        }
        if (attackRange > agent.sensory.viewRadius * 0.9f) 
            attackRange = agent.sensory.viewRadius * 0.9f;
    }

    public void StartAttack(Transform target)
    {
        realTarget = target;
        agent.targetObject = realTarget;
        combatPhase = 0;
        combatTimer = 0f;
        agent.sensory.viewAngle = 80f; 
        
        agent.navigator.ResetNavigation(); 
        agent.animator.ResetTrigger("Sprint");
        agent.animator.SetTrigger("Shoot03");

        if (agent.brain.enableAdvancedTactics) agent.brain.AlertAllies(target);
    }

    private ActionStatus HandleAttack()
    {
        if (realTarget == null) return ActionStatus.Failure;
    
        switch (combatPhase)
        {
            case 0: 
                agent.isTurningInPlace = true;
                agent.turnTargetPos = realTarget.position;
                
                if (agent.brain.enableAdvancedTactics)
                {
                    Vector3 aimDir = (agent.turnTargetPos - shootPoint.position).normalized;
                    float distToTarget = Vector3.Distance(shootPoint.position, agent.turnTargetPos);
                    bool lineOfFireBlocked = false;
                    LayerMask blockMask = agent.brain.allyLayer | agent.navigator.ObstacleLayers;
                    RaycastHit[] hits = Physics.SphereCastAll(shootPoint.position, 0.4f, aimDir, distToTarget, blockMask);
                    
                    foreach (var h in hits)
                    {
                        if (h.collider.transform != this.transform && h.collider.transform != realTarget)
                        {
                            if (h.distance < distToTarget) { lineOfFireBlocked = true; break; }
                        }
                    }

                    if (lineOfFireBlocked)
                    {
                        Vector3 rightDir = Vector3.Cross(Vector3.up, aimDir).normalized;
                        Vector3 leftDir = -rightDir;

                        if (agent.strafeDirection == Vector3.zero || Physics.Raycast(transform.position + Vector3.up, agent.strafeDirection, 1.5f, agent.navigator.ObstacleLayers))
                        {
                            float rightSpace = 10f, leftSpace = 10f;
                            if (Physics.Raycast(transform.position + Vector3.up, rightDir, out RaycastHit rHit, 10f, agent.navigator.ObstacleLayers)) rightSpace = rHit.distance;
                            if (Physics.Raycast(transform.position + Vector3.up, leftDir, out RaycastHit lHit, 10f, agent.navigator.ObstacleLayers)) leftSpace = lHit.distance;
                            agent.strafeDirection = (rightSpace >= leftSpace) ? rightDir : leftDir;
                        }

                        agent.isStrafing = true;
                        if (aimLaser != null) aimLaser.enabled = false; 
                        return ActionStatus.Running; 
                    }
                }

                agent.isStrafing = false; 
                agent.strafeDirection = Vector3.zero; 
                combatTimer += Time.deltaTime;

                if (aimLaser != null)
                {
                    aimLaser.enabled = true;
                    aimLaser.SetPosition(0, shootPoint.position);
                    aimLaser.SetPosition(1, realTarget.position + Vector3.up * 1f); 
                    aimLaser.startColor = Color.yellow; aimLaser.endColor = Color.yellow;
                }

                if (combatTimer >= (aimingTime - lockTime))
                {
                    combatPhase = 1;
                    Vector3 targetPos = realTarget.position + Vector3.up * 1f;
                    lockedDirection = (targetPos - shootPoint.position).normalized;
                }
                break;

            case 1: 
                agent.isTurningInPlace = false; 
                combatTimer += Time.deltaTime;
                if (aimLaser != null) { aimLaser.startColor = Color.red; aimLaser.endColor = Color.red; }
                if (combatTimer >= aimingTime) combatPhase = 2; 
                break;

            case 2: 
                if (aimLaser != null) aimLaser.enabled = false; 
                ShootBullet(lockedDirection);
                combatTimer = 0f; combatPhase = 3; 
                break;

            case 3: 
                agent.isTurningInPlace = true;
                agent.turnTargetPos = realTarget.position;
                combatTimer += Time.deltaTime;
                if (combatTimer >= attackCooldown) { combatPhase = 0; combatTimer = 0f; }
                break;
        }
        return ActionStatus.Running;
    }

    private void ShootBullet(Vector3 direction)
    {
        agent.animator.SetTrigger("Shoot01");
        GameObject bullet = BulletPool.Instance.GetBullet(shootPoint.position, Quaternion.LookRotation(direction));
        if (bullet.TryGetComponent(out SimpleProjectile projectile)) projectile.Fire(direction, false);
    }

    // ==========================================
    // 6. Investigate Logic
    // ==========================================
    public void StartInvestigation(Vector3 targetPos, int forceStartPhase = 0)
    {
        agent.animator.SetTrigger("Sprint");
        investigatePos = targetPos;
        investigatePhase = forceStartPhase;
        investigateWaitTimer = 2.0f; 
        investigateTimeout = 10.0f; 
        
        agent.sensory.viewAngle = 120f; 
        agent.navigator.ResetNavigation();

        if (forceStartPhase == 1) agent.isTurningInPlace = false;
        else if (forceStartPhase == 2)
        {
            agent.isTurningInPlace = false;
            dummyTarget.transform.position = investigatePos;
            agent.targetObject = dummyTarget.transform;
            agent.navigator.isNavigating = true;
        }
    }

    private ActionStatus HandleInvestigate()
    {
        switch (investigatePhase)
        {
            case 0: 
                agent.isTurningInPlace = true;
                agent.turnTargetPos = investigatePos;
                agent.targetObject = null; 
                Vector3 dirToTarget = (investigatePos - transform.position);
                dirToTarget.y = 0;
                if (Vector3.Angle(transform.forward, dirToTarget) < 5f) { agent.isTurningInPlace = false; investigatePhase = 1; }
                break;

            case 1: 
                investigateWaitTimer -= Time.deltaTime;
                if (investigateWaitTimer <= 0f)
                {
                    investigatePhase = 2;
                    dummyTarget.transform.position = investigatePos;
                    agent.targetObject = dummyTarget.transform;
                    agent.navigator.isNavigating = true;
                }
                break;

            case 2: 
                investigateTimeout -= Time.deltaTime;
                if (investigateTimeout <= 0f) 
                {
                    agent.navigator.ResetNavigation();
                    return ActionStatus.Success; 
                }

                Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
                Vector3 flatTarget = new Vector3(investigatePos.x, 0, investigatePos.z);

                // 條件 1：絕對距離抵達 (適用於空曠地帶)
                if (Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold * 1.5f)
                {
                    agent.navigator.ResetNavigation();
                    return ActionStatus.Success; 
                }

                // ✨【關鍵修復】條件 2：如果走到 A* 路徑盡頭且停下來了，視同盡力完成調查！
                if (agent.navigator.currentPath != null && agent.navigator.currentPath.Length > 0)
                {
                    if (agent.navigator.currentWaypointIndex >= agent.navigator.currentPath.Length - 1)
                    {
                        Vector3 pathEnd = agent.navigator.currentPath[agent.navigator.currentPath.Length - 1];
                        Vector3 flatPathEnd = new Vector3(pathEnd.x, 0, pathEnd.z);
                        
                        // 已經走到導航提供的最後一個點，且速度幾乎降到 0
                        if (Vector3.Distance(flatPos, flatPathEnd) < agent.navigator.waypointThreshold * 1.5f && agent.velocity.sqrMagnitude < 0.2f)
                        {
                            Debug.Log($"{agent.name} 抵達 A* 可行走範圍的最盡頭，完成調查！");
                            agent.navigator.ResetNavigation();
                            return ActionStatus.Success;
                        }
                    }
                }
                break;
        }
        return ActionStatus.Running;
    }

    // ==========================================
    // 7. Steering Sync (與 Navigator 的對接)
    // ==========================================
    private void UpdateSteeringState(AgentDecision currentDecision)
    {
        agent.ClearStates(); 
        
        agent.isWaiting = false;
        if (currentDecision == AgentDecision.SHORTREST) agent.isWaiting = true;
        else if (currentDecision == AgentDecision.LONGREST) agent.isWaiting = true;
        else if (currentDecision == AgentDecision.ATTACK)
        {
            if (!agent.isStrafing) agent.isWaiting = true;
        }
        else if (currentDecision == AgentDecision.INVESTIGATE && investigatePhase == 1) agent.isWaiting = true;

        if (agent.isWaiting) return;

        if (currentDecision == AgentDecision.CHASE)
        {
            agent.AddState(AgentState.PURSUE);
            agent.maxSpeed = agent.chaseSpeed;
        }
        else if (currentDecision == AgentDecision.RUN)
        {
            agent.AddState(AgentState.PURSUE); 
            if (!runToSpecificEndpoint) agent.AddState(AgentState.FLEE); 
            agent.maxSpeed = agent.chaseSpeed; 
        }
        else if (currentDecision == AgentDecision.ATTACK)
        {
            agent.AddState(AgentState.SEEK);
            agent.maxSpeed = agent.chaseSpeed * 0.8f; 
        }
        else if (currentDecision == AgentDecision.PATROL || currentDecision == AgentDecision.INVESTIGATE)
        {
            agent.AddState(AgentState.SEEK);
            agent.maxSpeed = agent.defaultSpeed;
        }

        if (agent.targetObject != null)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatTarget = new Vector3(agent.targetObject.position.x, 0, agent.targetObject.position.z);
            if (Vector3.Distance(flatPos, flatTarget) < agent.navigator.arriveSlowRadius)
            {
                agent.ClearStates(); 
                agent.AddState(AgentState.ARRIVE); 
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!ShouldShowDebugGizmos())
            return;

        if (Application.isPlaying && agent != null && agent.brain != null)
        {
            if (agent.brain.currentDecision == AgentDecision.INVESTIGATE)
            {
                Gizmos.color = Color.blue; Gizmos.DrawSphere(investigatePos, 0.5f);   
            }
            if (agent.brain.currentDecision == AgentDecision.ATTACK)
            {
                Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, attackRange);
            }
            if (agent.brain.currentDecision == AgentDecision.RUN && !runToSpecificEndpoint)
            {
                Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(currentDodgePoint, 0.6f); Gizmos.DrawLine(transform.position, currentDodgePoint);
            }
        }
    }

    private bool ShouldShowDebugGizmos()
    {
        if (agent == null)
            agent = GetComponent<Agent>();

        return agent == null || agent.showDebugGizmos;
    }
}
