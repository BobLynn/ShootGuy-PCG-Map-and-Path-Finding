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

        // 1. ✨ 確保自己在聲音廣播的物理範圍內 (因為 Broadcast 會發給所有人)
        if (distanceToSound > stimulus.radius) return;

        // 2. ✨ 防呆：略過自己發出的聲音
        if (stimulus.source == this.gameObject) return;

        // ✨ 修正：感官系統只負責「聽到」，將情報原封不動上報給大腦
        // 絕對不參與 currentDecision 的狀態檢查！
        agent.brain.OnAudioHeard(stimulus);
    }

    // ==========================================
    // 視覺系統 (Polling) 
    // ==========================================
    void Update()
    {
        FieldOfViewCheck();
    }

    private void FieldOfViewCheck()
    {
        bool currentCanSee = false;

        // 1. 先用球體重疊 (OverlapSphere) 找出範圍內所有的 Target (例如玩家)
        Collider[] targetsInViewRadius = Physics.OverlapSphere(transform.position, viewRadius, targetMask);

        for (int i = 0; i < targetsInViewRadius.Length; i++)
        {
            Transform target = targetsInViewRadius[i].transform;
            
            Vector3 dirToTarget = (target.position - transform.position).normalized;

            // 2. 檢查角度：目標是否在我的視野錐體內？
            if (Vector3.Angle(transform.forward, dirToTarget) < viewAngle / 2)
            {
                float dstToTarget = Vector3.Distance(transform.position, target.position);

                // 3. 檢查視線阻擋 (Line of Sight)：我跟玩家之間有沒有牆壁？
                Vector3 eyePos = transform.position + Vector3.up * 1.5f;
                Vector3 targetCenter = target.position + Vector3.up * 1.0f;

                if (!Physics.Linecast(eyePos, targetCenter, obstacleMask))
                {
                    // 看到玩家了！
                    lastKnownPlayerPos = target.position;
                    currentCanSee = true;

                    agent.brain.OnPlayerSpotted(target, dstToTarget);
                    break;
                }
            }
        }

        // ✨ 修正：狀態改變檢查，只負責回報「丟失視線」，不插手檢查 AgentBrain 的狀態
        if (canSeePlayer && !currentCanSee)
        {
            // UnityEngine.Debug.Log($"{gameObject.name} 失去了玩家的視線...");
            agent.brain.OnPlayerLost(lastKnownPlayerPos);
        }
 
        canSeePlayer = currentCanSee;
    }

    // ==========================================
    // Visual Debugging (Telemetry)
    // ==========================================
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        Vector3 viewAngle01 = DirFromAngle(transform.eulerAngles.y, -viewAngle / 2);
        Vector3 viewAngle02 = DirFromAngle(transform.eulerAngles.y, viewAngle / 2);
        UnityEngine.Vector3 eyePos = transform.position + Vector3.up * 1.5f; 
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(eyePos, eyePos + viewAngle01 * viewRadius);
        Gizmos.DrawLine(eyePos, eyePos + viewAngle02 * viewRadius);

        if (canSeePlayer)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(eyePos, lastKnownPlayerPos + Vector3.up * 1.0f); 
        }
    }

    private Vector3 DirFromAngle(float eulerY, float angleInDegrees)
    {
        angleInDegrees += eulerY;
        return new Vector3(Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(angleInDegrees * Mathf.Deg2Rad));
    }
}