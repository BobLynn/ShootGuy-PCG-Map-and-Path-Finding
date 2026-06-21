using KevinIglesias;
using UnityEngine;

[RequireComponent(typeof(Agent))]
[RequireComponent(typeof(AgentBrain))]
[RequireComponent(typeof(AgentActionController))]
[RequireComponent(typeof(SensorySystem))]
public class TargetAgentController : MonoBehaviour
{
    [Header("Escape Rules")]
    public Transform escapePoint;
    public bool startEscapingWhenPlayerSpotted = true;
    public float escapeArriveRadius = 1.5f;
    public bool despawnEscortOnEscape = true;

    [Header("Escort")]
    public Agent escortAgent;
    public bool forceEscortFollowDuringEscape = true;

    [Header("Target Identity")]
    public bool disableWeaponsOnStart = true;
    public bool revealTargetToPlayer = true;
    public Color revealColor = Color.red;
    public Vector2 revealBoxSize = new Vector2(72f, 112f);

    private Agent agent;
    private AgentBrain brain;
    private AgentActionController actionController;
    private SensorySystem sensory;
    private bool isEscaping;
    private bool hasEscaped;

    void Awake()
    {
        CacheComponents();
        ApplyTargetDefaults();
    }

    void Start()
    {
        CacheComponents();
        ApplyTargetDefaults();

        if (escapePoint == null)
        {
            GameObject spawn = GameObject.Find("PlayerSpawn");
            if (spawn != null)
                escapePoint = spawn.transform;
        }

        SyncEscapeEndpoint();
    }

    void Update()
    {
        if (hasEscaped)
            return;

        if (startEscapingWhenPlayerSpotted && !isEscaping && ShouldStartEscape())
        {
            BeginEscape(FindPlayerTransform());
        }

        if (!isEscaping)
            return;

        UpdateEscortFollow();

        if (HasReachedEscapePoint())
        {
            CompleteEscape();
        }
    }

    public void ConfigureForFinalLevel(Transform escape, Agent escort, GridMap3D gridMap, WaypointGraph3D waypointGraph, LayerMask obstacleLayers)
    {
        CacheComponents();

        escapePoint = escape;
        escortAgent = escort;
        ApplyTargetDefaults();
        SyncEscapeEndpoint();
        ConfigureNavigator(agent != null ? agent.navigator : null, gridMap, waypointGraph, obstacleLayers);

        if (escortAgent != null)
        {
            ConfigureNavigator(escortAgent.navigator, gridMap, waypointGraph, obstacleLayers);
        }
    }

    public void BeginEscape(Transform threat)
    {
        if (hasEscaped || isEscaping)
            return;

        CacheComponents();
        SyncEscapeEndpoint();

        if (escapePoint == null)
        {
            Debug.LogWarning($"[TargetAgentController] {name} cannot escape because no escapePoint is assigned.");
            return;
        }

        isEscaping = true;
        if (brain != null)
        {
            brain.ChangeDecision(AgentDecision.RUN, threat != null ? threat : escapePoint, default, 0);
        }

        UpdateEscortFollow();
    }

    private void CacheComponents()
    {
        if (agent == null) agent = GetComponent<Agent>();
        if (brain == null) brain = GetComponent<AgentBrain>();
        if (actionController == null) actionController = GetComponent<AgentActionController>();
        if (sensory == null) sensory = GetComponent<SensorySystem>();
    }

    private void ApplyTargetDefaults()
    {
        if (agent == null)
            return;

        agent.agentType = AgentType.TARGET;
        agent.currentWeapon = SoldierWeapons.None;
        agent.currentAction = SoldierAction.Nothing;
        agent.defaultState = AgentState.NONE;

        if (brain != null && !isEscaping)
        {
            brain.defaultDecision = AgentDecision.LONGREST;
            brain.currentDecision = AgentDecision.LONGREST;
        }

        if (disableWeaponsOnStart)
        {
            HumanSoldierController soldier = GetComponent<HumanSoldierController>();
            if (soldier != null)
            {
                soldier.equippedWeapon = SoldierWeapons.None;
                soldier.action = SoldierAction.Nothing;
                if (soldier.weapons != null)
                {
                    foreach (GameObject weapon in soldier.weapons)
                    {
                        if (weapon != null)
                            weapon.SetActive(false);
                    }
                }
            }
        }
    }

    private void SyncEscapeEndpoint()
    {
        if (actionController == null)
            return;

        actionController.runToSpecificEndpoint = true;
        actionController.escapeEndpoint = escapePoint;
    }

    private bool ShouldStartEscape()
    {
        if (agent != null && agent.playerfounded)
            return true;

        return sensory != null && sensory.canSeePlayer;
    }

    private Transform FindPlayerTransform()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        return player != null ? player.transform : null;
    }

    private void UpdateEscortFollow()
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

    private bool HasReachedEscapePoint()
    {
        if (escapePoint == null)
            return false;

        if (actionController != null && actionController.HasSuccessfullyEscaped())
            return true;

        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatEscape = new Vector3(escapePoint.position.x, 0f, escapePoint.position.z);
        return Vector3.Distance(flatPos, flatEscape) <= escapeArriveRadius;
    }

    private void CompleteEscape()
    {
        hasEscaped = true;
        Debug.Log($"[TargetAgentController] {name} escaped. Mission failed.");

        if (despawnEscortOnEscape && escortAgent != null)
            escortAgent.gameObject.SetActive(false);

        gameObject.SetActive(false);
    }

    private static void ConfigureNavigator(AgentNavigator navigator, GridMap3D gridMap, WaypointGraph3D waypointGraph, LayerMask obstacleLayers)
    {
        if (navigator == null)
            return;

        navigator.gridMap = gridMap;
        navigator.waypointGraph = waypointGraph;
        navigator.useGridMap = false;
        navigator.ObstacleLayers = obstacleLayers;
    }

    void OnGUI()
    {
        if (!revealTargetToPlayer || !gameObject.activeInHierarchy)
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
}
