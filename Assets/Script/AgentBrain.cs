using UnityEngine;
using KevinIglesias;
using System.Collections.Generic;
[RequireComponent(typeof(Agent))]
public class AgentBrain : MonoBehaviour
{
    private Agent agent;
    private Vector3 spawnPosition;
    private Vector3 spawnForward;
    private bool warnedMissingCoreRefs = false;
    private bool warnedMissingCombatRefs = false;
    private bool warnedMissingBulletPool = false;

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
    private float attackRange = 0f;          // 攻擊距離(小于viewRadius，確保在視野內才攻擊)
    public float aimingTime = 3.0f;       // 總瞄準時間
    public float lockTime = 2.5f;        // 射擊前的鎖定時間 (給玩家的閃避空檔)
    public float attackCooldown = 2.0f;   // 射擊後的冷卻時間

    private float combatTimer = 0f;
    private int combatPhase = 0;          // 0: 瞄準, 1: 鎖定, 2: 擊發, 3: 冷卻
    private Vector3 lockedDirection;      // 鎖定時的子彈發射方向

    // 可選：用來畫出紅外線瞄準線的視覺提示
    private LineRenderer aimLaser;
    
    [Header("Chase Settings")]
    private int chasePhase = 0;
    private float chaseTimer = 0f;
    public float reactionTime = 0.3f; // 從看到玩家到開始追逐的反應時間

    [Header("RUN / Flee / Dodge Settings")]
    public Transform escapeEndpoint;       // 擊殺目標（Target）的特定逃跑終點
    public bool runToSpecificEndpoint = false; // 是否啟用「直奔終點」模式
    public float fleeReactionTime = 0.5f; 
    public float hideSearchRadius = 25f;    // 尋找遮蔽障礙物的球體半徑
    public float distanceBehindObstacle = 2.5f; // 躲在障礙物中線後方多遠的距離

 
    // 👇 新增的 Cover Reservation 設定
    public float coverCrowdRadius = 2.0f;     // 檢查掩體周圍多大範圍內有其他 Agent
    public float crowdPenaltyWeight = 15.0f;  // 每多一個 Agent，該掩體的分數懲罰 (等同於增加 15m 的距離成本)
    private Vector3 currentDodgePoint;     // 當前計算出的視線死角盲區點
    
    private int fleePhase = 0;
    private float fleeTimer = 0f;
    


    void Awake()
    {
        agent = GetComponent<Agent>();
        agent.brain = this; // 互相綁定
        agent.navigator = GetComponent<AgentNavigator>(); // 確保 Navigator 參照正確
        agent.sensory = GetComponent<SensorySystem>(); // 確保 Sensory 參照正確

        ApplyWeaponStats();


        // 建立一個隱藏的虛擬目標，當我們只需要 Agent 前往某個座標(而非追逐特定實體)時使用
        dummyTarget = new GameObject($"DummyTarget_{gameObject.name}");
        dummyTarget.transform.SetParent(transform);

        // attackRange = agent.sensory.viewRadius * 0.6f; // 確保攻擊距離不超過視野距離，避免邏輯衝突
        
        // 如果有掛載 LineRenderer，就抓取它來做視覺提示
        TryGetComponent(out aimLaser);
        if (aimLaser != null) aimLaser.enabled = false;
    }

    void Start()
    {
        currentDecision = defaultDecision;
        spawnPosition = agent.transform.position;
        spawnForward = agent.transform.forward;
        if (currentDecision == AgentDecision.PATROL){
            ChangeRoute(defaultRouteName, startIndex, false);
        }
    }

    void OnDestroy()
    {
        if (dummyTarget != null)
        {
            Destroy(dummyTarget);
            dummyTarget = null;
        }
    }

