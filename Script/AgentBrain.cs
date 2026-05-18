using UnityEngine;

[RequireComponent(typeof(Agent))]
public class AgentBrain : MonoBehaviour
{
    private Agent agent;

    [Header("Current Decision")]
    public AgentDecision currentDecision = AgentDecision.PATROL;
    public AgentDecision defaultDecision = AgentDecision.PATROL;

    [Header("Patrol Schedule")]
    public string defaultRouteName = "TEST_ROUTE";
    public int startIndex = 0;
    private RouteDefinition currentRoute;
    public int currentRouteWaypointIndex = 0;
    public float waitTimer = 0f;

    [Header("Investigate Strategy")]
    private Vector3 investigatePos;
    private float investigateWaitTimer = 0f;
    private int investigatePhase = 0; // 0: 轉向, 1: 停留觀察, 2: 追蹤前往
    private GameObject dummyTarget;   // 用來指引 Navigator 前往特定座標的虛擬物件

    [Header("Combat & Shooting Settings")]
    public Transform shootPoint;          // 槍口或胸前發射點的位置
    public float aimingTime = 2.0f;       // 總瞄準時間
    public float lockTime = 0.25f;        // 射擊前的鎖定時間 (給玩家的閃避空檔)
    public float attackCooldown = 2.0f;   // 射擊後的冷卻時間

    private float combatTimer = 0f;
    private int combatPhase = 0;          // 0: 瞄準, 1: 鎖定, 2: 擊發, 3: 冷卻
    private Vector3 lockedDirection;      // 鎖定時的子彈發射方向

    // 可選：用來畫出紅外線瞄準線的視覺提示
    private LineRenderer aimLaser;

    void Awake()
    {
        agent = GetComponent<Agent>();
        agent.brain = this; // 互相綁定
        agent.navigator = this.GetComponent<AgentNavigator>(); // 確保 Navigator 參照正確

        // 建立一個隱藏的虛擬目標，當我們只需要 Agent 前往某個座標(而非追逐特定實體)時使用
        dummyTarget = new GameObject($"DummyTarget_{gameObject.name}");

        // 如果有掛載 LineRenderer，就抓取它來做視覺提示
        TryGetComponent(out aimLaser);
        if (aimLaser != null) aimLaser.enabled = false;
    }

    void Start()
    {
        ChangeRoute(defaultRouteName, startIndex, false);
    }

    // 在 AgentBrain.cs 中
    void Update()
    {
        // 1. 執行高階行為決策 (Patrol, Investigate 等)
        switch (currentDecision)
        {
            case AgentDecision.PATROL:
            case AgentDecision.REST:
                HandlePatrol();
                break;
            case AgentDecision.INVESTIGATE:
                HandleInvestigate();
                break;
        }

        // 2. 統一下達中階 Steering State (取代原本 Agent.cs 的邏輯)
        UpdateSteeringState();
    }

