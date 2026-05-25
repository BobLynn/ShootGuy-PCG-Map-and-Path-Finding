using System.Collections.Generic;
using System.Diagnostics;
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

    void Start()
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

        if (distanceToSound <= stimulus.radius)
        {
            UnityEngine.Debug.Log($"{gameObject.name} 聽到了 {stimulus.type}!");

            // targetInvestigatePos = stimulus.position;
            // hasSuspiciousStimulus = true;

            // 呼叫 Brain 正式進入 Investigate 流程
            if (!canSeePlayer) // 如果当前没有看到玩家，才去调查声音来源
            {
                agent.brain.StartInvestigation(stimulus.position);
            }
        }
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
        bool canSeePlayer = false;

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
                    canSeePlayer = true;

                    agent.brain.OnPlayerSpotted(target, dstToTarget);
                    break;
                }
            }
        }
        if (!canSeePlayer && agent.brain.currentDecision == AgentDecision.ATTACK)
        {
            canSeePlayer = false;
            UnityEngine.Debug.Log($"{gameObject.name} 失去了玩家的視線...");
            agent.brain.OnPlayerLost(lastKnownPlayerPos);
        }
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
}