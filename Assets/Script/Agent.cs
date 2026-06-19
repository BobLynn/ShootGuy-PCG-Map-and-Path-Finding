using UnityEngine;
using KevinIglesias;
using System.Collections; //Required for IEnumerator and Coroutines
using System.Collections.Generic;

[System.Flags]
public enum AgentState 
{ 
    NONE = 0, 
    SEEK = 1 << 0,     // 1
    ARRIVE = 1 << 1,   // 2
    PURSUE = 1 << 2,    // 4
    WANDER = 1 << 3,   // 8  (預留給未來擴充)
    FLEE = 1 << 4,      // 16 (預留給未來擴充)
    EVADE = 1 << 5     // 32 (預留給未來擴充)
}
// public enum AgentState { NONE, CHASE, ARRIVE, SEEK} 
public enum AgentDecision { LONGREST,SHORTREST, PATROL, CHASE, INVESTIGATE, RUN, ATTACK }

public enum AgentType {GUARD, CIVILIAN, TARGET}
[RequireComponent(typeof(CharacterController))] // 確保物件上有 CharacterController
[RequireComponent(typeof(AgentLocomotion))]
[RequireComponent(typeof(AgentNavigator))]
[RequireComponent(typeof(AgentBrain))]
[RequireComponent(typeof(SensorySystem))]
public class Agent : MonoBehaviour
{
    private static readonly List<Agent> activeAgents = new List<Agent>();
    public static IReadOnlyList<Agent> ActiveAgents => activeAgents;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveAgents()
    {
        activeAgents.Clear();
    }

    public string Name = "Agent";
    public AgentType agentType = AgentType.GUARD;

    [Header("Debug")]
    public bool showDebugLogs = false;

    [Header("Target & Strategy")]
    public Transform targetObject;
    public Transform[] threatObjects; // 可能的威脅來源（例如玩家、爆炸點等）
    public AgentState defaultState = AgentState.SEEK;
    public AgentState currentState = AgentState.SEEK;

    [Header("Agent Stats")]
    public float health = 100f;
    public float defaultSpeed = 5f;
    public float chaseSpeed = 8f;
    public float maxForce = 10f;

    [Header("Weapon & Animation")]
    private SoldierWeapons previousWeapon = SoldierWeapons.AssaultRifle; // 讓你在 Inspector 選擇武器
    private SoldierPosition previousPosition = SoldierPosition.StandUp;
    private SoldierAction previousAction = SoldierAction.Nothing;
    private SoldierMovement previousMovement = SoldierMovement.NoMovement;


    // 用來記錄當前動畫狀態，防止每幀瘋狂發送 Trigger
    public SoldierMovement currentMovement = SoldierMovement.NoMovement;
    public SoldierWeapons currentWeapon = SoldierWeapons.None;
    public SoldierPosition currentPosition = SoldierPosition.StandUp;
    public SoldierAction currentAction = SoldierAction.Nothing;
    [HideInInspector] public HumanSoldierController soldierController;
    

    [Header("Hub Interface (跨元件通訊)")]
    [HideInInspector] public AgentLocomotion locomotion; 
    [HideInInspector] public AgentNavigator navigator;
    [HideInInspector] public AgentBrain brain; // 新增 Brain 參照
    [HideInInspector] public SensorySystem sensory; // 新增 SensorySystem 參照
    

    [HideInInspector] public float maxSpeed;             
    [HideInInspector] public Vector3 steeringForce;      
    [HideInInspector] public bool wantsToJump = false;   

    [Header("Behavior Flags")]
    public bool isWaiting = false;
    public bool isTurningInPlace = false; // 原地轉向標籤
    public Vector3 turnTargetPos;         // 原地轉向的目標點

    public Vector3 velocity
    {
        get
        {
            if (locomotion != null)
                return locomotion.velocity;

            if (!warnedMissingLocomotion)
            {
                warnedMissingLocomotion = true;
                Debug.LogWarning($"[Agent] {name} is missing AgentLocomotion; velocity defaults to zero.");
            }

            return Vector3.zero;
        }
    }

    [HideInInspector] public Animator animator; // 新增 Animator 參照
    private bool warnedMissingLocomotion = false;
    private bool warnedMissingAnimationRefs = false;
    private bool warnedMissingBrain = false;

    void OnEnable()
    {
        if (!activeAgents.Contains(this))
        {
            activeAgents.Add(this);
        }
    }

    void OnDisable()
    {
        activeAgents.Remove(this);
    }

    void Start()
    {
        locomotion = GetComponent<AgentLocomotion>();
        navigator = GetComponent<AgentNavigator>();
        brain = GetComponent<AgentBrain>(); // 確保掛載 Brain
        sensory = GetComponent<SensorySystem>(); // 確保掛載 SensorySystem

        // 綁定並初始化武器模型
        soldierController = GetComponent<HumanSoldierController>();
        animator = GetComponent<Animator>();
        if (soldierController != null)
        {
            previousWeapon = currentWeapon ; 
            previousMovement = currentMovement; 
            previousPosition = currentPosition;
            previousAction = currentAction;

            soldierController.equippedWeapon = currentWeapon; // 設定當前武器狀態
            soldierController.position = currentPosition; // 設定當前位置狀態
            soldierController.action = currentAction; // 設定當前動作狀態
            soldierController.movement = currentMovement; // 設定當前移動狀態

            // soldierController.enabled = false; // 關閉它原有的 Update 迴圈，由 AI 接管控制權

            soldierController.ChangeWeapon(currentWeapon); // 顯示正確的武器模型

        }
        maxSpeed = defaultSpeed;
        currentState = defaultState;

        
    }
    public void AddState(AgentState state) => currentState |= state;
    public void RemoveState(AgentState state) => currentState &= ~state;
    public bool HasState(AgentState state) => (currentState & state) == state;
    public void ClearStates() => currentState = AgentState.NONE;