    // 在 AgentBrain.cs 中
    void Update()
    {
        if (!CanRunBrain())
            return;

        // 1. 執行高階行為決策 (Patrol, Investigate 等)
        switch (currentDecision)
        {

            case AgentDecision.PATROL:
            case AgentDecision.SHORTREST:
                HandlePatrol();
                break;
            case AgentDecision.LONGREST:
                HandleRest();
                break;
            case AgentDecision.INVESTIGATE:
                HandleInvestigate();
                break;
            case AgentDecision.ATTACK:
                HandleAttack();
                break;
            case AgentDecision.CHASE:
                HandleChase();
                break;
            case AgentDecision.RUN:
                HandleRun();
                break;
        }
        // 確保攻擊距離不超過視野距離，避免邏輯衝突
        attackRange = agent.sensory.viewRadius * 0.6f; // 確保攻擊距離不超過視野距離，避免邏輯衝突


        // 2. 統一下達中階 Steering State
        UpdateSteeringState();
    }

    /// <summary>
    /// 核心演算法：尋找視線死角，並加入意圖預約 (Intent Reservation) 防止多名 Agent 搶奪同一個掩體
    /// </summary>
    public Vector3 CalculateDodgePoint()
    {
        // 防呆：如果沒有威脅，直接原地不動
        if (agent.threatObjects == null || agent.threatObjects.Length == 0) return transform.position;

        // 整理有效威脅，並計算所有威脅的「平均中心點」
        System.Collections.Generic.List<Vector3> validThreats = new System.Collections.Generic.List<Vector3>();
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
        threatCenter /= validThreats.Count; // 算出威脅群的幾何中心

        IReadOnlyList<Agent> allAgents = Agent.ActiveAgents;

        // 1. 掃描周圍特定 Layer 的所有物理障礙物
        Collider[] obstacles = Physics.OverlapSphere(transform.position, hideSearchRadius, agent.navigator.ObstacleLayers);

        Vector3 bestHidePos = transform.position;
        float bestScore = Mathf.Infinity; // 替換掉原本的 closestDistToAgent，改用綜合評分
        bool foundValidSpot = false;

        foreach (Collider obs in obstacles)
        {
            // 排除自身與威脅目標本身 (避免把敵人當成掩體)
            if (obs.transform == transform) continue;
            bool isThreat = false;
            foreach (Transform t in agent.threatObjects) { if (t != null && obs.transform == t) isThreat = true; }
            if (isThreat) continue;

            Vector3 obsPos = obs.transform.position;
    
            // 2. 決定障礙物的背側：從「威脅群中心點」指向「障礙物」的延伸線
            Vector3 dirFromThreatCenter = (obsPos - threatCenter).normalized;
            dirFromThreatCenter.y = 0;

            Vector3 potentialHidePos = obsPos + dirFromThreatCenter * distanceBehindObstacle;
            potentialHidePos.y = transform.position.y; // 保持在同一個高度平面
            Vector3 targetCheckPos = potentialHidePos + Vector3.up * 1.0f; // Agent 蹲下或躲藏時的身體中心高度

            // 3. 視線死角驗證 (Linecast)：對每一個敵人進行嚴格的單獨審查
            bool isValidForAllThreats = true;
            
            foreach (Vector3 specificThreatPos in validThreats)
            {
                Vector3 eyePos = specificThreatPos + Vector3.up * 1.5f;

                // 如果射線沒有撞到障礙物，代表這條視線暢通 -> 這個敵人看得到掩體後方 -> 點位失效！
                if (!Physics.Linecast(eyePos, targetCheckPos, agent.navigator.ObstacleLayers))
                {
                    isValidForAllThreats = false;
                    break; // 只要有一個敵人看得到，直接捨棄這個點，換下一顆障礙物
                }
            }

            // 4. 戰術評估：視線安全過關後，計算綜合分數 (距離 + 目的地擁擠懲罰)
            if (isValidForAllThreats)
            {
                float baseDistance = Vector3.Distance(transform.position, potentialHidePos);
                
                // --- ✨ Cover Reservation (意圖預約制) ---
                int reservedCount = 0;
                
                foreach (Agent ally in allAgents)
                {
                    // 排除自己與變成威脅的目標 (例如叛變或玩家)
                    if (ally == this.agent || isThreat) continue;

                    // 1. 意圖預約檢查：如果隊友正在移動，檢查他們的「目的地」是否在這個掩體附近
                    if (ally.targetObject != null)
                    {
                        float distToAllyTarget = Vector3.Distance(ally.targetObject.position, potentialHidePos);
                        if (distToAllyTarget < coverCrowdRadius)
                        {
                            reservedCount++;
                        }
                    }
                    // 2. 實體佔用檢查：如果隊友沒有目標(發呆或守衛中)，則檢查他們的「物理位置」
                    else
                    {
                        float distToAllyBody = Vector3.Distance(ally.transform.position, potentialHidePos);
                        if (distToAllyBody < coverCrowdRadius)
                        {
                            reservedCount++;
                        }
                    }
                }

                // 計算分數：距離越遠分數越高(越爛)，每個佔用者會大幅增加這個點的成本分數
                float score = baseDistance + (reservedCount * crowdPenaltyWeight);
                // ------------------------------------------------

                if (score < bestScore)
                {
                    bestScore = score;
                    bestHidePos = potentialHidePos;
                    foundValidSpot = true;
                }
            }
        }

        // 5. 完全防呆：如果四周空曠完全沒有任何障礙物，朝向「威脅中心點」的反方向極限延伸逃跑
        if (!foundValidSpot)
        {
            Vector3 panicDir = (transform.position - threatCenter).normalized;
            panicDir.y = 0;
            bestHidePos = transform.position + panicDir * 12f;
        }

        return bestHidePos;
    }

