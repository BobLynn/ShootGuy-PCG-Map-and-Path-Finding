using System.Diagnostics;
using UnityEngine;
using KevinIglesias;

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
public class Agent : MonoBehaviour
{
    public string Name = "Agent";
    public string Type = "Guard";
    public AgentType agentType = AgentType.GUARD;
    [Header("Target & Strategy")]
    public Transform targetObject;
    public Transform[] threatObjects; // 可能的威脅來源（例如玩家、爆炸點等）
    public AgentState defaultState = AgentState.SEEK;
    public AgentState currentState = AgentState.SEEK;

    [Header("Agent Stats")]
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

    public Vector3 velocity => locomotion.velocity;

    [HideInInspector] public Animator animator; // 新增 Animator 參照

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
}