    void Update()
    {
        if (!CanUpdateAnimationState())
            return;

        // 下面宣告的都是執行動畫時必要的常駐動畫參數，必須保持trigger才可以順利正確呼叫其他動畫
        if (currentWeapon != previousWeapon)
        {
            animator.ResetTrigger(previousWeapon.ToString()); // 重置之前武器的 Trigger，避免動畫卡死
            previousWeapon = currentWeapon;
            soldierController.equippedWeapon = currentWeapon; // 觸發正確的武器動畫
            soldierController.ChangeWeapon(currentWeapon);
        }

        if (currentPosition != previousPosition)
        {
            animator.ResetTrigger(previousPosition.ToString()); // 重置之前位置的 Trigger，避免動畫卡死
            previousPosition = currentPosition;
            soldierController.position = currentPosition; // 觸發正確的位置動畫
        }

         if (currentAction != previousAction)
        {
            animator.ResetTrigger(previousAction.ToString()); // 重置之前動作的 Trigger，避免動畫卡死
            previousAction = currentAction;
            soldierController.action = currentAction; // 觸發正確的動作動畫
        }

        if (currentMovement != previousMovement)
        {
            animator.ResetTrigger(previousMovement.ToString()); // 重置之前移動的 Trigger，避免動畫卡死
            previousMovement = currentMovement;
            soldierController.movement = currentMovement; // 觸發正確的移動動畫
        }
        // animator.SetTrigger(currentWeapon.ToString()); // 觸發正確的武器動畫
        // animator.SetTrigger(currentPosition.ToString()); // 觸發正確的位置動畫
        // animator.SetTrigger(currentAction.ToString()); // 觸發正確的動作動畫
        // animator.SetTrigger(currentMovement.ToString()); // 觸發正確的移動

    }

    public void TakeDamage(float damage, Vector3 damageSourcePos = default(Vector3))
    {
        health -= damage;

        // 如果 damageSourcePos 有傳入有效座標才計算方向
        bool hasDamageSource = damageSourcePos != default(Vector3);

        if (health > 0f) 
        {
            if (hasDamageSource)
            {
                // 建議改寫成這樣，利用現有的平滑旋轉系統
                isTurningInPlace = true;
                turnTargetPos = damageSourcePos;
            }
            
            // 可以根據受傷方向播放不同的受擊動畫 (Damage01 ~ Damage05) 
        }
        else if (health <= 0f)
        {
            // 預設為普通的往後倒動畫
            SoldierAction deathAction = SoldierAction.Death01; 

            if (hasDamageSource)
            {
                // 1. 取得從 Agent 指向「傷害來源」的方向向量
                Vector3 dirToSource = damageSourcePos - transform.position;
                
                // 2. 只把 Y 軸歸零，投影到 XZ 水平面 (保留 X 左右 與 Z 前後)
                dirToSource.y = 0; 
                
                // 3. 歸零 Y 軸後，重新單位化 (非常重要，確保向量長度正確)
                dirToSource = dirToSource.normalized;

                // 計算 Agent 正前方與傷害來源的夾角
                float hitAngle = Vector3.SignedAngle(transform.forward, dirToSource, Vector3.up);
                LogDebug($"{name} was hit from angle: {hitAngle}");

                // 3. 切割成四個 90 度的扇形區域來判斷
                if (hitAngle >= -45f && hitAngle <= 45f)
                {
                    // 傷害來自前方 -> 往後倒
                    deathAction = SoldierAction.Death02; 
                }
                else if (hitAngle > 45f && hitAngle < 135f)
                {
                    // 傷害來自右方 -> 往左側倒
                    deathAction = SoldierAction.Death03; 
                }
                else if (hitAngle < -45f && hitAngle > -135f)
                {
                    // 傷害來自左方 -> 往右側倒
                    deathAction = SoldierAction.Death03; 
                }
                else
                {
                    // 傷害來自後方 (大於 135 或是 小於 -135) -> 往前倒 (撲街)
                    deathAction = SoldierAction.Death01; 
                }
            }
            
            // 賦值並執行死亡邏輯
            currentAction = deathAction;
            currentState = AgentState.NONE; // 死亡後清空所有行為狀態
            if (brain != null)
            {
                brain.currentDecision = AgentDecision.LONGREST; // 死亡後進入休息狀態
            }
            else if (!warnedMissingBrain)
            {
                warnedMissingBrain = true;
                Debug.LogWarning($"[Agent] {name} is missing AgentBrain; death decision state was not updated.");
            }

            StartCoroutine(DelayDeath()); // 延遲死亡，讓動畫有時間播放完
        }
    }

    IEnumerator DelayDeath()
    {
        LogDebug($"{name} death timer started!");

        // Pause execution for 2 seconds
        yield return new WaitForSeconds(2f);

        gameObject.SetActive(false);
        LogDebug($"{name} has died and disabled!");
    }

    private bool CanUpdateAnimationState()
    {
        if (animator != null && soldierController != null)
            return true;

        if (!warnedMissingAnimationRefs)
        {
            warnedMissingAnimationRefs = true;
            Debug.LogWarning($"[Agent] {name} is missing Animator or HumanSoldierController; animation state sync is disabled.");
        }

        return false;
    }

    private void LogDebug(string message)
    {
        if (!showDebugLogs)
            return;

        Debug.Log($"[Agent] {message}");
    }
}