    /// <summary>
    /// 戰術性躲避介面：普通警衛（Guard）在被玩家瞄準或壓制時可主動調用此函式尋找掩體
    /// </summary>
    public void StartTacticalDodge(Transform shooter)
    {
        if (currentDecision == AgentDecision.RUN) return; // 已經在逃跑中則不重複觸發

        UnityEngine.Debug.Log($"{agent.Name} ({agent.agentType}) 遭到火力威脅/瞄準，觸發戰術尋找掩體！");
        runToSpecificEndpoint = false; // 警衛的目的是找掩體，而非跑去關卡終點
        StartFlee(shooter, 1); // 強制跳過發呆階段，直接進入 Phase 1 尋路進掩體
    }
    /// <summary>
    /// 感知事件接收器
    /// </summary>
    public void OnPlayerSpotted(Transform player, float distance)
    {
        if (agent.agentType ==  AgentType.CIVILIAN)
        {
            UnityEngine.Debug.Log($"{agent.Name} (Target) 发现玩家了！");
            currentDecision = AgentDecision.RUN;
            StartFlee(player);

        }
        // 如果正在 ATTACK，且正在瞄準/鎖定/開火 (Phase 0,1,2) -> 絕對不打斷，讓他射完
        if (currentDecision == AgentDecision.ATTACK && combatPhase >= 1)
        {
            return;
        }

        // 判断距离决定行为
        if (distance <= attackRange)
        {
            if (currentDecision != AgentDecision.ATTACK)
            {
                StartAttack(player);
            }
        }
        else 
        {
            if (currentDecision != AgentDecision.CHASE)
            {
                currentDecision = AgentDecision.CHASE;
                StartChase(player);
            }
        }

    }

    public void OnPlayerLost(Vector3 lastKnownPos)
    {
        if (currentDecision == AgentDecision.ATTACK)
        {
            // 選擇 A：只要躲進牆後，馬上取消瞄準去調查 (適合擬真潛行遊戲)
            if (combatPhase < 2) 
            {
                UnityEngine.Debug.Log("玩家躲入掩體，取消開火，前往調查！");
                if (aimLaser != null) aimLaser.enabled = false;
            }
            // 選擇 B：如果已經進入 Phase 1 (鎖定)，就算躲進牆後也要開火 (火力壓制)
            // (如果要選 B，請把上方的 if 條件改成 combatPhase < 1)
        }

        //转入调查模式的phase 2，直接前往玩家最後位置調查
        StartInvestigation(lastKnownPos, 2);
    }

