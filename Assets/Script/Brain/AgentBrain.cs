using UnityEngine;
using KevinIglesias;
using System.Collections;
using System.Security.Cryptography.X509Certificates;


[RequireComponent(typeof(Agent))]
[RequireComponent(typeof(AgentActionController))] // 確保自動掛載執行器
public class AgentBrain : MonoBehaviour
{
    private Agent agent;
    [HideInInspector] public AgentActionController actionController;

    [Header("Current Decision (FSM)")]
    public AgentDecision currentDecision = AgentDecision.NONE;
    public AgentDecision defaultDecision = AgentDecision.PATROL;
    
    // ✨ 大腦專屬記憶：記錄上一個狀態，方便未來做「打斷後恢復」
    public AgentDecision previousDecision { get; private set; } = AgentDecision.PATROL;

    [Header("Advanced Tactics (Multi-Agent)")]
    public bool enableAdvancedTactics = true;
    public LayerMask allyLayer;             
    public float squadCommRadius = 30f;     
    [HideInInspector] public bool isSuppressing = false; 
    private float lastAlertTime = 0f;       

    void Awake()
    {
        agent = GetComponent<Agent>();
        agent.brain = this;
        actionController = GetComponent<AgentActionController>();
    }

    void Start()
    {
        // 初始路由載入 (但不啟動物理，由 PATROL 狀態負責啟動)
        if (defaultDecision == AgentDecision.PATROL)
        {
            actionController.ChangeRoute(actionController.defaultRouteName, actionController.startIndex, false);
        }
        
        // 觸發初始狀態
        currentDecision = AgentDecision.NONE; // 先清空，避免與 defaultDecision 相同而不觸發
        ChangeDecision(defaultDecision);
    }

    void Update()
    {
        if (agent.health <= 0f) return;

        // 更新給隊友看的戰術標籤
        isSuppressing = (currentDecision == AgentDecision.ATTACK && 
                        (actionController.combatPhase == 1 || actionController.combatPhase == 2));

        if (currentDecision != AgentDecision.ATTACK)
        {
            agent.isStrafing = false;
        }

        // ==========================================
        // ✨ 向執行器下達命令，並取得執行狀態報告
        // ==========================================
        ActionStatus status = actionController.ExecuteAction(currentDecision);

        // 如果執行器報告「任務完成」，大腦負責決定下一步
        if (status == ActionStatus.Success)
        {
            HandleActionSuccess();
        }
    }

    // ==========================================
    // ✨ 核心 FSM 狀態轉換中心 (Centralized State Transition)
    // ==========================================
    public void ChangeDecision(AgentDecision newDecision, Transform target = null, Vector3 targetPos = default, int forcePhase = 0)
    {
        if (currentDecision == newDecision && newDecision != AgentDecision.INVESTIGATE && newDecision != AgentDecision.RUN) 
            return; 

        previousDecision = currentDecision;
        currentDecision = newDecision;

        UnityEngine.Debug.Log($"[{agent.name}] Decision Changed: {previousDecision} -> {currentDecision}");

        // 呼叫 ActionController 進行該狀態的「進入(Enter)」初始化
        switch (currentDecision)
        {
            case AgentDecision.PATROL:
                actionController.StartPatrol();
                break;
            case AgentDecision.SHORTREST:
                actionController.StartShortRest();
                break;
            case AgentDecision.LONGREST:
                actionController.StartLongRest();
                break;
            case AgentDecision.ATTACK:
                actionController.StartAttack(target);
                break;
            case AgentDecision.CHASE:
                actionController.StartChase(target, forcePhase);
                break;
            case AgentDecision.RUN:
                actionController.StartFlee(target, forcePhase);
                break;
            case AgentDecision.INVESTIGATE:
                actionController.StartInvestigation(targetPos, forcePhase);
                break;
        }
    }

    // ==========================================
    // ✨ 狀態切換邏輯樹 (FSM Logic Tree)
    // ==========================================
    private void HandleActionSuccess()
    {
        switch (currentDecision)
        {
            case AgentDecision.PATROL:
                // 巡邏抵達節點 -> 短休息
                ChangeDecision(AgentDecision.SHORTREST);
                break;
                
            case AgentDecision.SHORTREST:
                // 短休息結束 -> 推進節點，繼續巡邏，若沒節點了就長休息
                if (actionController.AdvanceWaypoint()) {
                    ChangeDecision(AgentDecision.PATROL);
                } else {
                    ChangeDecision(AgentDecision.LONGREST); 
                }
                break;

            case AgentDecision.INVESTIGATE:
                // 調查結束 -> 回到預設狀態，或者如果是長休息則先返家
                if (defaultDecision == AgentDecision.LONGREST && !actionController.IsAtSpawn()) {
                    ChangeDecision(AgentDecision.INVESTIGATE, null, actionController.spawnPosition, 2);
                    Debug.Log($"{agent.name} 調查結束，先返回出生點休息...");
                } else {
                    ChangeDecision(defaultDecision);
                }
                break;

            case AgentDecision.RUN:
                // 逃跑/躲避成功 -> 視情況躲藏或回歸
                if (actionController.runToSpecificEndpoint) {
                    ChangeDecision(AgentDecision.LONGREST);
                    StartCoroutine(agent.DelayDisappear());
                } else {
                    ChangeDecision(defaultDecision);
                }
                break;
        }
    }

