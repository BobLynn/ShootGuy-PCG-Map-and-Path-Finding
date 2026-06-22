using UnityEngine;
using KevinIglesias;
using System.Collections; //Required for IEnumerator and Coroutines
using System.Collections.Generic;
using System.Diagnostics.Contracts;

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
public enum AgentDecision { NONE, LONGREST,SHORTREST, PATROL, CHASE, INVESTIGATE, RUN, ATTACK }

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
    public bool showDebugGizmos = true;

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
    public bool playerfounded = false; 

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

    // ✨ [新增] 戰術側步意圖標籤 (供 Brain 傳遞給 BehaviorManager)
    [Header("Tactical Movement")]
    public bool isStrafing = false;
    public Vector3 strafeDirection = Vector3.zero;

    [Header("Target Agent Rules")]
    public Transform escapePoint;
    public Agent escortAgent;
    public bool targetEscapesWhenPlayerSpotted = true;
    public float targetEscapeArriveRadius = 1.5f;
    public bool despawnEscortOnEscape = true;
    public bool forceEscortFollowDuringEscape = true;
    public bool revealTargetToPlayer = false;
    public Color revealColor = Color.red;
    public Vector2 revealBoxSize = new Vector2(72f, 112f);

    [Header("Target Agent Presentation")]
    public bool applyTargetPresentation = true;
    public string targetTagName = "Target";
    public string targetLayerName = "target";
    public Material targetVisualMaterial;
    public string[] targetVisualRendererNames = { "HumanF_BodyMesh", "Human_SoldierHelmet" };
    public bool disableWeaponsWhenTarget = true;

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
    private bool targetIsEscaping = false;
    private bool targetHasEscaped = false;

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

    void OnValidate()
    {
        if (agentType != AgentType.TARGET)
            return;

        locomotion = GetComponent<AgentLocomotion>();
        navigator = GetComponent<AgentNavigator>();
        brain = GetComponent<AgentBrain>();
        sensory = GetComponent<SensorySystem>();
        soldierController = GetComponent<HumanSoldierController>();
        animator = GetComponent<Animator>();

        ApplyTargetAgentDefaults();
        SyncTargetEscapeEndpoint();
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

        if (agentType == AgentType.TARGET)
        {
            ApplyTargetAgentDefaults();
            SyncTargetEscapeEndpoint();
        }

        
    }
    public void AddState(AgentState state) => currentState |= state;
    public void RemoveState(AgentState state) => currentState &= ~state;
    public bool HasState(AgentState state) => (currentState & state) == state;
    public void ClearStates() => currentState = AgentState.NONE;

    void Update()
    {
        UpdateTargetAgentRules();

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

            StartCoroutine(DelayDisappear()); // 延遲死亡，讓動畫有時間播放完
        }
    }

    public IEnumerator DelayDisappear()
    {
        LogDebug($"{name} death timer started!");

        // Pause execution for 2 seconds
        yield return new WaitForSeconds(2f);

        gameObject.SetActive(false);
        LogDebug($"{name} has died and disabled!");
    }

    public void ConfigureAsFinalTarget(Transform escape, Agent escort, GridMap3D gridMap, WaypointGraph3D waypointGraph, LayerMask obstacleLayers)
    {
        locomotion = locomotion != null ? locomotion : GetComponent<AgentLocomotion>();
        navigator = navigator != null ? navigator : GetComponent<AgentNavigator>();
        brain = brain != null ? brain : GetComponent<AgentBrain>();
        sensory = sensory != null ? sensory : GetComponent<SensorySystem>();
        soldierController = soldierController != null ? soldierController : GetComponent<HumanSoldierController>();
        animator = animator != null ? animator : GetComponent<Animator>();

        agentType = AgentType.TARGET;
        escapePoint = escape;
        escortAgent = escort;

        ApplyTargetAgentDefaults();
        SyncTargetEscapeEndpoint();
        ConfigureNavigator(navigator, gridMap, waypointGraph, obstacleLayers);

        if (escortAgent != null)
        {
            ConfigureNavigator(escortAgent.navigator != null ? escortAgent.navigator : escortAgent.GetComponent<AgentNavigator>(), gridMap, waypointGraph, obstacleLayers);
        }
    }

    public void BeginTargetEscape(Transform threat)
    {
        if (agentType != AgentType.TARGET || targetHasEscaped || targetIsEscaping)
            return;

        if (escapePoint == null)
        {
            GameObject spawn = GameObject.Find("PlayerSpawn");
            if (spawn != null)
                escapePoint = spawn.transform;
        }

        SyncTargetEscapeEndpoint();

        if (escapePoint == null)
        {
            Debug.LogWarning($"[Agent] {name} cannot start target escape because escapePoint is missing.");
            return;
        }

        targetIsEscaping = true;
        if (brain != null)
        {
            brain.ChangeDecision(AgentDecision.RUN, threat != null ? threat : escapePoint, default, 0);
        }

        UpdateTargetEscortFollow();
    }

    private void UpdateTargetAgentRules()
    {
        if (agentType != AgentType.TARGET || targetHasEscaped || health <= 0f)
            return;

        ApplyTargetAgentDefaults();

        if (escapePoint == null)
        {
            GameObject spawn = GameObject.Find("PlayerSpawn");
            if (spawn != null)
            {
                escapePoint = spawn.transform;
                SyncTargetEscapeEndpoint();
            }
        }

        if (targetEscapesWhenPlayerSpotted && !targetIsEscaping && ShouldStartTargetEscape())
        {
            BeginTargetEscape(FindPlayerTransform());
        }

        if (!targetIsEscaping)
            return;

        UpdateTargetEscortFollow();

        if (HasTargetReachedEscapePoint())
        {
            CompleteTargetEscape();
        }
    }

    private void ApplyTargetAgentDefaults()
    {
        currentWeapon = SoldierWeapons.None;
        currentAction = SoldierAction.Nothing;
        defaultState = AgentState.NONE;

        if (brain != null && !targetIsEscaping)
        {
            brain.defaultDecision = AgentDecision.LONGREST;
            brain.currentDecision = AgentDecision.LONGREST;
        }

        if (applyTargetPresentation)
        {
            ApplyTargetTagAndLayer();
            ApplyTargetVisualMaterial();
        }

        if (disableWeaponsWhenTarget)
        {
            HideEquippedWeapons();
        }
    }

    private void ApplyTargetTagAndLayer()
    {
        if (!string.IsNullOrEmpty(targetTagName))
        {
            try
            {
                gameObject.tag = targetTagName;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"[Agent] Target tag '{targetTagName}' does not exist.");
            }
        }

        int targetLayer = LayerMask.NameToLayer(targetLayerName);
        if (targetLayer >= 0)
        {
            SetLayerRecursively(gameObject, targetLayer);
        }
        else if (!string.IsNullOrEmpty(targetLayerName))
        {
            Debug.LogWarning($"[Agent] Target layer '{targetLayerName}' does not exist.");
        }
    }

    private void ApplyTargetVisualMaterial()
    {
        if (targetVisualMaterial == null || targetVisualRendererNames == null)
            return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            for (int i = 0; i < targetVisualRendererNames.Length; i++)
            {
                if (renderer.name == targetVisualRendererNames[i])
                {
                    renderer.sharedMaterial = targetVisualMaterial;
                    break;
                }
            }
        }
    }

    private void HideEquippedWeapons()
    {
        if (soldierController == null)
            soldierController = GetComponent<HumanSoldierController>();

        if (soldierController == null)
            return;

        soldierController.equippedWeapon = SoldierWeapons.None;
        soldierController.action = SoldierAction.Nothing;

        if (soldierController.weapons == null)
            return;

        foreach (GameObject weapon in soldierController.weapons)
        {
            if (weapon != null)
                weapon.SetActive(false);
        }
    }

    private void SyncTargetEscapeEndpoint()
    {
        AgentActionController actionController = GetComponent<AgentActionController>();
        if (actionController == null)
            return;

        actionController.runToSpecificEndpoint = true;
        actionController.escapeEndpoint = escapePoint;
    }

    private bool ShouldStartTargetEscape()
    {
        if (playerfounded)
            return true;

        return sensory != null && sensory.canSeePlayer;
    }

    private Transform FindPlayerTransform()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        return player != null ? player.transform : null;
    }

    private void UpdateTargetEscortFollow()
    {
        if (!forceEscortFollowDuringEscape || escortAgent == null || !escortAgent.gameObject.activeInHierarchy)
            return;

        AgentBrain escortBrain = escortAgent.brain != null ? escortAgent.brain : escortAgent.GetComponent<AgentBrain>();
        if (escortBrain == null)
            return;

        escortAgent.agentType = AgentType.GUARD;
        escortAgent.targetObject = transform;
        escortBrain.ChangeDecision(AgentDecision.CHASE, transform, default, 1);
    }

    private bool HasTargetReachedEscapePoint()
    {
        if (escapePoint == null)
            return false;

        AgentActionController actionController = GetComponent<AgentActionController>();
        if (actionController != null && actionController.HasSuccessfullyEscaped())
            return true;

        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatEscape = new Vector3(escapePoint.position.x, 0f, escapePoint.position.z);
        return Vector3.Distance(flatPos, flatEscape) <= targetEscapeArriveRadius;
    }

    private void CompleteTargetEscape()
    {
        targetHasEscaped = true;
        Debug.Log($"[Agent] {name} escaped. Mission failed.");

        if (despawnEscortOnEscape && escortAgent != null)
            escortAgent.gameObject.SetActive(false);

        gameObject.SetActive(false);
    }

    private static void ConfigureNavigator(AgentNavigator agentNavigator, GridMap3D gridMap, WaypointGraph3D waypointGraph, LayerMask obstacleLayers)
    {
        if (agentNavigator == null)
            return;

        agentNavigator.gridMap = gridMap;
        agentNavigator.waypointGraph = waypointGraph;
        agentNavigator.useGridMap = false;
        agentNavigator.ObstacleLayers = obstacleLayers;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    void OnGUI()
    {
        if (agentType != AgentType.TARGET || !revealTargetToPlayer || !gameObject.activeInHierarchy)
            return;

        Camera camera = GetRevealCamera();
        if (camera == null)
            return;

        Vector3 headScreen = camera.WorldToScreenPoint(transform.position + Vector3.up * 2.0f);
        Vector3 bodyScreen = camera.WorldToScreenPoint(transform.position + Vector3.up * 0.9f);
        if (headScreen.z <= 0f)
            return;

        float x = headScreen.x - revealBoxSize.x * 0.5f;
        float y = Screen.height - headScreen.y;
        float height = Mathf.Max(revealBoxSize.y, Mathf.Abs((Screen.height - bodyScreen.y) - y) + 48f);
        Rect box = new Rect(x, y, revealBoxSize.x, height);

        GUI.color = revealColor;
        DrawScreenRectOutline(box, 2f);
        GUI.Label(new Rect(box.x - 8f, box.y - 22f, box.width + 32f, 22f), "TARGET");

        Transform player = FindPlayerTransform();
        if (player != null)
        {
            float distance = Vector3.Distance(player.position, transform.position);
            GUI.Label(new Rect(box.x - 8f, box.yMax + 2f, box.width + 48f, 22f), $"{distance:0}m");
        }

        GUI.color = Color.white;
    }

    private static Camera GetRevealCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                return cameras[i];
        }

        return null;
    }

    private static void DrawScreenRectOutline(Rect rect, float thickness)
    {
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
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