    private void UpdateSteeringState()
    {
        agent.ClearStates(); 

        if (agent.isWaiting || agent.isTurningInPlace) return;

        // 1. 根據高階決策決定基礎移動狀態
        if (currentDecision == AgentDecision.CHASE)
        {
            agent.AddState(AgentState.PURSUE);
            agent.maxSpeed = agent.chaseSpeed;
        }
        else if (currentDecision == AgentDecision.RUN)
        {
            agent.AddState(AgentState.SEEK); // 無論哪種逃跑模式，都必須依賴全域路徑規劃（A*）繞過牆壁
            
            // 防抖與路徑修正：
            // 如果是直奔「特定終點」模式，不啟用純 FLEE 的物理排斥力，避免在接近終點窄門時被玩家的位置彈開導致卡牆。
            // 如果是「躲避掩體」模式，則開啟 FLEE 力，獲得背對玩家時的驚慌推力加成。
            if (!runToSpecificEndpoint)
            {
                agent.AddState(AgentState.FLEE); 
            }
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
                agent.ClearStates(); // 先清除原有狀態，確保 ARRIVE 不會被 SEEK/PURSUE 蓋掉
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
        if (agent.HasState(AgentState.PURSUE)) agent.maxSpeed = agent.chaseSpeed;
        else agent.maxSpeed = agent.defaultSpeed;
    }

    // --- Flee 邏輯 ---
    public void StartFlee(Transform threat, int forceStartPhase = 0)
    {
        currentDecision = AgentDecision.RUN;
        agent.animator.SetTrigger("Sprint");
        
        // 【修正】正確將威脅存入陣列，而不是覆蓋 targetObject
        agent.threatObjects = new Transform[] { threat }; 
        
        fleePhase = forceStartPhase;
        fleeTimer = 0f;

        agent.navigator.ResetNavigation();

        // 決策路徑 A：關卡目標直接衝向逃生點
        if (runToSpecificEndpoint && escapeEndpoint != null)
        {
            agent.targetObject = escapeEndpoint;
            agent.navigator.isNavigating = true;
            UnityEngine.Debug.Log($"{agent.Name} 執行終點奔跑：全速撤離至安全出口！");
        }
        // 決策路徑 B：戰術尋找死角掩體
        else
        {
            currentDodgePoint = CalculateDodgePoint();
            Transform dummyTargetTransform = GetDummyTargetTransform();
            if (dummyTargetTransform == null)
                return;

            dummyTargetTransform.position = currentDodgePoint;
            
            // 【修正】這裡才是正確設定導航目標的地方
            agent.targetObject = dummyTargetTransform;
            agent.navigator.isNavigating = true;
            UnityEngine.Debug.Log($"{agent.Name} 執行死角規避：計算隱蔽點中。");
        }

        if (forceStartPhase == 1) fleePhase = 1;
    }

    private void HandleRun()
    {
        // 模式 1：直奔特定終點 (擊殺目標解法)
        if (runToSpecificEndpoint && escapeEndpoint != null)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatTarget = new Vector3(escapeEndpoint.position.x, 0, escapeEndpoint.position.z);
            
            if (Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold)
            {
                UnityEngine.Debug.LogWarning("【遊戲結束】擊殺目標（Target）已成功逃離至終點！");
                currentDecision = AgentDecision.SHORTREST;
                agent.navigator.ResetNavigation();
            }
            return;
        }

        // 模式 2：尋找並待在視線死角掩體 (動態躲藏)
        if (agent.threatObjects == null || agent.threatObjects.Length == 0) return;
        
        switch (fleePhase)
        {
            case 0: // 反應停頓
                fleeTimer += Time.deltaTime;
                if (fleeTimer >= fleeReactionTime)
                {
                    fleePhase = 1;
                    agent.navigator.isNavigating = true;
                    fleeTimer = 0f;
                }
                break;

            case 1: // 全力進入掩體並確認安全
                fleeTimer += Time.deltaTime;
                
                // 每 0.5 秒重新評估一次玩家位置，動態更新最佳盲區點
                if (fleeTimer >= 0.5f)
                {
                    currentDodgePoint = CalculateDodgePoint();
                    Transform dummyTargetTransform = GetDummyTargetTransform();
                    if (dummyTargetTransform == null)
                    {
                        EndFlee();
                        return;
                    }

                    dummyTargetTransform.position = currentDodgePoint;
                    fleeTimer = 0f;
                }

                // 【修正】動態計算與「最近威脅」的距離，取代寫死的 threatObjects[0]
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

                // 如果所有威脅都消失了（例如被系統 Destroy），直接脫戰
                if (!hasActiveThreat)
                {
                    EndFlee();
                    return;
                }

                float distToDodgePoint = Vector3.Distance(transform.position, currentDodgePoint);

                // 安全判定：
                // 1. 順利抵達掩體點 (代表成功卡進死角)
                // 2. 距離所有威脅都超出最大視野範圍 (代表徹底甩掉敵人)
                bool isSuccessfullyHidden = distToDodgePoint < agent.navigator.waypointThreshold;
                bool isOutOfRange = closestThreatDist > agent.sensory.viewRadius;

                if (isSuccessfullyHidden || isOutOfRange)
                {
                    EndFlee();
                }
                break;
        }
    }

