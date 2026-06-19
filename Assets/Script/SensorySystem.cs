using UnityEngine;

[RequireComponent(typeof(Agent))] // 確保跟你的 Agent 綁定在一起
public class SensorySystem : MonoBehaviour
{
    [Header("Vision Settings")]
    public float viewRadius = 20f;           // 視野距離
    [Range(0, 360)] public float viewAngle = 90f; // 視野角度 (錐體)
    public LayerMask targetMask;             // 誰是目標？(例如 Player Layer)
    public LayerMask obstacleMask;           // 什麼會擋住視線？(牆壁、障礙物)

    [Header("Current Perception State")]
    public bool canSeePlayer = false;
    public Vector3 lastKnownPlayerPos;
    
    [Header("Memory / Brain Interface")]
    // public Vector3 targetInvestigatePos;     // 聽到聲音後要去調查的地點
    // public bool hasSuspiciousStimulus = false;

    private Agent agent;

    void Awake()
    {
        agent = GetComponent<Agent>();
    }

    // ==========================================
    // 聽覺系統 (Event-Driven) 
    // ==========================================
    private void OnEnable()
    {
        // 訂閱全域聲音事件
        StimulusManager.OnAudioStimulusCreated += HearAudioStimulus;
    }

    private void OnDisable()
    {
        // 取消訂閱 (避免 Agent 死亡後還在聽聲音導致報錯)
        StimulusManager.OnAudioStimulusCreated -= HearAudioStimulus;
    }

    private void HearAudioStimulus(AudioStimulus stimulus)
    {
        float distanceToSound = Vector3.Distance(transform.position, stimulus.position);

        // 根據聲音類型做出不同的戰術反應
        if (stimulus.type == "Gunshot" && stimulus.source != null)
        {
            // 聽到槍聲：立刻將開槍者視為威脅，尋找掩體躲避！
            // 條件：只有在還沒進入戰鬥開火，或是還沒在逃跑時才觸發 (避免一直打斷當前動作)
            if (agent.brain.currentDecision != AgentDecision.ATTACK && 
                agent.brain.currentDecision != AgentDecision.RUN)
            {
                // 把聲音來源 (Player) 傳給大腦，強制啟動戰術規避
                agent.brain.StartTacticalDodge(stimulus.source.transform);
            }
        }
        else
        {
            // 聽到普通聲音 (例如腳步聲、硬幣聲)：前往調查
            if (!canSeePlayer) 
            {
                agent.brain.StartInvestigation(stimulus.position);
            }
        }
        // 玩家開搶時呼叫下面這行來廣播聲音刺激 (記得把 playerObject 換成你的玩家物件參照)
        // StimulusManager.Instance.BroadcastAudioStimulus(pos, radius, "Gunshot", playerObject);
    }

    // ==========================================
    // 視覺系統 (Polling) 
    // ==========================================
    void Update()
    {
        // 為了效能，實務上通常會用 Coroutine 每 0.1 秒檢查一次，而不是放在 Update 裡每幀算
        FieldOfViewCheck();
    }

    private void FieldOfViewCheck()
    {
        bool sawPlayer = false;

        // 1. 先用球體重疊 (OverlapSphere) 找出範圍內所有的 Target (例如玩家)
        Collider[] targetsInViewRadius = Physics.OverlapSphere(transform.position, viewRadius, targetMask);

        for (int i = 0; i < targetsInViewRadius.Length; i++)
        {
            Transform target = targetsInViewRadius[i].transform;
            
            // 計算目標方向
            Vector3 dirToTarget = (target.position - transform.position).normalized;

            // 2. 檢查角度：目標是否在我的視野錐體內？
            if (Vector3.Angle(transform.forward, dirToTarget) < viewAngle / 2)
            {
                // UnityEngine.Debug.Log($"{gameObject.name} 發現了 {target.name} 在視野範圍內!");
                float dstToTarget = Vector3.Distance(transform.position, target.position);

                // 3. 檢查視線阻擋 (Line of Sight)：我跟玩家之間有沒有牆壁？
                // 為了避免射線掃到地板，通常會加上 Agent 身高的一半作為眼高
                Vector3 eyePos = transform.position + Vector3.up * 1.5f;
                Vector3 targetCenter = target.position + Vector3.up * 1.0f;

                if (!Physics.Linecast(eyePos, targetCenter, obstacleMask))
                {
                    // 看到玩家了！
                    lastKnownPlayerPos = target.position;
                    sawPlayer = true;

                    agent.brain.OnPlayerSpotted(target, dstToTarget);
                    break;
                }
            }
        }
        if (!sawPlayer && canSeePlayer && agent.brain.currentDecision == AgentDecision.ATTACK)
        {
            LogDebug($"{gameObject.name} 失去了玩家的視線...");
            agent.brain.OnPlayerLost(lastKnownPlayerPos);
        }

        canSeePlayer = sawPlayer;
    }

    // ==========================================
    // Visual Debugging (Telemetry)
    // ==========================================
    private void OnDrawGizmos()
    {
        // 畫出視野距離圓圈
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        // 畫出視線錐體的兩條邊界線
        Vector3 viewAngle01 = DirFromAngle(transform.eulerAngles.y, -viewAngle / 2);
        Vector3 viewAngle02 = DirFromAngle(transform.eulerAngles.y, viewAngle / 2);
        UnityEngine.Vector3 eyePos = transform.position + Vector3.up * 1.5f; // 眼高位置
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(eyePos, eyePos + viewAngle01 * viewRadius);
        Gizmos.DrawLine(eyePos, eyePos + viewAngle02 * viewRadius);

        // 如果看到玩家，把視野線畫成紅色
        if (canSeePlayer)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(eyePos, lastKnownPlayerPos + Vector3.up * 1.0f); // 畫到玩家中心位置
        }
    }

    // 輔助數學函數：將角度轉換為方向向量 (供 Gizmos 畫圖使用)
    private Vector3 DirFromAngle(float eulerY, float angleInDegrees)
    {
        angleInDegrees += eulerY;
        return new Vector3(Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(angleInDegrees * Mathf.Deg2Rad));
    }

    private void LogDebug(string message)
    {
        if (agent == null || !agent.showDebugLogs)
            return;

        UnityEngine.Debug.Log($"[SensorySystem] {message}");
    }
}