    // ==========================================
    // 感知事件接收 (Perception Callbacks)
    // ==========================================
    public void StartTacticalDodge(Transform shooter)
    {
        if (currentDecision == AgentDecision.RUN) return; 

        if (enableAdvancedTactics && CheckAllySuppressing())
        {
            UnityEngine.Debug.Log($"【交替掩護】{agent.name} 發現隊友正在火力掩護，果斷衝鋒！");
            ChangeDecision(AgentDecision.CHASE, shooter, default, 0);
            return;
        }

        UnityEngine.Debug.Log($"{agent.name} ({agent.agentType}) 觸發戰術尋找掩體！");
        ChangeDecision(AgentDecision.RUN, shooter, default, 1);
    }

    public void OnPlayerSpotted(Transform player, float distance)
    {
        if (!player.GetComponent<Player>().isTrespassing && !agent.playerfounded) return;
        agent.playerfounded = true;
        if (currentDecision == AgentDecision.RUN) return; 
        
        if (agent.agentType == AgentType.TARGET && currentDecision != AgentDecision.RUN)
        {
            ChangeDecision(AgentDecision.RUN, player, default, 0);
            return;
        }

        if (currentDecision == AgentDecision.ATTACK && actionController.combatPhase >= 1) return;


        if (distance <= actionController.attackRange)
        {
            if (currentDecision != AgentDecision.ATTACK) ChangeDecision(AgentDecision.ATTACK, player);
        }
        else 
        {
            if (currentDecision != AgentDecision.CHASE) ChangeDecision(AgentDecision.CHASE, player, default, 0);
        }
    }

    public void OnPlayerLost(Vector3 lastKnownPos)
    {
        if (currentDecision == AgentDecision.ATTACK)
        {
            if (actionController.combatPhase < 2) 
            {
                actionController.DisableAimLaser();
            }
            ChangeDecision(AgentDecision.CHASE, actionController.realTarget, default, 1);
            return;
        }
        if (currentDecision == AgentDecision.RUN) return;
        if (agent.playerfounded){
            ChangeDecision(AgentDecision.INVESTIGATE, null, lastKnownPos, 2);
        }
    }

    // ==========================================
    // ✨ 新增：接收來自感官系統的聲音情報
    // ==========================================
    public void OnAudioHeard(AudioStimulus stimulus)
    {
        if (stimulus.type == "Gunshot" && stimulus.source != null)
        {
            // 聽到槍聲：立刻將開槍者視為威脅，尋找掩體躲避！
            if (currentDecision != AgentDecision.ATTACK && 
                currentDecision != AgentDecision.RUN)
            {
                StartTacticalDodge(stimulus.source.transform);
            }
        }
        else if (stimulus.type == "TacticalAlert" && stimulus.target != null)
        {
            // 收到隊友的戰術大喊
            OnTacticalAlertReceived(stimulus.target.transform);
        }
        else
        {
            // 聽到普通聲音 (例如腳步聲、硬幣聲)：前往調查
            if (!agent.sensory.canSeePlayer) 
            {
                // 統一透過 ChangeDecision 切換狀態
                ChangeDecision(AgentDecision.INVESTIGATE, null, stimulus.position, 0);
            }
        }
    }

    // ==========================================
    // ✨ 新增：處理戰術警報的決策邏輯
    // ==========================================
    private void OnTacticalAlertReceived(Transform threat)
    {
        // 如果自己已經在戰鬥、追逐或逃跑，就忽略警報 (不隨便打斷正在執行的重要決策)
        if (currentDecision == AgentDecision.ATTACK ||
            currentDecision == AgentDecision.CHASE ||
            currentDecision == AgentDecision.RUN ||
            currentDecision == AgentDecision.INVESTIGATE)
        {
            return; 
        }

        // 根據自己的角色 (AgentType) 做出專屬的戰術反應
        if (agent.agentType == AgentType.GUARD)
        {
            UnityEngine.Debug.Log($"【戰術接收】{agent.name} 收到隊友警報，前往支援調查！");
            ChangeDecision(AgentDecision.INVESTIGATE, null, threat.position, 2); 
        }
        else if (agent.agentType == AgentType.TARGET)
        {
            UnityEngine.Debug.Log($"【戰術接收】{agent.name} 收到保鏢警報，緊急撤離！");
            ChangeDecision(AgentDecision.RUN, threat, default, 1); 
        }
    }

    public void AlertAllies(Transform threat)
    {
        if (Time.time - lastAlertTime < 2.0f) return; 
        lastAlertTime = Time.time;
        if (StimulusManager.Instance != null)
        {
            StimulusManager.Instance.BroadcastAudioStimulus(
                transform.position, squadCommRadius, "TacticalAlert", this.gameObject, threat.gameObject);
        }
    }

    private bool CheckAllySuppressing()
    {
        Collider[] allies = Physics.OverlapSphere(transform.position, squadCommRadius, allyLayer);
        foreach (Collider col in allies)
        {
            if (col.transform == this.transform) continue;
            AgentBrain allyBrain = col.GetComponent<AgentBrain>();
            if (allyBrain != null && allyBrain.isSuppressing) return true;
        }
        return false;
    }
}