    /// <summary>
    /// 【統一處理脫戰邏輯，讓代碼更簡潔
    /// </summary>
    private void EndFlee()
    {
        UnityEngine.Debug.Log($"{agent.Name} 成功進入掩體死角/脫離危機，解除驚慌狀態並回歸正常邏輯。");
        agent.threatObjects = null;
        currentDecision = defaultDecision;
        
        if (currentDecision == AgentDecision.PATROL)
        {
            ChangeRoute(defaultRouteName, -1, true);
        }
        else
        {
            agent.navigator.ResetNavigation();
            agent.currentState = agent.defaultState;
        }
    }

    // --- Chase 邏輯 ---
    public void StartChase(Transform target, int forceStartPhase = 0)
    {

        currentDecision = AgentDecision.CHASE;
        agent.animator.ResetTrigger("Shoot03");
        agent.animator.SetTrigger("Sprint");

        agent.targetObject = target;
        chasePhase = forceStartPhase;
        chaseTimer = 0f;
        agent.sensory.viewAngle = 45f; // 調整視野角度，讓 Agent 在追擊時能看到更廣的範圍
        agent.isWaiting = false;

        agent.navigator.ResetNavigation();
        UnityEngine.Debug.Log("Spotted! Entering Chase Phase 0 (Reaction)");
        if (forceStartPhase == 1)
        {
            // 直接從追擊階段開始，跳過反應階段
            chasePhase = 1;
            agent.isTurningInPlace = false;
            agent.navigator.isNavigating = true;
            agent.currentState = AgentState.PURSUE;
            UnityEngine.Debug.Log("Force starting at Chase Phase 1 (Active Pursuit)");
        }

        
    }

    private void HandleChase()
    {
        if (agent.targetObject == null) return;

        switch (chasePhase)
        {
            case 0: // Phase 0 : 反應階段 (Reaction)
                agent.isTurningInPlace = true;
                agent.turnTargetPos = agent.targetObject.position;
                chaseTimer += Time.deltaTime;
                if (chaseTimer >= reactionTime)
                {
                    chasePhase = 1;
                    agent.isTurningInPlace = false;
                    agent.navigator.isNavigating = true;
                    agent.currentState = AgentState.PURSUE;
                    UnityEngine.Debug.Log("Entering Chase Phase 1: Actively chasing the player!");
                }
                break;

            case 1: // Phase 1: 全力追擊
                // 持續將 Navigator 的目標設為玩家 (確保追蹤動態目標)
                // agent.targetObject = agent.targetObject;
                
                // 距離判斷已經交給 OnPlayerSpotted 處理了
                // 只要玩家還在視野內，Sensory 就會一直呼叫 OnPlayerSpotted
                // 一旦跨越 attackRange，那邊就會自動切換 StartAttack()
                break;
        }
    }
    // --- Combat & Shooting 邏輯 ---