    private void UpdateSteeringState()
    {
        agent.ClearStates(); 

        if (agent.isWaiting || agent.isTurningInPlace) return;

        // 1. 根據高階決策決定基礎移動狀態
        if (currentDecision == AgentDecision.CHASE)
        {
            agent.AddState(AgentState.CHASE);
            agent.maxSpeed = agent.chaseSpeed;
        }
        else if (currentDecision == AgentDecision.PATROL || currentDecision == AgentDecision.INVESTIGATE)
        {
            agent.AddState(AgentState.SEEK);
            agent.maxSpeed = agent.defaultSpeed;
        }

        // 2. 評估距離：如果足夠接近目標：ARRIVE 狀態
        if (agent.targetObject != null)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatTarget = new Vector3(agent.targetObject.position.x, 0, agent.targetObject.position.z);
            float distanceToTarget = Vector3.Distance(flatPos, flatTarget);

            // 當進入減速半徑，觸發 ARRIVE 旗標
            if (distanceToTarget < agent.navigator.arriveSlowRadius)
            {
                agent.ClearStates(); // 先清除原有狀態，確保 ARRIVE 不會被 SEEK/CHASE 蓋掉
                agent.AddState(AgentState.ARRIVE); 
            }
        }
        if (agent.navigator.isNavigating)
        {
            if(agent.navigator.currentPath != null && agent.navigator.currentWaypointIndex == agent.navigator.currentPath.Length - 1)
            {
                agent.ClearStates();
                agent.AddState(AgentState.ARRIVE);
            }
            else if (agent.navigator.currentPath != null && agent.navigator.currentWaypointIndex < agent.navigator.currentPath.Length - 1)
            {
                if (agent.HasState(AgentState.ARRIVE))
                {
                    agent.ClearStates();
                    agent.AddState(AgentState.SEEK);
                }
            }
        }
        if (agent.HasState(AgentState.CHASE)) agent.maxSpeed = agent.chaseSpeed;
        else agent.maxSpeed = agent.defaultSpeed;
    }
    // --- Combat & Shooting 邏輯 ---
    public void StartAttack(Transform target)
    {
        currentDecision = AgentDecision.ATTACK;
        agent.targetObject = target;
        combatPhase = 0;
        combatTimer = 0f;
        
        agent.navigator.ResetNavigation(); // 戰鬥時停止尋路移動
    }

    private void HandleAttack()
    {
        if (agent.targetObject == null) return;

        switch (combatPhase)
        {
            case 0: // Phase 0: 瞄準階段 (Aiming)
                // 強制 Agent 站在原地並面向玩家
                agent.isTurningInPlace = true;
                agent.turnTargetPos = agent.targetObject.position;
                agent.ClearStates(); 

                combatTimer += Time.deltaTime;

                // --- 視覺提示：畫出雷射線 (不會有物理碰撞) ---
                if (aimLaser != null)
                {
                    aimLaser.enabled = true;
                    aimLaser.SetPosition(0, shootPoint.position);
                    aimLaser.SetPosition(1, agent.targetObject.position + Vector3.up * 1f); // 瞄準胸口
                    aimLaser.startColor = Color.yellow; // 警告色
                    aimLaser.endColor = Color.yellow;
                }

                // 當時間推進到 (總瞄準時間 - 鎖定時間) 時，進入下一階段
                if (combatTimer >= (aimingTime - lockTime))
                {
                    combatPhase = 1;
                    
                    // 記錄當下目標的方向，這就是等一下實體子彈要飛出去的絕對方向！
                    Vector3 targetPos = agent.targetObject.position + Vector3.up * 1f;
                    lockedDirection = (targetPos - shootPoint.position).normalized;
                }
                break;

            case 1: // Phase 1: 鎖定/前搖階段 (Wind-up / Lock)
                // 關鍵：解除跟隨玩家旋轉！Agent 會保持最後一刻的面朝方向
                agent.isTurningInPlace = false; 
                combatTimer += Time.deltaTime;

                // --- 視覺提示：雷射轉為紅色，代表即將開火 ---
                if (aimLaser != null)
                {
                    aimLaser.startColor = Color.red;
                    aimLaser.endColor = Color.red;
                }

                if (combatTimer >= aimingTime)
                {
                    combatPhase = 2; // 時間到，準備擊發
                }
                break;

            case 2: // Phase 2: 擊發階段 (Shooting)
                if (aimLaser != null) aimLaser.enabled = false; // 關閉雷射
                
                ShootBullet(lockedDirection);
                
                combatTimer = 0f;
                combatPhase = 3; // 進入冷卻
                break;

            case 3: // Phase 3: 冷卻階段 (Cooldown)
                // 在冷卻期間可以讓 Agent 繼續盯著玩家，或是切換回 CHASE 狀態一邊追一邊開槍
                agent.isTurningInPlace = true;
                agent.turnTargetPos = agent.targetObject.position;
                
                combatTimer += Time.deltaTime;
                if (combatTimer >= attackCooldown)
                {
                    // 冷卻結束，重新進入瞄準輪迴
                    combatPhase = 0; 
                    combatTimer = 0f;
                }
                break;
        }
    }

    private void ShootBullet(Vector3 direction)
    {
        // 改成跟 Pool 借子彈：
        GameObject bullet = BulletPool.Instance.GetBullet(shootPoint.position, Quaternion.LookRotation(direction));
        
        if (bullet.TryGetComponent(out SimpleProjectile projectile))
        {
            projectile.Fire(direction);
        }
        
        UnityEngine.Debug.Log("Bang! Fired pooled bullet at locked direction.");
    }

    // --- Investigate 邏輯 ---
    public void StartInvestigation(Vector3 targetPos)
    {
        currentDecision = AgentDecision.INVESTIGATE;
        investigatePos = targetPos;
        investigatePhase = 0; // 從原地轉向開始
        investigateWaitTimer = 2.0f; // 設定停留觀察的時間

        agent.navigator.ResetNavigation();
        agent.isWaiting = false; // 打斷巡邏的發呆
    }

    private void HandleInvestigate()
    {
        switch (investigatePhase)
        {
            case 0: // 1. 用 Arrive 行為在目前位置前方停下，並朝向刺激來源轉身
                agent.isTurningInPlace = true;
                agent.turnTargetPos = investigatePos;
                agent.targetObject = null; // 清除導航目標

                Vector3 dirToTarget = (investigatePos - transform.position);
                dirToTarget.y = 0;
                
                // 檢查是否已經轉到目標方向 (容差 5 度)
                if (Vector3.Angle(transform.forward, dirToTarget) < 5f)
                {
                    agent.isTurningInPlace = false;
                    investigatePhase = 1;
                }
                break;

            case 1: // 2. 停留幾秒鐘
                agent.isWaiting = true; // 觸發 AgentNavigator 的強制煞車
                investigateWaitTimer -= Time.deltaTime;
                
                if (investigateWaitTimer <= 0f)
                {
                    agent.isWaiting = false;
                    investigatePhase = 2;

                    // 3. 用 Seek 行為追蹤刺激來源
                    dummyTarget.transform.position = investigatePos;
                    agent.targetObject = dummyTarget.transform;
                    agent.currentState = AgentState.SEEK;
                    agent.navigator.isNavigating = true;
                }
                break;

            case 2: // 4. 到達目標位置後，若沒發現異常切回預設狀態
                Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
                Vector3 flatTarget = new Vector3(investigatePos.x, 0, investigatePos.z);

                if (Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold)
                {
                    // 抵達現場，沒看到東西 (視覺由 SensorySystem 處理，若看到會自動切換為 CHASE)
                    currentDecision = defaultDecision;
                    if (currentDecision == AgentDecision.PATROL)
                    {
                        agent.navigator.ResetNavigation();
                        ChangeRoute(defaultRouteName, -1, true); // 自動尋找最近的巡邏點恢復巡邏
                        UnityEngine.Debug.Log("Investigation complete. Returning to patrol.");
                    }
                }
                break;
        }
    }

    // --- Patrol 邏輯 (從原本的 Agent.cs 搬移過來) ---
    public void ChangeRoute(string newRouteName, int forcedStartIndex = -1, bool autoSearchStartPoint = false)
    {
        if (RouteManager.Instance == null) return;

        RouteDefinition fetchedRoute = RouteManager.Instance.GetRoute(newRouteName);
        if (fetchedRoute != null)
        {
            currentRoute = fetchedRoute;
            agent.isWaiting = false; 
            agent.navigator.ResetNavigation(); 
            agent.navigator.isNavigating = true; 
            
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
            {
                currentRouteWaypointIndex = forcedStartIndex;
            }
            else currentRouteWaypointIndex = 0;
            agent.targetObject = currentRoute.waypoints[currentRouteWaypointIndex].point;
        }
    }

    private void HandlePatrol()
    {
        if (currentRoute == null || currentRoute.waypoints.Count == 0) return;

        if (agent.isWaiting)
        {
            waitTimer -= Time.deltaTime;
            agent.targetObject = null;
            if (waitTimer <= 0f)
            {
                agent.isWaiting = false;
                currentDecision = AgentDecision.PATROL;
                agent.currentState = AgentState.SEEK;
                agent.navigator.ResetNavigation();
                agent.navigator.isNavigating = true;

                currentRouteWaypointIndex++;
                if (currentRouteWaypointIndex >= currentRoute.waypoints.Count)
                {
                    if (currentRoute.isLoop) currentRouteWaypointIndex = 0;
                    else
                    {
                        agent.currentState = AgentState.ARRIVE;
                        currentDecision = AgentDecision.REST;
                        return;
                    }
                }
                agent.targetObject = currentRoute.waypoints[currentRouteWaypointIndex].point;
            }
        }
        else
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatTarget = agent.targetObject != null ? new Vector3(agent.targetObject.position.x, 0, agent.targetObject.position.z) : Vector3.zero;

            if (agent.targetObject != null && Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold * 2)
            {
                agent.isWaiting = true;
                waitTimer = currentRoute.waypoints[currentRouteWaypointIndex].waitTime;
                currentDecision = AgentDecision.REST;
                UnityEngine.Debug.Log($"Arrived at waypoint {currentRouteWaypointIndex}. Waiting for {waitTimer} seconds.");
            }
        }

        if (!agent.isWaiting)
        {
            agent.targetObject = currentRoute.waypoints[currentRouteWaypointIndex].point;
        }
    }
}