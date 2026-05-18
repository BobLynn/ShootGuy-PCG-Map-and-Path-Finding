using System.Diagnostics;
using UnityEngine;

[System.Flags]
public enum AgentState 
{ 
    NONE = 0, 
    SEEK = 1 << 0,     // 1
    ARRIVE = 1 << 1,   // 2
    CHASE = 1 << 2,    // 4
    WANDER = 1 << 3,   // 8  (預留給未來擴充)
    FLEE = 1 << 4      // 16 (預留給未來擴充)
}
// public enum AgentState { NONE, CHASE, ARRIVE, SEEK} 
public enum AgentDecision { REST, PATROL, CHASE, INVESTIGATE, FLEE, ATTACK }
[RequireComponent(typeof(CharacterController))] // 確保物件上有 CharacterController
public class Agent : MonoBehaviour
{
    [Header("Target & Strategy")]
    public Transform targetObject;
    public AgentState defaultState = AgentState.SEEK;
    public AgentState currentState = AgentState.SEEK;

    [Header("Agent Stats")]
    public float defaultSpeed = 5f;
    public float chaseSpeed = 8f;
    public float maxForce = 10f;

    [Header("Hub Interface (跨元件通訊)")]
    [HideInInspector] public AgentLocomotion locomotion; 
    [HideInInspector] public AgentNavigator navigator;
    [HideInInspector] public AgentBrain brain; // 新增 Brain 參照

    [HideInInspector] public float maxSpeed;             
    [HideInInspector] public Vector3 steeringForce;      
    [HideInInspector] public bool wantsToJump = false;   

    [Header("Behavior Flags")]
    public bool isWaiting = false;
    public bool isTurningInPlace = false; // 原地轉向標籤
    public Vector3 turnTargetPos;         // 原地轉向的目標點

    public Vector3 velocity => locomotion.velocity;

    void Start()
    {
        locomotion = GetComponent<AgentLocomotion>();
        navigator = GetComponent<AgentNavigator>();
        brain = GetComponent<AgentBrain>(); // 確保掛載 Brain
        maxSpeed = defaultSpeed;
        currentState = defaultState;

        
    }
    public void AddState(AgentState state) => currentState |= state;
    public void RemoveState(AgentState state) => currentState &= ~state;
    public bool HasState(AgentState state) => (currentState & state) == state;
    public void ClearStates() => currentState = AgentState.NONE;

    void Update()
    {

    }
}