    /// <summary>
    /// 動態配置武器參數
    /// </summary>
    private void ApplyWeaponStats()
    {
        if (!CanUseCombatSetup())
            return;

        switch (agent.currentWeapon)
        {
            case SoldierWeapons.AssaultRifle: // 步槍：中距離、瞄準快、射速高
                attackRange = agent.sensory.viewRadius * 0.7f; aimingTime = 1.5f; lockTime = 0.7f; attackCooldown = 0.7f;
                shootPoint.localPosition = new Vector3(0.116f, 1.255f, 0.711f); // 調整槍口位置
                break;
            case SoldierWeapons.Rifle: // 狙擊槍：長距離、瞄準慢、射速慢
                attackRange = agent.sensory.viewRadius * 0.9f; aimingTime = 3.0f; lockTime = 1.0f; attackCooldown = 2.5f;
                shootPoint.localPosition = new Vector3(0.139f, 1.336f, 1.144f); // 調整槍口位置
                break;
            case SoldierWeapons.Gun: // 手槍：短距離、瞄準極快
                attackRange = agent.sensory.viewRadius * 0.5f; aimingTime = 1.0f; lockTime = 0.6f; attackCooldown = 1.0f;
                shootPoint.localPosition = new Vector3(0.19f, 1.23f, 0.667f); // 調整槍口位置
                break;
            case SoldierWeapons.Bazooka: // 火箭筒：破壞力強、前搖後搖都極長
                attackRange = agent.sensory.viewRadius * 0.8f; aimingTime = 3.5f; lockTime = 1.5f; attackCooldown = 4.0f;
                shootPoint.localPosition = new Vector3(0.191f, 1.405f, 0.842f); // 調整槍口位置
                break;
            default:
                attackRange = agent.sensory.viewRadius * 0.7f; aimingTime = 2.0f; lockTime = 0.8f; attackCooldown = 1.5f;
                shootPoint.localPosition = new Vector3(0, 1.23f, 0.47f); // 調整槍口位置
                break;
        }
        
        // 確保攻擊距離不超過感官視野，否則會出現邏輯 Bug
        if (attackRange > agent.sensory.viewRadius * 0.9f) 
            attackRange = agent.sensory.viewRadius * 0.9f;
    }

    public void StartAttack(Transform target)
    {
        if (!CanUseCombatSetup())
            return;

        currentDecision = AgentDecision.ATTACK;
        agent.isWaiting = false;
        agent.targetObject = target;
        combatPhase = 0;
        combatTimer = 0f;
        agent.sensory.viewAngle = 80f; // 調整視野角度，讓 Agent 在攻擊時能看到更廣的範圍
        
        agent.navigator.ResetNavigation(); // 戰鬥時停止尋路移動

        // 觸發舉槍瞄準動畫
        agent.animator.ResetTrigger("Sprint");
        agent.animator.SetTrigger("Shoot03");
    }

    private void HandleAttack()
    {
        if (!CanUseCombatSetup())
            return;

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
                    // UnityEngine.Debug.Log("Aiming laser activated.");
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
                    LogDebug($"{agent.name} entering lock phase 1. Player has a brief window to dodge!");
                    
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
                    LogDebug($"{agent.name} lock phase complete. Firing bullet!");
                    combatPhase = 2; // 時間到，準備擊發
                }
                // UnityEngine.Debug.Log($"Locking... Time: {combatTimer:F2}s, LockedDirection: {lockedDirection}");
                break;

            case 2: // Phase 2: 擊發階段 (Shooting)
                if (aimLaser != null) aimLaser.enabled = false; // 關閉雷射
                
                ShootBullet(lockedDirection);

                
                combatTimer = 0f;
                combatPhase = 3; // 進入冷卻
                // UnityEngine.Debug.Log("Bullet fired! Entering cooldown.");
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
                // UnityEngine.Debug.Log($"Cooling down... Time: {combatTimer:F2}s");
                break;
        }
    }

    private void ShootBullet(Vector3 direction)
    {
        if (!CanUseCombatSetup())
            return;

        if (BulletPool.Instance == null)
        {
            if (!warnedMissingBulletPool)
            {
                warnedMissingBulletPool = true;
                UnityEngine.Debug.LogWarning($"[AgentBrain] {agent.name} cannot shoot because BulletPool is missing.");
            }

            return;
        }

        agent.animator.SetTrigger("Shoot01");
        // 改成跟 Pool 借子彈：
        GameObject bullet = BulletPool.Instance.GetBullet(shootPoint.position, Quaternion.LookRotation(direction));

        if (bullet == null)
            return;
        
        if (bullet.TryGetComponent(out SimpleProjectile projectile))
        {
            projectile.Fire(direction, false);
        }
        
        LogDebug($"{agent.name} Bang! Fired pooled bullet at locked direction.");
    }

    private bool CanRunBrain()
    {
        if (agent != null && agent.navigator != null && agent.sensory != null)
            return true;

        if (!warnedMissingCoreRefs)
        {
            warnedMissingCoreRefs = true;
            UnityEngine.Debug.LogWarning($"[AgentBrain] {name} is missing Agent, AgentNavigator, or SensorySystem; brain update is disabled.");
        }

        return false;
    }

    private bool CanUseCombatSetup()
    {
        if (agent != null && agent.sensory != null && agent.animator != null && shootPoint != null)
            return true;

        if (!warnedMissingCombatRefs)
        {
            warnedMissingCombatRefs = true;
            UnityEngine.Debug.LogWarning($"[AgentBrain] {name} is missing SensorySystem, Animator, or shootPoint; combat setup is disabled.");
        }

        return false;
    }

    private void LogDebug(string message)
    {
        if (agent == null || !agent.showDebugLogs)
            return;

        UnityEngine.Debug.Log($"[AgentBrain] {message}");
    }

    private Transform GetDummyTargetTransform()
    {
        if (dummyTarget != null)
            return dummyTarget.transform;

        UnityEngine.Debug.LogWarning($"[AgentBrain] {name} is missing its dummy target; navigation request was skipped.");
        return null;
    }

    // --- Investigate 邏輯 ---
    public void StartInvestigation(Vector3 targetPos, int forceStartPhase = 0)
    {
        currentDecision = AgentDecision.INVESTIGATE;
        agent.animator.SetTrigger("Sprint");
        
        investigatePos = targetPos;
        investigatePhase = forceStartPhase;
        investigateWaitTimer = 2.0f; // 設定停留觀察的時間
        
        agent.sensory.viewAngle = 120f; // 調整視野角度，讓 Agent 在調查時能看到更廣的範圍

        agent.navigator.ResetNavigation();
        agent.isWaiting = false; // 打斷巡邏的發呆

        

        // 直接從停留觀察開始，跳過轉向階段
        if (forceStartPhase == 1)
        {
            // 直接從停留觀察開始，跳過轉向階段
            agent.isTurningInPlace = false;
            // investigateWaitTimer = 0f; // 跳過停留，直接進入追蹤
        }
        // 直接從追蹤開始，跳過轉向和停留階段
        else if (forceStartPhase == 2)
        {
            agent.isTurningInPlace = false;
            Transform dummyTargetTransform = GetDummyTargetTransform();
            if (dummyTargetTransform == null)
                return;

            dummyTargetTransform.position = investigatePos;
            agent.targetObject = dummyTargetTransform;
            agent.currentState = AgentState.SEEK;
            agent.navigator.isNavigating = true;
            LogDebug("Investigation Phase 2: Moving to investigate position.");
        }
        
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
                    LogDebug("Investigation Phase 1");
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
                    Transform dummyTargetTransform = GetDummyTargetTransform();
                    if (dummyTargetTransform == null)
                        return;

                    dummyTargetTransform.position = investigatePos;
                    agent.targetObject = dummyTargetTransform;
                    agent.currentState = AgentState.SEEK;
                    agent.navigator.isNavigating = true;
                    LogDebug("Investigation Phase 2: Moving to investigate position.");
                }
                break;

            case 2: // 4. 到達目標位置後，若沒發現異常切回預設狀態
                Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
                Vector3 flatTarget = new Vector3(investigatePos.x, 0, investigatePos.z);

                if (Vector3.Distance(flatPos, flatTarget) < agent.navigator.waypointThreshold)
                {
                    
                        
                    if (defaultDecision == AgentDecision.PATROL)
                    {
                        currentDecision = defaultDecision;
                        agent.navigator.ResetNavigation();
                        ChangeRoute(defaultRouteName, -1, true); 
                    }
                    // 👇  LONGREST 的回家邏輯
                    else if (defaultDecision == AgentDecision.LONGREST)
                    {
                        Vector3 flatSpawn = new Vector3(spawnPosition.x, 0, spawnPosition.z);
                        
                        // 檢查是否已經抵達出生點附近 (給予一點容錯空間)
                        if (Vector3.Distance(flatPos, flatSpawn) < agent.navigator.waypointThreshold * 2f)
                        {
                            // 已經在出生點了，直接切回 LONGREST
                            currentDecision = defaultDecision;
                            agent.navigator.ResetNavigation();
                            agent.currentState = agent.defaultState;
                            return; // ⚠️ 這裡必須 return，否則會繼續執行下面的 StartInvestigation
                        }

                        // 如果還沒抵達出生點 (代表剛結束第一次的聲音/視線調查)，則開啟尋路走回家
                        StartInvestigation(spawnPosition, 2); 
                    }
                }
                break;
        }
    }

    // --- Patrol 邏輯 (從原本的 Agent.cs 搬移過來) ---
    // 等同于startPatrol()，但增加了強制指定路線和起點的功能，以及自動尋找最近巡邏點的選項
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
            agent.currentState = AgentState.SEEK;
            
            agent.sensory.viewAngle = 45f;
            
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
                        currentDecision = AgentDecision.SHORTREST;
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
                
                if (defaultDecision == AgentDecision.PATROL)
                {
                    waitTimer = currentRoute.waypoints[currentRouteWaypointIndex].waitTime;
                    currentDecision = AgentDecision.SHORTREST;
                    LogDebug($"Arrived at waypoint {currentRouteWaypointIndex}. Waiting for {waitTimer} seconds.");
                }
                else if (defaultDecision == AgentDecision.LONGREST)
                {
                    waitTimer = 999f; // 無限等待，直到被外部事件打斷（例如玩家靠近觸發調查）
                    currentDecision = AgentDecision.LONGREST;
                    LogDebug("Arrived at rest point. Waiting indefinitely until disturbed.");
                }
                
            }
        }

        if (!agent.isWaiting)
        {
            agent.targetObject = currentRoute.waypoints[currentRouteWaypointIndex].point;
        }
    }

    private void HandleRest()
    {
        agent.ClearStates();
        agent.targetObject = null;
        agent.isWaiting = true;

        // 若是 LONGREST，檢查並執行轉向初始角度
        if (currentDecision == AgentDecision.LONGREST)
        {
            Vector3 targetDir = spawnForward;
            targetDir.y = 0;
            
            // 檢查是否還沒對齊初始角度 (容許 2 度的微小誤差防抖)
            if (Vector3.Angle(transform.forward, targetDir) > 2f)
            {
                agent.isTurningInPlace = true;
                // 設定一個在正前方遠處的虛擬目標點，引導 AgentLocomotion 平滑轉向
                agent.turnTargetPos = transform.position + targetDir * 5f; 
            }
            else
            {
                // 轉向完成，解除轉向標籤
                agent.isTurningInPlace = false;
            }
        }
    }
    
    private void OnDrawGizmos()
    {
        if (agent == null)
            agent = GetComponent<Agent>();

        if (agent != null && !agent.showDebugGizmos)
            return;

        // 可視化調查點
        if (currentDecision == AgentDecision.INVESTIGATE)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(investigatePos, 0.5f);   
        }
        // 可视化攻击距离
        if (currentDecision == AgentDecision.ATTACK)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
        if (currentDecision == AgentDecision.RUN && !runToSpecificEndpoint)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(currentDodgePoint, 0.6f);
            Gizmos.DrawLine(transform.position, currentDodgePoint);
        }
        
    }
}
