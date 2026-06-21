#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

using System.Collections.Generic;
using System.Text;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

public class VillaPCG_v4 : MonoBehaviour, ILevelNavigator
{
    [Header("Navigation Debug Display")]
    public bool showWaypointGraph3D = true;
    public bool showGridMap3D = true;

    [Header("Agent Debug Display")]
    public bool showAgentDebugGizmos = true;

    [Header("Seed")]
    public int seed = 12345;
    public bool randomizeSeedOnPlay = false;

    // [Header("Obstacle / Cover Rules")]
    // public float playerRobotHeight = 2.0f;
    // public float obstacleMinExtraHeight = 0.3f;
    // public float obstacleMaxHeight = 4.0f;

    [Header("Navigation Layers")]
    public string walkableLayerName = "Walkable";
    public string unwalkableLayerName = "Unwalkable";
    public string waypointLayerName = "Default";

    [Header("Area Layers")]
    public string casualAreaLayerName = "CasualArea";
    public string restrictedAreaLayerName = "RestrictedArea";
    public string targetRoomLayerName = "TargetRoom";
    public float areaLayerMarkerHeight = 0.08f;

    [Header("Navigation Rebuild")]

    //WaypointGraph 設定
    public WaypointGraph3D waypointGraph;
    public bool rebuildWaypointGraphAfterGenerate = true;

    //GridMap 設定
    public GridMap3D gridMap;
    public bool rebuildGridMapAfterGenerate = true;
    public float gridMapPadding = 4.0f;

    readonly HashSet<string> missingLayerWarnings = new HashSet<string>();

    int ResolveLayer(string preferredLayerName, params string[] fallbackLayerNames)
    {
        int layer = LayerMask.NameToLayer(preferredLayerName);
        if (layer >= 0)
            return layer;

        foreach (string fallbackLayerName in fallbackLayerNames)
        {
            layer = LayerMask.NameToLayer(fallbackLayerName);
            if (layer >= 0)
            {
                string warningKey = preferredLayerName + "->" + fallbackLayerName;
                if (missingLayerWarnings.Add(warningKey))
                    Debug.LogWarning($"[VillaPCG] Layer '{preferredLayerName}' does not exist. Using '{fallbackLayerName}' instead.");

                return layer;
            }
        }

        string defaultWarningKey = preferredLayerName + "->Default";
        if (missingLayerWarnings.Add(defaultWarningKey))
            Debug.LogWarning($"[VillaPCG] Layer '{preferredLayerName}' does not exist. Using Default layer.");

        return 0;
    }

    int GetWalkableLayerSafe()
    {
        return ResolveLayer(walkableLayerName, "Obstacles", "Standable_Obstacles");
    }

    int GetUnwalkableLayerSafe()
    {
        return ResolveLayer(unwalkableLayerName, "Standable_Obstacles", "Obstacles");
    }

    int GetLayerSafe(string layerName)
    {
        return ResolveLayer(layerName);
    }

    int GetAreaLayerSafe(LayoutZone layoutZone)
    {
        string areaLayerName = layoutZone == LayoutZone.Restricted ? restrictedAreaLayerName : casualAreaLayerName;
        return ResolveLayer(areaLayerName, walkableLayerName);
    }

    int GetTargetRoomLayerSafe()
    {
        return ResolveLayer(targetRoomLayerName, restrictedAreaLayerName, casualAreaLayerName, walkableLayerName);
    }

    [Header("Generation Mode")]
    public bool generateOnPlay = false;

    [Header("Level Flow")]
    public int startLevel = 1;
    [Range(1, 3)]
    public int currentLevel = 1;
    public int finalPcgLevel = 3;
    public bool resetToStartLevelOnPlay = true;
    public bool teleportPlayerAfterGenerate = true;
    public Key levelAdvanceKey = Key.E;
    public float exitInteractRadius = 2.2f;
    public string playerTag = "Player";

    [Header("Level Objectives")]
    public Material exitMat;
    public Material targetMat;

    [Header("Connection Blocking")]
    public bool generateConnectionSideWalls = true;
    public float connectionSideWallOverlap = 0.35f;

    [Header("Geometry")]
    public float wallHeight = 3.0f;
    public float wallThickness = 0.25f;
    public float doorWidth = 4.0f;
    public float floorThickness = 0.15f;
    const float floorLayerSeparation = 0.02f;

    [Header("Visual Debug")]
    public bool showRoomLabels = true;
    public bool showWaypointGizmos = true;

    [Header("Materials")]
    public Material floorMat;
    public Material wallMat;
    public Material restrictedFloorMat;
    public Color restrictedFloorColor = new Color(1.0f, 0.93333334f, 0.54901963f);

    [Header("Layout Scale")]
    public float layoutScale = 2.0f;          // 房間與地圖整體放大 2 倍
    public float connectorFloorWidth = 3.5f;  // 房間之間連接地板寬度
    public bool generateConnectorFloors = true;
    public bool generateSafetyFoundation = true;

    [Header("Final Villa PCG")]
    [Range(40, 100)]
    public int finalVillaComplexity = 76;
    public FinalVillaStyle finalVillaStyle = FinalVillaStyle.Garden;

    [Header("Enemy Auto Layout")]
    public bool generateEnemyAgents = true;
    public GameObject enemyAgentPrefab;
    [Header("Final Target Agent")]
    public bool generateFinalTargetAgent = true;
    public GameObject targetAgentPrefab;
    public bool generateTargetEscort = true;
    public GameObject targetEscortAgentPrefab;
    public float targetEscortOffset = 2.4f;
    [HideInInspector]
    public int maxPatrolEnemyCount = 4;
    [HideInInspector]
    public int maxStandEnemyCount = 6;
    [Header("Enemy Count By Level")]
    [Range(0, 16)]
    public int level1PatrolEnemyCount = 3;
    [Range(0, 16)]
    public int level1StandEnemyCount = 4;
    [Range(0, 16)]
    public int level2PatrolEnemyCount = 7;
    [Range(0, 16)]
    public int level2StandEnemyCount = 8;
    [Range(0, 32)]
    public int level3PatrolEnemyCount = 12;
    [Range(0, 32)]
    public int level3StandEnemyCount = 13;
    [Header("Patrol Behavior Variants")]
    [Range(0, 8)]
    public int roomPairPatrolEnemyCount = 2;
    public float enemySpawnHeight = 0.08f;
    public float enemyRoomInset = 2.2f;
    public float patrolRouteCornerAvoidance = 3.2f;
    public float standingGuardDoorOffset = 2.0f;
    public float enemySpawnExclusionRadius = 6.0f;
    public float patrolWaypointWaitTime = 1.0f;
    public float roomPairPatrolAnchorWaitTime = 3.5f;
    public float roomPairPatrolAnchorInset = 2.6f;
    public string generatedEnemyRoutePrefix = "PCG_Enemy_";
    [HideInInspector]
    public bool logEnemyPlacementSummary = true;
    [HideInInspector]
    [TextArea(3, 8)]
    public string lastMapRuleSummary;
    [HideInInspector]
    [TextArea(3, 8)]
    public string lastEnemyPlacementSummary;

    void Start()
    {
        if (Application.isPlaying)
        {
            if (resetToStartLevelOnPlay)
                currentLevel = Mathf.Clamp(startLevel, 1, finalPcgLevel);

            if (generateOnPlay || resetToStartLevelOnPlay)
                Generate();
        }
    }

    private List<Room> rooms = new List<Room>();
    private List<Connection> connections = new List<Connection>();
    private List<Vector3> waypoints = new List<Vector3>();
    private Dictionary<string, List<string>> roomGraph = new Dictionary<string, List<string>>();
    private string playerSpawnRoomName;
    private string primaryGoalRoomName;
    private string secondaryGoalRoomName;
    private Room levelExitRoom;
    private Room finalTargetRoom;
    private Transform currentSpawnTransform;
    private List<Vector3> generatedEnemyGizmoPositions = new List<Vector3>();

    enum Side
    {
        North,
        South,
        East,
        West
    }

    enum SecurityLevel
    {
        Casual,
        Restricted,
    }

    public enum LayoutZone
    {
        Casual,
        Restricted
    }

    static LayoutZone GetLayoutZoneForSecurity(SecurityLevel security)
    {
        switch (security)
        {
            case SecurityLevel.Casual:
                return LayoutZone.Casual;
            case SecurityLevel.Restricted:
                return LayoutZone.Restricted;
            default:
                return LayoutZone.Casual;
        }
    }

    static string GetLayoutZoneLabel(LayoutZone layoutZone)
    {
        switch (layoutZone)
        {
            case LayoutZone.Casual:
                return "Casual Zone";
            case LayoutZone.Restricted:
                return "Restricted Zone";
            default:
                return layoutZone.ToString();
        }
    }

    public enum FinalVillaStyle
    {
        Garden,
        Courtyard,
        Resort,
        LinearFinalLevel
    }

    class Room
    {
        public string name;
        public Vector2 center;
        public Vector2 size;
        public SecurityLevel security;
        public LayoutZone layoutZone;

        public Room(string name, Vector2 center, Vector2 size, SecurityLevel security)
        {
            this.name = name;
            this.center = center;
            this.size = size;
            this.security = security;
            this.layoutZone = VillaPCG_v4.GetLayoutZoneForSecurity(security);
        }

        public float Left => center.x - size.x * 0.5f;
        public float Right => center.x + size.x * 0.5f;
        public float Bottom => center.y - size.y * 0.5f;
        public float Top => center.y + size.y * 0.5f;
    }

    class Connection
    {
        public Room a;
        public Room b;
        public Side sideA;
        public Side sideB;
        public Vector3 doorWorldPos;

        public Connection(Room a, Room b, Side sideA, Side sideB, Vector3 doorWorldPos)
        {
            this.a = a;
            this.b = b;
            this.sideA = sideA;
            this.sideB = sideB;
            this.doorWorldPos = doorWorldPos;
        }
    }

    class RoomPairPatrolCandidate
    {
        public Connection connection;
        public Room roomA;
        public Room roomB;
        public Vector3 anchorA;
        public Vector3 anchorB;
        public float score;
        public string reason;
    }

    class GridRoom
    {
        public string name;
        public int gx;
        public int gy;
        public int gw;
        public int gh;
        public SecurityLevel security;
        public LayoutZone layoutZone;
        public Room room;

        public GridRoom(string name, int gx, int gy, int gw, int gh, SecurityLevel security)
        {
            this.name = name;
            this.gx = gx;
            this.gy = gy;
            this.gw = gw;
            this.gh = gh;
            this.security = security;
            this.layoutZone = VillaPCG_v4.GetLayoutZoneForSecurity(security);
        }

        public void SetSecurity(SecurityLevel security)
        {
            this.security = security;
            this.layoutZone = VillaPCG_v4.GetLayoutZoneForSecurity(security);

            if (room != null)
            {
                room.security = security;
                room.layoutZone = this.layoutZone;
            }
        }

        public int Left => gx;
        public int Right => gx + gw;
        public int Top => gy;
        public int Bottom => gy + gh;
    }

    class GridConnection
    {
        public GridRoom a;
        public GridRoom b;

        public GridConnection(GridRoom a, GridRoom b)
        {
            this.a = a;
            this.b = b;
        }
    }

    class EnemyPlacementRecord
    {
        public string enemyName;
        public string routeName;
        public string roomName;
        public string ruleReason;
        public Vector3 position;
    }

    class StandingGuardCandidate
    {
        public Room room;
        public Vector3 position;
        public float score;
        public string reason;
    }

    // void Start()
    // {
    //     Generate();
    // }

    public void Generate()
    {
        currentLevel = Mathf.Clamp(currentLevel, 1, finalPcgLevel);

        ClearOldMap();

        if (randomizeSeedOnPlay)
            seed = Random.Range(0, 999999);

        Random.InitState(seed);

        CreateMaterialsIfMissing();
        GenerateLevelLayout(currentLevel);
        BuildRoomGraph();
        ValidateReachability();
        UpdateMapRuleSummary();

        // 視覺用大地板，可選，但不要讓 pathfinding 使用它
        CreateOnePieceFloor();

        // 真正的 rooms / walls
        BuildGeometry();

        // corridor walls
        // CreateConnectionCorridors();

        // corridor floors: when rooms do not touch, these become the walkable bridge
        // that GridMap3D can raycast onto.
        if (generateConnectorFloors)
            CreateConnectorFloors();

        // connection side walls
        if (generateConnectionSideWalls)
            CreateConnectionSideWalls();

        if (generateSafetyFoundation)
            CreateSafetyFoundation();

        GenerateWaypoints();
        CreatePlayerSpawn();
        CreateLevelSpecificObjects();

        Physics.SyncTransforms();

        RebuildNavigationAfterGenerate();

        GenerateEnemyLayout();
        ApplyAgentDebugDisplay();

        Physics.SyncTransforms();

    #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    #endif

        Debug.Log($"[VillaPCG] Runtime level {currentLevel} map generated.");

        if (Application.isPlaying && teleportPlayerAfterGenerate)
            TeleportPlayerToSpawn();
    }

    public void GoToLevel(int levelNumber)
    {
        int clampedLevel = Mathf.Clamp(levelNumber, 1, finalPcgLevel);
        if (clampedLevel == currentLevel && Application.isPlaying)
            return;

        currentLevel = clampedLevel;
        Generate();
    }

    public void AdvanceToNextLevel()
    {
        GoToLevel(currentLevel + 1);
    }

    public void ClearGeneratedVilla()
    {
        ClearOldMap();
    }

    public void RegenerateWithNewSeed()
    {
        int previousSeed = seed;

    #if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RecordObject(this, "Regenerate Villa With New Seed");
    #endif

        seed = CreateNonRepeatingSeed(previousSeed);
        bool previousRandomizeSeedOnPlay = randomizeSeedOnPlay;
        randomizeSeedOnPlay = false;

        try
        {
            Generate();
        }
        finally
        {
            randomizeSeedOnPlay = previousRandomizeSeedOnPlay;
        }

    #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    #endif

        Debug.Log($"[VillaPCG] Regenerated level {currentLevel} with new seed {seed} (previous seed {previousSeed}).");
    }

    int CreateNonRepeatingSeed(int previousSeed)
    {
        int newSeed = (System.Guid.NewGuid().GetHashCode() & int.MaxValue) % 999999;
        if (newSeed == previousSeed)
            newSeed = (newSeed + 1) % 999999;

        return newSeed;
    }

    public void GenerateLevelOne()
    {
        currentLevel = 1;
        Generate();
    }

    public void GenerateLevelTwo()
    {
        currentLevel = 2;
        Generate();
    }

    public void GenerateFinalPcgLevel()
    {
        currentLevel = finalPcgLevel;
        Generate();
    }

    void EnsureNavigationReferences()
    {
        if (gridMap == null)
            gridMap = Object.FindFirstObjectByType<GridMap3D>();

        if (waypointGraph == null)
            waypointGraph = Object.FindFirstObjectByType<WaypointGraph3D>();
    }

    void RebuildNavigationAfterGenerate()
    {
        EnsureNavigationReferences();

        Physics.SyncTransforms();

        if (rebuildGridMapAfterGenerate)
        {
            if (gridMap != null)
            {
                int walkableLayer = GetWalkableLayerSafe();
                int unwalkableLayer = GetUnwalkableLayerSafe();
                Bounds mapBounds = GetGeneratedMapBounds(gridMapPadding);

                gridMap.gridCenter = mapBounds.center;
                gridMap.width = mapBounds.size.x;
                gridMap.length = mapBounds.size.z;
                gridMap.walkableLayer = 1 << walkableLayer;
                gridMap.unwalkableLayer = 1 << unwalkableLayer;

                gridMap.GenerateGrid();
            }
            else
            {
                Debug.LogWarning("[VillaPCG] GridMap3D reference is missing; grid was not rebuilt.");
            }
        }

        if (rebuildWaypointGraphAfterGenerate)
        {
            if (waypointGraph != null)
            {
                int walkableLayer = GetWalkableLayerSafe();
                int unwalkableLayer = GetUnwalkableLayerSafe();
                waypointGraph.walkableLayers = 1 << walkableLayer;
                waypointGraph.unwalkableLayers = 1 << unwalkableLayer;
                waypointGraph.GenerateGraph();
            }
            else
                Debug.LogWarning("[VillaPCG] WaypointGraph3D reference is missing; waypoint graph was not rebuilt.");
        }
    }

    // [ContextMenu("Generate Waypoint Graph")]
    // public void GenerateGraph()
    // {
    //     nodes.Clear();
    //     edges.Clear();

    //     // 原本 GenerateGraph 內容
    // }

    void ClearOldMap()
    {
        ClearGeneratedEnemyRoutes();

        List<GameObject> toDelete = new List<GameObject>();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            toDelete.Add(transform.GetChild(i).gameObject);
        }

        foreach (GameObject obj in toDelete)
        {
            if (Application.isPlaying)
            {
                obj.SetActive(false);
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        rooms.Clear();
        connections.Clear();
        waypoints.Clear();
        roomGraph.Clear();
    }

    void CreateMaterialsIfMissing()
    {
        if (floorMat == null)
        {
            floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.color = new Color(0.55f, 0.55f, 0.55f);
        }

        if (wallMat == null)
        {
            wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            wallMat.color = new Color(0.78f, 0.72f, 0.62f);
        }

        if (restrictedFloorMat == null)
        {
            restrictedFloorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }
        restrictedFloorMat.color = restrictedFloorColor;

        if (exitMat == null)
        {
            exitMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            exitMat.color = new Color(0.15f, 0.75f, 0.35f);
        }

        if (targetMat == null)
        {
            targetMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            targetMat.color = new Color(0.75f, 0.08f, 0.08f);
        }
    }

    void GenerateLevelLayout(int levelNumber)
    {
        playerSpawnRoomName = null;
        primaryGoalRoomName = null;
        secondaryGoalRoomName = null;
        levelExitRoom = null;
        finalTargetRoom = null;
        currentSpawnTransform = null;

        currentLevel = Mathf.Clamp(levelNumber, 1, finalPcgLevel);

        if (currentLevel == 1)
        {
            GenerateApproachAlleyLayout();
            return;
        }

        if (currentLevel == 2)
        {
            GenerateServiceWingLayout();
            return;
        }

        GenerateVillaLayout();
    }

    void GenerateApproachAlleyLayout()
    {
        Vector2 stagingSize = GetSeededRoomSize(8, 5, 0.18f);
        Vector2 streetSize = GetSeededRoomSize(8, 6, 0.16f);
        Vector2 maintenanceSize = GetSeededRoomSize(7, 8, 0.18f);
        Vector2 generatorSize = GetSeededRoomSize(5, 5, 0.16f);
        Vector2 supplySize = GetSeededRoomSize(5, 5, 0.16f);
        Vector2 exitSize = GetSeededRoomSize(8, 5, 0.16f);

        float stagingY = -10.0f;
        float streetY = GetAdjacentCenter(stagingY, stagingSize.y, streetSize.y, 1.0f);
        float maintenanceY = GetAdjacentCenter(streetY, streetSize.y, maintenanceSize.y, 1.0f);
        float exitY = GetAdjacentCenter(maintenanceY, maintenanceSize.y, exitSize.y, 1.0f);
        float generatorX = GetAdjacentCenter(0.0f, maintenanceSize.x, generatorSize.x, -1.0f);
        float supplyX = GetAdjacentCenter(0.0f, maintenanceSize.x, supplySize.x, 1.0f);

        Room stagingAlley = AddRoom("L1 Staging Alley", new Vector2(0, stagingY), stagingSize, SecurityLevel.Casual);
        Room streetGate = AddRoom("L1 Street Gate", new Vector2(0, streetY), streetSize, SecurityLevel.Casual);
        Room maintenanceHall = AddRoom("L1 Maintenance Hall", new Vector2(0, maintenanceY), maintenanceSize, SecurityLevel.Restricted);
        Room generatorRoom = AddRoom("L1 Generator Room", new Vector2(generatorX, maintenanceY), generatorSize, SecurityLevel.Restricted);
        Room supplyRoom = AddRoom("L1 Supply Room", new Vector2(supplyX, maintenanceY), supplySize, SecurityLevel.Casual);
        Room exitRoom = AddRoom("L1 Exit Room", new Vector2(0, exitY), exitSize, SecurityLevel.Restricted);

        ConnectRooms(stagingAlley, streetGate, Side.North, Side.South);
        ConnectRooms(streetGate, maintenanceHall, Side.North, Side.South);
        ConnectRooms(maintenanceHall, exitRoom, Side.North, Side.South);
        ConnectRooms(maintenanceHall, generatorRoom, Side.West, Side.East);
        ConnectRooms(maintenanceHall, supplyRoom, Side.East, Side.West);

        playerSpawnRoomName = stagingAlley.name;
        primaryGoalRoomName = exitRoom.name;
        levelExitRoom = exitRoom;
    }

    void GenerateServiceWingLayout()
    {
        Vector2 serviceSize = GetSeededRoomSize(8, 5, 0.16f);
        Vector2 loadingSize = GetSeededRoomSize(12, 7, 0.16f);
        Vector2 recordsSize = GetSeededRoomSize(5, 5, 0.16f);
        Vector2 workshopSize = GetSeededRoomSize(5, 5, 0.16f);
        Vector2 checkpointSize = GetSeededRoomSize(8, 6, 0.16f);
        Vector2 guardSize = GetSeededRoomSize(5, 5, 0.16f);
        Vector2 exitSize = GetSeededRoomSize(8, 5, 0.16f);

        float serviceY = -11.0f;
        float loadingY = GetAdjacentCenter(serviceY, serviceSize.y, loadingSize.y, 1.0f);
        float checkpointY = GetAdjacentCenter(loadingY, loadingSize.y, checkpointSize.y, 1.0f);
        float exitY = GetAdjacentCenter(checkpointY, checkpointSize.y, exitSize.y, 1.0f);
        float recordsX = GetAdjacentCenter(0.0f, loadingSize.x, recordsSize.x, -1.0f);
        float workshopX = GetAdjacentCenter(0.0f, loadingSize.x, workshopSize.x, 1.0f);
        float guardX = GetAdjacentCenter(0.0f, checkpointSize.x, guardSize.x, -1.0f);

        Room serviceEntrance = AddRoom("L2 Service Entrance", new Vector2(0, serviceY), serviceSize, SecurityLevel.Casual);
        Room loadingBay = AddRoom("L2 Loading Bay", new Vector2(0, loadingY), loadingSize, SecurityLevel.Restricted);
        Room recordsRoom = AddRoom("L2 Records Room", new Vector2(recordsX, loadingY), recordsSize, SecurityLevel.Restricted);
        Room workshop = AddRoom("L2 Workshop", new Vector2(workshopX, loadingY), workshopSize, SecurityLevel.Restricted);
        Room securityCheckpoint = AddRoom("L2 Security Checkpoint", new Vector2(0, checkpointY), checkpointSize, SecurityLevel.Restricted);
        Room guardLounge = AddRoom("L2 Guard Lounge", new Vector2(guardX, checkpointY), guardSize, SecurityLevel.Restricted);
        Room exitRoom = AddRoom("L2 Exit Room", new Vector2(0, exitY), exitSize, SecurityLevel.Restricted);

        ConnectRooms(serviceEntrance, loadingBay, Side.North, Side.South);
        ConnectRooms(loadingBay, securityCheckpoint, Side.North, Side.South);
        ConnectRooms(securityCheckpoint, exitRoom, Side.North, Side.South);
        ConnectRooms(loadingBay, recordsRoom, Side.West, Side.East);
        ConnectRooms(loadingBay, workshop, Side.East, Side.West);
        ConnectRooms(securityCheckpoint, guardLounge, Side.West, Side.East);

        playerSpawnRoomName = serviceEntrance.name;
        primaryGoalRoomName = exitRoom.name;
        levelExitRoom = exitRoom;
    }

    Vector2 GetSeededRoomSize(float baseWidth, float baseHeight, float variation)
    {
        return new Vector2(
            GetSeededRoomDimension(baseWidth, variation),
            GetSeededRoomDimension(baseHeight, variation)
        );
    }

    float GetSeededRoomDimension(float baseValue, float variation)
    {
        float randomized = baseValue * Random.Range(1.0f - variation, 1.0f + variation);
        float halfUnit = Mathf.Round(randomized * 2.0f) * 0.5f;
        return Mathf.Max(4.0f, halfUnit);
    }

    float GetAdjacentCenter(float anchorCenter, float anchorSize, float attachedSize, float direction)
    {
        return anchorCenter + direction * ((anchorSize + attachedSize) * 0.5f);
    }

    void GenerateVillaLayout()
    {
        if (finalVillaStyle == FinalVillaStyle.LinearFinalLevel)
        {
            GenerateLinearFinalVillaLayout();
            return;
        }

        int targetRooms = Mathf.Clamp(Mathf.RoundToInt(18.0f + finalVillaComplexity * 0.22f), 18, 40);
        int gridCols = 44;
        int gridRows = 34;
        List<GridRoom> gridRooms = new List<GridRoom>();
        List<GridConnection> gridConnections = new List<GridConnection>();
        HashSet<string> connectionKeys = new HashSet<string>();

        int coreW = finalVillaStyle == FinalVillaStyle.Courtyard ? 10 : finalVillaStyle == FinalVillaStyle.Resort ? 13 : 12;
        int coreH = finalVillaStyle == FinalVillaStyle.Resort ? 7 : 8;
        GridRoom core = new GridRoom(
            finalVillaStyle == FinalVillaStyle.Courtyard ? "Courtyard Arcade" : "Grand Salon",
            gridCols / 2 - coreW / 2,
            gridRows / 2 - coreH / 2 + 1,
            coreW,
            coreH,
            SecurityLevel.Casual
        );

        gridRooms.Add(core);

        for (int attempt = 0; gridRooms.Count < targetRooms && attempt < targetRooms * 90; attempt++)
        {
            GridRoom parent = gridRooms[Random.Range(0, gridRooms.Count)];
            Side side = GetRandomSide();
            SecurityLevel security = GetRandomFinalVillaSecurity(parent);
            Vector2Int size = GetRandomFinalVillaRoomSize(security);

            GridRoom room = CreateAttachedGridRoom(
                GetFinalVillaRoomName(gridRooms.Count, security),
                parent,
                side,
                size.x,
                size.y,
                security
            );

            if (!IsGridRoomInside(room, gridCols, gridRows))
                continue;

            if (OverlapsAnyGridRoom(room, gridRooms))
                continue;

            if (!GridRoomsTouch(room, parent))
                continue;

            gridRooms.Add(room);
            AddGridConnection(parent, room, gridConnections, connectionKeys);
        }

        AddExtraGridAdjacencies(gridRooms, gridConnections, connectionKeys);
        AssignFinalVillaObjectives(gridRooms);

        float xOffset = gridCols * 0.5f;
        float yOffset = gridRows * 0.5f;
        foreach (GridRoom gridRoom in gridRooms)
        {
            Vector2 center = new Vector2(
                gridRoom.gx + gridRoom.gw * 0.5f - xOffset,
                yOffset - (gridRoom.gy + gridRoom.gh * 0.5f)
            );
            Vector2 size = new Vector2(gridRoom.gw, gridRoom.gh);
            gridRoom.room = AddRoom(gridRoom.name, center, size, gridRoom.security);
        }

        foreach (GridConnection gridConnection in gridConnections)
        {
            ConnectGridRooms(gridConnection.a, gridConnection.b);
        }

        GridRoom spawnGridRoom = FindGridRoomByName(gridRooms, "Courtyard / Entrance");
        GridRoom targetGridRoom = FindGridRoomByName(gridRooms, "Target Room");
        GridRoom safeGridRoom = FindGridRoomByName(gridRooms, "Safe Room / Exit");

        playerSpawnRoomName = spawnGridRoom != null ? spawnGridRoom.room.name : gridRooms[0].room.name;
        primaryGoalRoomName = targetGridRoom != null ? targetGridRoom.room.name : gridRooms[gridRooms.Count - 1].room.name;
        secondaryGoalRoomName = safeGridRoom != null ? safeGridRoom.room.name : null;
        finalTargetRoom = targetGridRoom != null ? targetGridRoom.room : gridRooms[gridRooms.Count - 1].room;
    }

    void GenerateLinearFinalVillaLayout()
    {
        int targetRooms = Mathf.Clamp(Mathf.RoundToInt(18.0f + finalVillaComplexity * 0.22f), 18, 40);
        float complexity01 = Mathf.InverseLerp(40.0f, 100.0f, finalVillaComplexity);
        int spineRoomCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(11.0f, 16.0f, complexity01)), 8, targetRooms);

        List<Room> spineRooms = new List<Room>();
        List<Vector2> spineCenters = new List<Vector2>();
        List<Vector2> spineSizes = new List<Vector2>();

        Vector2 spawnSize = GetSeededRoomSize(8.0f, 5.0f, 0.12f);
        Vector2 spawnCenter = new Vector2(0.0f, -18.0f);
        Room spawnRoom = AddRoom("Courtyard / Entrance", spawnCenter, spawnSize, SecurityLevel.Casual);
        spineRooms.Add(spawnRoom);
        spineCenters.Add(spawnCenter);
        spineSizes.Add(spawnSize);

        Vector2 previousSize = spawnSize;
        float currentY = spawnCenter.y;

        for (int i = 1; i < spineRoomCount - 1; i++)
        {
            Vector2 size = GetLinearFinalSpineRoomSize(i, spineRoomCount, complexity01);
            currentY = GetAdjacentCenter(currentY, previousSize.y, size.y, 1.0f);

            SecurityLevel security = GetLinearFinalSpineSecurity(i, spineRoomCount);
            Room room = AddRoom(GetLinearFinalSpineRoomName(i), new Vector2(0.0f, currentY), size, security);
            spineRooms.Add(room);
            spineCenters.Add(new Vector2(0.0f, currentY));
            spineSizes.Add(size);

            ConnectRooms(spineRooms[spineRooms.Count - 2], room, Side.North, Side.South);
            previousSize = size;
        }

        Vector2 targetSize = GetSeededRoomSize(8.5f, 5.5f, 0.10f);
        currentY = GetAdjacentCenter(currentY, previousSize.y, targetSize.y, 1.0f);
        Room targetRoom = AddRoom("Target Room", new Vector2(0.0f, currentY), targetSize, SecurityLevel.Restricted);
        spineRooms.Add(targetRoom);
        spineCenters.Add(new Vector2(0.0f, currentY));
        spineSizes.Add(targetSize);
        ConnectRooms(spineRooms[spineRooms.Count - 2], targetRoom, Side.North, Side.South);

        int sideRoomBudget = Mathf.Max(0, targetRooms - spineRooms.Count);
        bool[,] sideUsed = new bool[spineRooms.Count, 2];
        List<Room> primarySideRooms = new List<Room>();
        List<Vector2> primarySideCenters = new List<Vector2>();
        List<Vector2> primarySideSizes = new List<Vector2>();
        List<Side> primarySideDirections = new List<Side>();

        int sideAttempt = 0;
        int firstBranchableSpine = 1;
        int lastBranchableSpine = spineRooms.Count - 2;
        while (sideRoomBudget > 0 && firstBranchableSpine <= lastBranchableSpine && sideAttempt < spineRooms.Count * 4)
        {
            int spineIndex = firstBranchableSpine + (sideAttempt % (lastBranchableSpine - firstBranchableSpine + 1));
            bool placeOnEast = ((sideAttempt / Mathf.Max(1, lastBranchableSpine - firstBranchableSpine + 1)) + spineIndex) % 2 == 0;
            int sideIndex = placeOnEast ? 1 : 0;

            if (!sideUsed[spineIndex, sideIndex])
            {
                Room anchor = spineRooms[spineIndex];
                Vector2 anchorCenter = spineCenters[spineIndex];
                Vector2 anchorSize = spineSizes[spineIndex];
                Vector2 sideSize = GetLinearFinalSideRoomSize(anchorSize, complexity01);
                float maxYOffset = Mathf.Max(0.0f, (anchorSize.y - sideSize.y) * 0.35f);
                float yOffset = maxYOffset > 0.0f ? Random.Range(-maxYOffset, maxYOffset) : 0.0f;
                float direction = placeOnEast ? 1.0f : -1.0f;
                float sideX = GetAdjacentCenter(anchorCenter.x, anchorSize.x, sideSize.x, direction);
                Vector2 sideCenter = new Vector2(sideX, anchorCenter.y + yOffset);

                Room sideRoom = AddRoom(GetLinearFinalSideRoomName(primarySideRooms.Count + 1), sideCenter, sideSize, GetLinearFinalSideRoomSecurity(spineIndex, spineRooms.Count));
                ConnectRooms(anchor, sideRoom, placeOnEast ? Side.East : Side.West, placeOnEast ? Side.West : Side.East);

                sideUsed[spineIndex, sideIndex] = true;
                primarySideRooms.Add(sideRoom);
                primarySideCenters.Add(sideCenter);
                primarySideSizes.Add(sideSize);
                primarySideDirections.Add(placeOnEast ? Side.East : Side.West);
                sideRoomBudget--;
            }

            sideAttempt++;
        }

        int secondaryIndex = 0;
        while (sideRoomBudget > 0 && secondaryIndex < primarySideRooms.Count)
        {
            Room anchor = primarySideRooms[secondaryIndex];
            Vector2 anchorCenter = primarySideCenters[secondaryIndex];
            Vector2 anchorSize = primarySideSizes[secondaryIndex];
            Side directionSide = primarySideDirections[secondaryIndex];
            float direction = directionSide == Side.East ? 1.0f : -1.0f;
            Vector2 branchSize = GetLinearFinalSecondaryRoomSize(anchorSize, complexity01);
            float branchX = GetAdjacentCenter(anchorCenter.x, anchorSize.x, branchSize.x, direction);
            Vector2 branchCenter = new Vector2(branchX, anchorCenter.y);

            Room branchRoom = AddRoom(GetLinearFinalSecondaryRoomName(secondaryIndex + 1), branchCenter, branchSize, SecurityLevel.Restricted);
            ConnectRooms(anchor, branchRoom, directionSide, directionSide == Side.East ? Side.West : Side.East);

            sideRoomBudget--;
            secondaryIndex++;
        }

        playerSpawnRoomName = spawnRoom.name;
        primaryGoalRoomName = targetRoom.name;
        secondaryGoalRoomName = null;
        finalTargetRoom = targetRoom;
    }

    Vector2 GetLinearFinalSpineRoomSize(int index, int spineRoomCount, float complexity01)
    {
        float width = Random.Range(7.0f, 10.5f + complexity01 * 1.5f);
        float height = Random.Range(4.8f, 6.8f);

        if (index > 1 && index < spineRoomCount - 2 && Random.value < 0.28f + complexity01 * 0.12f)
            width += Random.Range(1.5f, 3.0f);

        return new Vector2(
            Mathf.Round(width * 2.0f) * 0.5f,
            Mathf.Round(height * 2.0f) * 0.5f
        );
    }

    Vector2 GetLinearFinalSideRoomSize(Vector2 anchorSize, float complexity01)
    {
        float width = Random.Range(4.0f, 6.5f + complexity01);
        float height = Random.Range(4.0f, Mathf.Max(4.25f, anchorSize.y - 0.4f));
        return new Vector2(
            Mathf.Round(width * 2.0f) * 0.5f,
            Mathf.Round(height * 2.0f) * 0.5f
        );
    }

    Vector2 GetLinearFinalSecondaryRoomSize(Vector2 anchorSize, float complexity01)
    {
        float width = Random.Range(3.5f, 5.5f + complexity01 * 0.5f);
        float height = Mathf.Clamp(anchorSize.y * Random.Range(0.75f, 0.95f), 3.5f, anchorSize.y);
        return new Vector2(
            Mathf.Round(width * 2.0f) * 0.5f,
            Mathf.Round(height * 2.0f) * 0.5f
        );
    }

    SecurityLevel GetLinearFinalSpineSecurity(int index, int spineRoomCount)
    {
        if (index <= 1)
            return Random.value < 0.45f ? SecurityLevel.Casual : SecurityLevel.Restricted;

        return SecurityLevel.Restricted;
    }

    SecurityLevel GetLinearFinalSideRoomSecurity(int spineIndex, int spineRoomCount)
    {
        if (spineIndex <= 2 && Random.value < 0.25f)
            return SecurityLevel.Casual;

        return SecurityLevel.Restricted;
    }

    string GetLinearFinalSpineRoomName(int index)
    {
        string[] names =
        {
            "Reception Gallery",
            "Grand Hall",
            "Security Checkpoint",
            "Private Gallery",
            "Service Crossing",
            "Inner Salon",
            "Vault Approach",
            "Executive Hall",
            "Observation Hall",
            "Final Antechamber"
        };

        return "L3 " + names[(index - 1) % names.Length] + " " + index.ToString("00");
    }

    string GetLinearFinalSideRoomName(int index)
    {
        string[] names =
        {
            "Records Room",
            "Guard Lounge",
            "Workshop",
            "Storage",
            "Library",
            "Private Study",
            "Security Office",
            "Guest Suite",
            "Wine Room",
            "Server Room"
        };

        return "L3 " + names[(index - 1) % names.Length] + " " + index.ToString("00");
    }

    string GetLinearFinalSecondaryRoomName(int index)
    {
        string[] names =
        {
            "Archive",
            "Utility Annex",
            "Equipment Room",
            "Staff Alcove",
            "Locked Study",
            "Side Vault"
        };

        return "L3 " + names[(index - 1) % names.Length] + " Annex " + index.ToString("00");
    }

    Side GetRandomSide()
    {
        int value = Random.Range(0, 4);
        if (value == 0) return Side.North;
        if (value == 1) return Side.East;
        if (value == 2) return Side.South;
        return Side.West;
    }

    SecurityLevel GetRandomFinalVillaSecurity(GridRoom parent)
    {
        float roll = Random.value;

        if (parent.security == SecurityLevel.Restricted)
            return SecurityLevel.Restricted;

        if (roll < 0.32f) return SecurityLevel.Casual;
        return SecurityLevel.Restricted;
    }

    Vector2Int GetRandomFinalVillaRoomSize(SecurityLevel security)
    {
        float sizeBias = security == SecurityLevel.Casual ? 1.2f : 0.86f;
        int width = Mathf.Clamp(Mathf.RoundToInt(Random.Range(2.0f, 6.6f) * sizeBias), 2, 8);
        int height = Mathf.Clamp(Mathf.RoundToInt(Random.Range(2.0f, 5.8f) * sizeBias), 2, 7);
        return new Vector2Int(width, height);
    }

    string GetFinalVillaRoomName(int index, SecurityLevel security)
    {
        string[] casualNames = { "Tea Room", "Gallery", "Dining Hall", "Conservatory", "Veranda", "Pool Lounge" };
        string[] restrictedNames = { "Library", "Private Study", "Guest Suite", "Staff Room", "Storage", "Observatory" };

        string[] names = casualNames;
        if (security == SecurityLevel.Restricted) names = restrictedNames;

        return names[Random.Range(0, names.Length)] + " " + index.ToString("00");
    }

    GridRoom CreateAttachedGridRoom(string name, GridRoom parent, Side side, int width, int height, SecurityLevel security)
    {
        int gx = parent.gx;
        int gy = parent.gy;

        if (side == Side.North || side == Side.South)
        {
            int minOffset = -width + 2;
            int maxOffset = parent.gw - 2;
            gx = parent.gx + Random.Range(minOffset, maxOffset + 1);
            gy = side == Side.North ? parent.gy - height : parent.gy + parent.gh;
        }
        else
        {
            int minOffset = -height + 2;
            int maxOffset = parent.gh - 2;
            gx = side == Side.West ? parent.gx - width : parent.gx + parent.gw;
            gy = parent.gy + Random.Range(minOffset, maxOffset + 1);
        }

        return new GridRoom(name, gx, gy, width, height, security);
    }

    bool IsGridRoomInside(GridRoom room, int cols, int rows)
    {
        return room.Left >= 1 && room.Top >= 1 && room.Right <= cols - 1 && room.Bottom <= rows - 1;
    }

    bool OverlapsAnyGridRoom(GridRoom room, List<GridRoom> existingRooms)
    {
        foreach (GridRoom existing in existingRooms)
        {
            if (GridRoomsOverlap(room, existing))
                return true;
        }

        return false;
    }

    bool GridRoomsOverlap(GridRoom a, GridRoom b)
    {
        return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    }

    bool GridRoomsTouch(GridRoom a, GridRoom b)
    {
        int verticalSpan = Mathf.Min(a.Bottom, b.Bottom) - Mathf.Max(a.Top, b.Top);
        int horizontalSpan = Mathf.Min(a.Right, b.Right) - Mathf.Max(a.Left, b.Left);
        bool horizontalTouch = (a.Right == b.Left || b.Right == a.Left) && verticalSpan >= 2;
        bool verticalTouch = (a.Bottom == b.Top || b.Bottom == a.Top) && horizontalSpan >= 2;
        return horizontalTouch || verticalTouch;
    }

    void AddExtraGridAdjacencies(List<GridRoom> gridRooms, List<GridConnection> gridConnections, HashSet<string> connectionKeys)
    {
        for (int i = 0; i < gridRooms.Count; i++)
        {
            for (int j = i + 1; j < gridRooms.Count; j++)
            {
                if (GridRoomsTouch(gridRooms[i], gridRooms[j]))
                    AddGridConnection(gridRooms[i], gridRooms[j], gridConnections, connectionKeys);
            }
        }
    }

    void AddGridConnection(GridRoom a, GridRoom b, List<GridConnection> gridConnections, HashSet<string> connectionKeys)
    {
        string key = GetGridConnectionKey(a, b);
        if (connectionKeys.Contains(key))
            return;

        connectionKeys.Add(key);
        gridConnections.Add(new GridConnection(a, b));
    }

    string GetGridConnectionKey(GridRoom a, GridRoom b)
    {
        string aKey = a.name;
        string bKey = b.name;
        return string.CompareOrdinal(aKey, bKey) < 0 ? aKey + "|" + bKey : bKey + "|" + aKey;
    }

    void AssignFinalVillaObjectives(List<GridRoom> gridRooms)
    {
        GridRoom spawn = gridRooms[0];
        GridRoom target = gridRooms[0];

        foreach (GridRoom room in gridRooms)
        {
            float centerY = room.gy + room.gh * 0.5f;
            float spawnCenterY = spawn.gy + spawn.gh * 0.5f;
            float targetCenterY = target.gy + target.gh * 0.5f;

            if (centerY > spawnCenterY)
                spawn = room;

            if (centerY < targetCenterY)
                target = room;
        }

        GridRoom safe = null;
        foreach (GridRoom room in gridRooms)
        {
            if (room == target)
                continue;

            if (safe == null)
            {
                safe = room;
                continue;
            }

            float roomCenterY = room.gy + room.gh * 0.5f;
            float safeCenterY = safe.gy + safe.gh * 0.5f;
            if (roomCenterY < safeCenterY)
                safe = room;
        }

        spawn.name = "Courtyard / Entrance";
        spawn.SetSecurity(SecurityLevel.Casual);
        target.name = "Target Room";
        target.SetSecurity(SecurityLevel.Restricted);

        if (safe != null)
        {
            safe.name = "Safe Room / Exit";
            safe.SetSecurity(SecurityLevel.Restricted);
        }
    }

    GridRoom FindGridRoomByName(List<GridRoom> gridRooms, string name)
    {
        foreach (GridRoom room in gridRooms)
        {
            if (room.name == name)
                return room;
        }

        return null;
    }

    void ConnectGridRooms(GridRoom a, GridRoom b)
    {
        if (a.Right == b.Left)
        {
            ConnectRooms(a.room, b.room, Side.East, Side.West);
            return;
        }

        if (b.Right == a.Left)
        {
            ConnectRooms(a.room, b.room, Side.West, Side.East);
            return;
        }

        if (a.Bottom == b.Top)
        {
            ConnectRooms(a.room, b.room, Side.South, Side.North);
            return;
        }

        if (b.Bottom == a.Top)
        {
            ConnectRooms(a.room, b.room, Side.North, Side.South);
        }
    }

    // Room AddRoom(string name, Vector2 center, Vector2 size, SecurityLevel security)
    // {
    //     Room r = new Room(name, center, size, security);
    //     rooms.Add(r);
    //     return r;
    // }

    Room AddRoom(string name, Vector2 center, Vector2 size, SecurityLevel security)
    {
        // 整張地圖等比例放大：
        // center 放大，讓房間之間的相對位置也放大
        // size 放大，讓每個房間面積變大
        Room r = new Room(
            name,
            center * layoutScale,
            size * layoutScale,
            security
        );

        rooms.Add(r);
        return r;
    }

    void ConnectRooms(Room a, Room b, Side sideA, Side sideB)
    {
        Vector3 doorPos = EstimateDoorPosition(a, b, sideA);
        connections.Add(new Connection(a, b, sideA, sideB, doorPos));
    }

    Vector3 EstimateDoorPosition(Room a, Room b, Side sideA)
    {
        float x = (a.center.x + b.center.x) * 0.5f;
        float z = (a.center.y + b.center.y) * 0.5f;

        if (sideA == Side.North)
        {
            x = GetSharedHorizontalWallCenter(a, b);
            z = a.Top;
        }
        else if (sideA == Side.South)
        {
            x = GetSharedHorizontalWallCenter(a, b);
            z = a.Bottom;
        }
        else if (sideA == Side.East)
        {
            x = a.Right;
            z = GetSharedVerticalWallCenter(a, b);
        }
        else if (sideA == Side.West)
        {
            x = a.Left;
            z = GetSharedVerticalWallCenter(a, b);
        }

        return new Vector3(x, 0, z);
    }

    float GetSharedHorizontalWallCenter(Room a, Room b)
    {
        float start = Mathf.Max(a.Left, b.Left);
        float end = Mathf.Min(a.Right, b.Right);

        if (end > start)
            return (start + end) * 0.5f;

        return (a.center.x + b.center.x) * 0.5f;
    }

    float GetSharedVerticalWallCenter(Room a, Room b)
    {
        float start = Mathf.Max(a.Bottom, b.Bottom);
        float end = Mathf.Min(a.Top, b.Top);

        if (end > start)
            return (start + end) * 0.5f;

        return (a.center.y + b.center.y) * 0.5f;
    }

    void BuildRoomGraph()
    {
        foreach (Room r in rooms)
        {
            if (!roomGraph.ContainsKey(r.name))
                roomGraph[r.name] = new List<string>();
        }

        foreach (Connection c in connections)
        {
            roomGraph[c.a.name].Add(c.b.name);
            roomGraph[c.b.name].Add(c.a.name);
        }
    }

    void ValidateReachability()
    {
        if (string.IsNullOrEmpty(playerSpawnRoomName) || string.IsNullOrEmpty(primaryGoalRoomName))
        {
            Debug.LogWarning("[VillaPCG] Reachability validation skipped because level endpoints are not configured.");
            return;
        }

        bool canReachTarget = IsReachable(playerSpawnRoomName, primaryGoalRoomName);

        if (!canReachTarget)
            Debug.LogWarning($"[VillaPCG] Invalid map: Player cannot reach {primaryGoalRoomName} from {playerSpawnRoomName}.");

        bool secondaryReachable = true;
        if (!string.IsNullOrEmpty(secondaryGoalRoomName))
        {
            secondaryReachable = IsReachable(primaryGoalRoomName, secondaryGoalRoomName);

            if (!secondaryReachable)
                Debug.LogWarning($"[VillaPCG] Invalid map: {primaryGoalRoomName} cannot reach {secondaryGoalRoomName}.");
        }

        if (canReachTarget && secondaryReachable)
            Debug.Log($"[VillaPCG] Level {currentLevel} reachability validation passed.");
    }

    void UpdateMapRuleSummary()
    {
        int casualCount = CountRoomsBySecurity(SecurityLevel.Casual);
        int restrictedCount = CountRoomsBySecurity(SecurityLevel.Restricted);
        int casualZoneCount = CountRoomsByLayoutZone(LayoutZone.Casual);
        int restrictedZoneCount = CountRoomsByLayoutZone(LayoutZone.Restricted);

        bool canReachPrimary = !string.IsNullOrEmpty(playerSpawnRoomName)
            && !string.IsNullOrEmpty(primaryGoalRoomName)
            && IsReachable(playerSpawnRoomName, primaryGoalRoomName);

        bool canReachSecondary = string.IsNullOrEmpty(secondaryGoalRoomName)
            || (!string.IsNullOrEmpty(primaryGoalRoomName) && IsReachable(primaryGoalRoomName, secondaryGoalRoomName));

        StringBuilder summary = new StringBuilder();
        summary.AppendLine($"Level {currentLevel} map PCG rules: seed={seed}, style={finalVillaStyle}, complexity={finalVillaComplexity}");
        summary.AppendLine($"rooms={rooms.Count}, connections={connections.Count}, roomGraphNodes={roomGraph.Count}");
        summary.AppendLine($"security: casual={casualCount}, restricted={restrictedCount}");
        summary.AppendLine($"layoutZones: casual={casualZoneCount}, restricted={restrictedZoneCount}");
        summary.AppendLine($"flow: spawn={GetSafeRuleName(playerSpawnRoomName)}, primaryGoal={GetSafeRuleName(primaryGoalRoomName)}, secondaryGoal={GetSafeRuleName(secondaryGoalRoomName)}");
        summary.AppendLine($"objectives: levelExit={GetSafeRuleName(levelExitRoom != null ? levelExitRoom.name : null)}, finalTarget={GetSafeRuleName(finalTargetRoom != null ? finalTargetRoom.name : null)}");
        summary.AppendLine($"reachability: spawnToPrimary={canReachPrimary}, primaryToSecondary={canReachSecondary}");

        lastMapRuleSummary = summary.ToString();

    #if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
    #endif
    }

    int CountRoomsBySecurity(SecurityLevel security)
    {
        int count = 0;
        foreach (Room room in rooms)
        {
            if (room.security == security)
                count++;
        }

        return count;
    }

    int CountRoomsByLayoutZone(LayoutZone layoutZone)
    {
        int count = 0;
        foreach (Room room in rooms)
        {
            if (room.layoutZone == layoutZone)
                count++;
        }

        return count;
    }

    string GetSafeRuleName(string value)
    {
        return string.IsNullOrEmpty(value) ? "none" : value;
    }

    bool IsReachable(string start, string goal)
    {
        if (!roomGraph.ContainsKey(start) || !roomGraph.ContainsKey(goal))
            return false;

        Queue<string> q = new Queue<string>();
        HashSet<string> visited = new HashSet<string>();

        q.Enqueue(start);
        visited.Add(start);

        while (q.Count > 0)
        {
            string cur = q.Dequeue();
            if (cur == goal)
                return true;

            foreach (string next in roomGraph[cur])
            {
                if (!visited.Contains(next))
                {
                    visited.Add(next);
                    q.Enqueue(next);
                }
            }
        }

        return false;
    }

    void BuildGeometry()
    {
        foreach (Room r in rooms)
        {
            CreateFloor(r);

            CreateWalls(r);

            if (showRoomLabels)
                CreateRoomLabel(r);
        }
    }

    void CreateFloor(Room r)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor_" + r.name;
        floor.transform.parent = transform;
        floor.transform.position = new Vector3(r.center.x, -floorThickness * 0.5f, r.center.y);
        floor.transform.localScale = new Vector3(r.size.x, floorThickness, r.size.y);

        floor.layer = GetWalkableLayerSafe();

        Renderer renderer = floor.GetComponent<Renderer>();
        renderer.material = IsRestricted(r) ? restrictedFloorMat : floorMat;

        if (r == finalTargetRoom)
        {
            CreateLayoutZoneMarker("Area_TargetRoom_" + r.name, floor.transform.position, floor.transform.localScale, GetTargetRoomLayerSafe(), transform);
            return;
        }

        CreateLayoutZoneMarker("Area_" + GetLayoutZoneNameToken(r.layoutZone) + "_" + r.name, floor.transform.position, floor.transform.localScale, GetAreaLayerSafe(r.layoutZone), transform);
    }

    bool IsRestricted(Room r)
    {
        return r.security == SecurityLevel.Restricted;
    }

    LayoutZone GetConnectionLayoutZone(Room a, Room b)
    {
        if ((a != null && a.layoutZone == LayoutZone.Restricted) || (b != null && b.layoutZone == LayoutZone.Restricted))
            return LayoutZone.Restricted;

        return LayoutZone.Casual;
    }

    string GetLayoutZoneNameToken(LayoutZone layoutZone)
    {
        return layoutZone == LayoutZone.Restricted ? "Restricted" : "Casual";
    }

    void CreateLayoutZoneMarker(string markerName, Vector3 floorCenter, Vector3 floorSize, int layer, Transform parent)
    {
        GameObject marker = new GameObject(markerName);
        marker.layer = layer;
        marker.transform.parent = parent;

        float markerHeight = Mathf.Max(0.01f, areaLayerMarkerHeight);
        marker.transform.position = new Vector3(floorCenter.x, markerHeight * 0.5f, floorCenter.z);

        BoxCollider collider = marker.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(Mathf.Abs(floorSize.x), markerHeight, Mathf.Abs(floorSize.z));
    }

    void CreateWalls(Room r)
    {
        CreateWallSide(r, Side.North);
        CreateWallSide(r, Side.South);
        CreateWallSide(r, Side.East);
        CreateWallSide(r, Side.West);
    }

    void CreateWallSide(Room r, Side side)
    {
        if (ShouldSkipWallSide(r, side))
            return;

        List<float> doorOffsets = GetDoorOffsetsForRoomSide(r, side);

        bool horizontal = side == Side.North || side == Side.South;
        float fullLength = horizontal ? r.size.x : r.size.y;
        float fixedCoord = 0;

        if (side == Side.North) fixedCoord = r.Top;
        if (side == Side.South) fixedCoord = r.Bottom;
        if (side == Side.East) fixedCoord = r.Right;
        if (side == Side.West) fixedCoord = r.Left;

        float start = -fullLength * 0.5f;
        float end = fullLength * 0.5f;

        doorOffsets.Sort();

        float cursor = start;

        foreach (float doorOffset in doorOffsets)
        {
            float gapStart = Mathf.Max(start, doorOffset - doorWidth * 0.5f);
            float gapEnd = Mathf.Min(end, doorOffset + doorWidth * 0.5f);

            if (gapStart > cursor)
                CreateWallSegment(r, side, cursor, gapStart, fixedCoord, horizontal);

            cursor = gapEnd;
        }

        if (cursor < end)
            CreateWallSegment(r, side, cursor, end, fixedCoord, horizontal);
    }

    //     bool ShouldSkipWallSide(Room r, Side side)
    // {
    //     foreach (Connection c in connections)
    //     {
    //         if (c.b != r || c.sideB != side)
    //             continue;

    //         // 只有真的共享同一條牆時，才跳過 B 的那面牆，避免雙層夾板。
    //         if (AreRoomsTouching(c))
    //             return true;
    //     }

    //     return false;
    // }

    bool ShouldSkipWallSide(Room r, Side side)
    {
        // v1 穩定版：每個房間都自己生成完整牆面。
        // 不再跳過 connection.b 的牆，避免 Kitchen / Storage 這類房間缺牆。
        return false;
    }

    bool AreRoomsTouching(Connection c)
    {
        Room a = c.a;
        Room b = c.b;

        float eps = wallThickness * 1.5f;

        if (c.sideA == Side.North && c.sideB == Side.South)
            return Mathf.Abs(a.Top - b.Bottom) < eps;

        if (c.sideA == Side.South && c.sideB == Side.North)
            return Mathf.Abs(a.Bottom - b.Top) < eps;

        if (c.sideA == Side.East && c.sideB == Side.West)
            return Mathf.Abs(a.Right - b.Left) < eps;

        if (c.sideA == Side.West && c.sideB == Side.East)
            return Mathf.Abs(a.Left - b.Right) < eps;

        return false;
    }

    bool AreRoomsSharingWall(Connection c)
    {
        Room a = c.a;
        Room b = c.b;

        float eps = wallThickness * 1.5f;

        if (c.sideA == Side.North && c.sideB == Side.South)
            return Mathf.Abs(a.Top - b.Bottom) < eps;

        if (c.sideA == Side.South && c.sideB == Side.North)
            return Mathf.Abs(a.Bottom - b.Top) < eps;

        if (c.sideA == Side.East && c.sideB == Side.West)
            return Mathf.Abs(a.Right - b.Left) < eps;

        if (c.sideA == Side.West && c.sideB == Side.East)
            return Mathf.Abs(a.Left - b.Right) < eps;

        return false;
    }

    // List<float> GetDoorOffsetsForRoomSide(Room r, Side side)
    // {
    //     List<float> offsets = new List<float>();

    //     foreach (Connection c in connections)
    //     {
    //         if (c.a == r && c.sideA == side)
    //         {
    //             offsets.Add(GetLocalDoorOffset(r, side, c.doorWorldPos));
    //         }
    //         else if (c.b == r && c.sideB == side)
    //         {
    //             if (!ShouldSkipWallSide(r, side))
    //                 offsets.Add(GetLocalDoorOffset(r, side, c.doorWorldPos));
    //         }
    //     }

    //     return offsets;
    // }

    List<float> GetDoorOffsetsForRoomSide(Room r, Side side)
    {
        List<float> offsets = new List<float>();

        foreach (Connection c in connections)
        {
            if (c.a == r && c.sideA == side)
            {
                offsets.Add(GetLocalDoorOffset(r, side, c.doorWorldPos));
            }
            else if (c.b == r && c.sideB == side)
            {
                offsets.Add(GetLocalDoorOffset(r, side, c.doorWorldPos));
            }
        }

        return offsets;
    }

    float GetLocalDoorOffset(Room r, Side side, Vector3 doorWorldPos)
    {
        if (side == Side.North || side == Side.South)
            return doorWorldPos.x - r.center.x;
        else
            return doorWorldPos.z - r.center.y;
    }

    void CreateWallSegment(Room r, Side side, float localStart, float localEnd, float fixedCoord, bool horizontal)
    {
        // 牆段兩端略微縮短，避免 T 字交界 / 房間交界處穿模凸出
        float shrink = wallThickness * 0.5f;
        localStart += shrink;
        localEnd -= shrink;

        float segmentLength = localEnd - localStart;
        if (segmentLength <= 0.05f)
            return;

        Vector3 position;
        Vector3 scale;

        if (horizontal)
        {
            float centerX = r.center.x + (localStart + localEnd) * 0.5f;
            position = new Vector3(centerX, wallHeight * 0.5f, fixedCoord);
            scale = new Vector3(segmentLength, wallHeight, wallThickness);
        }
        else
        {
            float centerZ = r.center.y + (localStart + localEnd) * 0.5f;
            position = new Vector3(fixedCoord, wallHeight * 0.5f, centerZ);
            scale = new Vector3(wallThickness, wallHeight, segmentLength);
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall_" + r.name + "_" + side;

        wall.layer = GetUnwalkableLayerSafe();

        wall.transform.parent = transform;
        wall.transform.position = position;
        wall.transform.localScale = scale;

        Renderer renderer = wall.GetComponent<Renderer>();
        renderer.material = wallMat;
    }

    void CreateRoomLabel(Room r)
    {
        GameObject labelObj = new GameObject("Label_" + r.name);
        labelObj.transform.parent = transform;
        labelObj.transform.position = new Vector3(r.center.x, 0.05f, r.center.y);

        TextMesh text = labelObj.AddComponent<TextMesh>();
        text.text = r == finalTargetRoom
            ? r.name + "\nTarget"
            : r.name + "\n" + r.security.ToString();
            // GetLayoutZoneLabel(r.layoutZone) + "\n" + 
        text.characterSize = 0.45f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.black;

        labelObj.transform.rotation = Quaternion.Euler(90, 0, 0);
    }

    void GenerateWaypoints()
    {
        GameObject wpRoot = new GameObject("Generated_Waypoints");
        wpRoot.transform.parent = transform;

        foreach (Room r in rooms)
        {
            Vector3 roomCenter = new Vector3(r.center.x, 0.05f, r.center.y);
            waypoints.Add(roomCenter);

            GameObject wp = new GameObject("WP_Room_" + r.name);
            wp.transform.parent = wpRoot.transform;
            wp.transform.position = roomCenter;
        }

        foreach (Connection c in connections)
        {
            Vector3 door = c.doorWorldPos + Vector3.up * 0.05f;
            waypoints.Add(door);

            GameObject wp = new GameObject("WP_Door_" + c.a.name + "_to_" + c.b.name);
            wp.transform.parent = wpRoot.transform;
            wp.transform.position = door;
        }
    }

    void CreatePlayerSpawn()
    {
        GameObject spawn = new GameObject("PlayerSpawn");
        spawn.transform.parent = transform;

        Room spawnRoom = rooms.Find(r => r.name == playerSpawnRoomName);
        if (spawnRoom != null)
        {
            float inset = Mathf.Min(2.0f * layoutScale, spawnRoom.size.y * 0.35f);
            spawn.transform.position = new Vector3(spawnRoom.center.x, 0.5f, spawnRoom.Bottom + inset);
            spawn.transform.rotation = Quaternion.identity;
        }
        else
        {
            spawn.transform.position = new Vector3(0, 0.5f, -12);
            spawn.transform.rotation = Quaternion.identity;
        }

        currentSpawnTransform = spawn.transform;
    }

    void CreateLevelSpecificObjects()
    {
        if (currentLevel < finalPcgLevel && levelExitRoom != null)
        {
            CreateLevelExitRoomObjects(levelExitRoom, currentLevel + 1);
        }

        if (currentLevel >= finalPcgLevel && finalTargetRoom != null)
        {
            CreateFinalTargetObject(finalTargetRoom);
        }
    }

    void GenerateEnemyLayout()
    {
        lastEnemyPlacementSummary = "";
        generatedEnemyGizmoPositions.Clear();

        if (!generateEnemyAgents)
            return;

        GameObject prefab = GetEnemyAgentPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[VillaPCG] Enemy auto layout skipped because enemyAgentPrefab is missing.");
            return;
        }

        RouteManager routeManager = EnsureRouteManager();
        if (routeManager == null)
        {
            Debug.LogWarning("[VillaPCG] Enemy auto layout skipped because RouteManager could not be created.");
            return;
        }

        GameObject routeRoot = new GameObject("Generated_EnemyRoutes");
        routeRoot.transform.parent = transform;

        GameObject enemyRoot = new GameObject("Generated_EnemyAgents");
        enemyRoot.transform.parent = transform;

        List<EnemyPlacementRecord> placements = new List<EnemyPlacementRecord>();
        List<Room> patrolRooms = GetEnemyCandidateRooms(true);
        SortRoomsByEnemyPlacementScore(patrolRooms, true);

        int patrolBudget = GetPatrolEnemyBudget();
        int generatedPatrolCount = 0;
        HashSet<Room> loopPatrolRooms = new HashSet<Room>();
        for (int i = 0; i < patrolRooms.Count && generatedPatrolCount < patrolBudget; i++)
        {
            Room room = patrolRooms[i];
            string routeName = generatedEnemyRoutePrefix + "Patrol_" + (generatedPatrolCount + 1).ToString("00");
            RouteDefinition route = CreatePatrolRoute(routeManager, routeRoot.transform, routeName, room);

            if (route.waypoints.Count > 0 && route.waypoints[0].point != null)
            {
                Vector3 spawnPosition = route.waypoints[0].point.position + Vector3.up * enemySpawnHeight;
                Quaternion spawnRotation = Quaternion.LookRotation(GetRoomFacingDirection(room, spawnPosition), Vector3.up);
                GameObject enemy = CreateEnemyInstance(prefab, "patrolEnemyTest_PCG_" + (generatedPatrolCount + 1).ToString("00"), spawnPosition, spawnRotation, enemyRoot.transform);
                ConfigureEnemyAgent(enemy, route.routeName, AgentDecision.PATROL);
                RecordEnemyPlacement(placements, enemy.name, route.routeName, room, spawnPosition, GetRoomPlacementReason(room, true));
                loopPatrolRooms.Add(room);
                generatedPatrolCount++;
            }
        }

        HashSet<Room> roomsUsedByPairPatrol = new HashSet<Room>();
        List<RoomPairPatrolCandidate> pairPatrolCandidates = BuildRoomPairPatrolCandidates();
        int generatedPairPatrolCount = 0;
        for (int i = 0; i < pairPatrolCandidates.Count && generatedPairPatrolCount < roomPairPatrolEnemyCount; i++)
        {
            RoomPairPatrolCandidate candidate = pairPatrolCandidates[i];
            if (roomsUsedByPairPatrol.Contains(candidate.roomA) || roomsUsedByPairPatrol.Contains(candidate.roomB))
                continue;

            string routeName = generatedEnemyRoutePrefix + "PairPatrol_" + (generatedPairPatrolCount + 1).ToString("00");
            RouteDefinition route = CreateRoomPairPatrolRoute(routeManager, routeRoot.transform, routeName, candidate);

            if (route.waypoints.Count > 0 && route.waypoints[0].point != null)
            {
                Vector3 spawnPosition = route.waypoints[0].point.position + Vector3.up * enemySpawnHeight;
                Quaternion spawnRotation = Quaternion.LookRotation(GetRoomFacingDirection(candidate.roomB, spawnPosition), Vector3.up);
                GameObject enemy = CreateEnemyInstance(prefab, "patrolEnemyTest_PCG_" + (generatedPatrolCount + 1).ToString("00"), spawnPosition, spawnRotation, enemyRoot.transform);
                ConfigureEnemyAgent(enemy, route.routeName, AgentDecision.PATROL);
                RecordEnemyPlacement(placements, enemy.name, route.routeName, candidate.roomA, spawnPosition, candidate.reason);
                roomsUsedByPairPatrol.Add(candidate.roomA);
                roomsUsedByPairPatrol.Add(candidate.roomB);
                generatedPairPatrolCount++;
                generatedPatrolCount++;
            }
        }

        List<StandingGuardCandidate> standCandidates = BuildStandingGuardCandidates(loopPatrolRooms);
        int standCount = Mathf.Min(GetStandEnemyBudget(), standCandidates.Count);
        for (int i = 0; i < standCount; i++)
        {
            StandingGuardCandidate candidate = standCandidates[i];
            Vector3 spawnPosition = candidate.position + Vector3.up * enemySpawnHeight;
            string routeName = generatedEnemyRoutePrefix + "Stand_" + (i + 1).ToString("00");
            Room containingRoom = candidate.room != null ? candidate.room : FindRoomContainingPoint(spawnPosition);
            CreateSinglePointRoute(routeManager, routeRoot.transform, routeName, spawnPosition, containingRoom);

            Vector3 facing = containingRoom != null ? GetRoomFacingDirection(containingRoom, spawnPosition) : Vector3.forward;
            GameObject enemy = CreateEnemyInstance(prefab, "standEnemyTest_PCG_" + (i + 1).ToString("00"), spawnPosition, Quaternion.LookRotation(facing, Vector3.up), enemyRoot.transform);
            ConfigureEnemyAgent(enemy, routeName, AgentDecision.LONGREST);
            RecordEnemyPlacement(placements, enemy.name, routeName, containingRoom, spawnPosition, candidate.reason);
        }

        routeManager.RebuildRouteDictionary();
        MarkRouteManagerDirty(routeManager);
        UpdateEnemyPlacementSummary(placements);
    }

    GameObject GetEnemyAgentPrefab()
    {
        if (enemyAgentPrefab != null)
            return enemyAgentPrefab;

    #if UNITY_EDITOR
        enemyAgentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agent_v2.prefab");
        if (enemyAgentPrefab != null)
            EditorUtility.SetDirty(this);
    #endif

        return enemyAgentPrefab;
    }

    RouteManager EnsureRouteManager()
    {
        RouteManager routeManager = Object.FindFirstObjectByType<RouteManager>();
        if (routeManager != null)
            return routeManager;

        GameObject routeManagerObject = new GameObject("Generated_RouteManager");
        routeManagerObject.transform.parent = transform;
        return routeManagerObject.AddComponent<RouteManager>();
    }

    void ClearGeneratedEnemyRoutes()
    {
        RouteManager routeManager = Object.FindFirstObjectByType<RouteManager>();
        if (routeManager == null || routeManager.allRoutes == null)
            return;

        int removed = routeManager.allRoutes.RemoveAll(IsGeneratedEnemyRoute);
        if (removed <= 0)
            return;

        routeManager.RebuildRouteDictionary();
        MarkRouteManagerDirty(routeManager);
    }

    bool IsGeneratedEnemyRoute(RouteDefinition route)
    {
        if (route == null || string.IsNullOrEmpty(route.routeName))
            return false;

        if ((!string.IsNullOrEmpty(generatedEnemyRoutePrefix) && route.routeName.StartsWith(generatedEnemyRoutePrefix, System.StringComparison.Ordinal))
            || route.routeName.StartsWith("PCG_Enemy_", System.StringComparison.Ordinal))
        {
            return true;
        }

        if (route.waypoints == null)
            return false;

        foreach (WaypointInfo waypoint in route.waypoints)
        {
            if (waypoint != null && waypoint.point != null && waypoint.point.IsChildOf(transform))
                return true;
        }

        return false;
    }

    int GetPatrolEnemyBudget()
    {
        return Mathf.Max(0, GetEnemyCountForCurrentLevel(level1PatrolEnemyCount, level2PatrolEnemyCount, level3PatrolEnemyCount));
    }

    int GetStandEnemyBudget()
    {
        return Mathf.Max(0, GetEnemyCountForCurrentLevel(level1StandEnemyCount, level2StandEnemyCount, level3StandEnemyCount));
    }

    int GetEnemyCountForCurrentLevel(int level1Count, int level2Count, int level3Count)
    {
        if (currentLevel <= 1)
            return level1Count;

        if (currentLevel == 2)
            return level2Count;

        return level3Count;
    }

    List<Room> GetEnemyCandidateRooms(bool patrolOnly)
    {
        List<Room> candidates = new List<Room>();

        foreach (Room room in rooms)
        {
            if (ShouldSkipEnemyRoom(room))
                continue;

            if (!CanGenerateEnemyInRoom(room))
                continue;

            int securityWeight = GetSecurityWeight(room.security);
            if (patrolOnly && securityWeight < GetSecurityWeight(SecurityLevel.Restricted))
                continue;

            if (!patrolOnly && securityWeight < GetSecurityWeight(SecurityLevel.Restricted))
                continue;

            candidates.Add(room);
        }

        if (candidates.Count > 0)
            return candidates;

        foreach (Room room in rooms)
        {
            if (!ShouldSkipEnemyRoom(room) && CanGenerateEnemyInRoom(room))
                candidates.Add(room);
        }

        return candidates;
    }

    void SortRoomsByEnemyPlacementScore(List<Room> candidates, bool patrolOnly)
    {
        candidates.Sort((a, b) => CompareRoomsForEnemyPlacement(a, b, patrolOnly));
    }

    int CompareRoomsForEnemyPlacement(Room a, Room b, bool patrolOnly)
    {
        int scoreCompare = CalculateEnemyRoomScore(b, patrolOnly).CompareTo(CalculateEnemyRoomScore(a, patrolOnly));
        if (scoreCompare != 0)
            return scoreCompare;

        int nameCompare = string.Compare(a.name, b.name, System.StringComparison.Ordinal);
        if (nameCompare != 0)
            return nameCompare;

        int xCompare = a.center.x.CompareTo(b.center.x);
        if (xCompare != 0)
            return xCompare;

        return a.center.y.CompareTo(b.center.y);
    }

    float CalculateEnemyRoomScore(Room room, bool patrolOnly)
    {
        if (room == null)
            return float.MinValue;

        float score = 0f;
        score += GetSecurityWeight(room.security) * 100f;
        score += Mathf.Clamp(GetRoomDepthFromSpawn(room.name), 0, 12) * 12f;
        score += Mathf.Sqrt(Mathf.Max(1f, room.size.x * room.size.y)) * (patrolOnly ? 1.8f : 1.0f);

        if (finalTargetRoom == room || room.name == primaryGoalRoomName)
            score += 55f;

        if (!string.IsNullOrEmpty(secondaryGoalRoomName) && room.name == secondaryGoalRoomName)
            score += 25f;

        if (IsNearPlayerSpawn(room.center))
            score -= 200f;

        return score;
    }

    string GetRoomPlacementReason(Room room, bool patrolOnly)
    {
        if (room == null)
            return "unknown room";

        string role = patrolOnly ? "patrol" : "stand";
        return $"RoomThreatScore/{role}: zone={GetLayoutZoneLabel(room.layoutZone)}, security={room.security}, graphDepth={GetRoomDepthFromSpawn(room.name)}, score={CalculateEnemyRoomScore(room, patrolOnly):0.0}";
    }

    int GetRoomDepthFromSpawn(string roomName)
    {
        if (string.IsNullOrEmpty(playerSpawnRoomName) || string.IsNullOrEmpty(roomName))
            return 0;

        if (!roomGraph.ContainsKey(playerSpawnRoomName) || !roomGraph.ContainsKey(roomName))
            return 0;

        Queue<string> queue = new Queue<string>();
        Dictionary<string, int> depths = new Dictionary<string, int>();
        queue.Enqueue(playerSpawnRoomName);
        depths[playerSpawnRoomName] = 0;

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (current == roomName)
                return depths[current];

            foreach (string next in roomGraph[current])
            {
                if (depths.ContainsKey(next))
                    continue;

                depths[next] = depths[current] + 1;
                queue.Enqueue(next);
            }
        }

        return 0;
    }

    bool IsNearPlayerSpawn(Vector2 point)
    {
        Room spawnRoom = rooms.Find(r => r.name == playerSpawnRoomName);
        if (spawnRoom == null)
            return false;

        return Vector2.Distance(spawnRoom.center, point) < enemySpawnExclusionRadius;
    }

    bool ShouldSkipEnemyRoom(Room room)
    {
        if (room == null)
            return true;

        if (room.name == playerSpawnRoomName)
            return true;

        if (room == levelExitRoom)
            return true;

        return false;
    }

    bool CanGenerateEnemyInRoom(Room room)
    {
        return room != null && room.layoutZone == LayoutZone.Restricted;
    }

    int GetSecurityWeight(SecurityLevel security)
    {
        switch (security)
        {
            case SecurityLevel.Restricted:
                return 2;
            default:
                return 0;
        }
    }

    void ShuffleRooms(List<Room> roomList)
    {
        for (int i = 0; i < roomList.Count; i++)
        {
            int swapIndex = Random.Range(i, roomList.Count);
            Room temp = roomList[i];
            roomList[i] = roomList[swapIndex];
            roomList[swapIndex] = temp;
        }
    }

    RouteDefinition CreatePatrolRoute(RouteManager routeManager, Transform routeRoot, string routeName, Room room)
    {
        RouteDefinition route = new RouteDefinition
        {
            routeName = routeName,
            isLoop = true
        };

        string roomToken = SanitizeNameToken(room.name);
        Vector3[] patrolPoints = GetPatrolPointsForRoom(room);
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            Transform point = CreateRoutePoint(routeRoot, routeName + "_" + roomToken + "_WP_" + (i + 1).ToString("00"), patrolPoints[i]);
            route.waypoints.Add(new WaypointInfo
            {
                point = point,
                waitTime = patrolWaypointWaitTime
            });
        }

        routeManager.allRoutes.Add(route);
        return route;
    }

    void CreateSinglePointRoute(RouteManager routeManager, Transform routeRoot, string routeName, Vector3 position, Room room)
    {
        RouteDefinition route = new RouteDefinition
        {
            routeName = routeName,
            isLoop = false
        };

        string roomToken = room != null ? SanitizeNameToken(room.name) : "UnknownRoom";
        route.waypoints.Add(new WaypointInfo
        {
            point = CreateRoutePoint(routeRoot, routeName + "_" + roomToken + "_Hold", position),
            waitTime = 999f
        });

        routeManager.allRoutes.Add(route);
    }

    string SanitizeNameToken(string source)
    {
        if (string.IsNullOrEmpty(source))
            return "Unnamed";

        StringBuilder builder = new StringBuilder(source.Length);
        foreach (char character in source)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
            else if (character == '_' || character == '-')
                builder.Append(character);
            else if (char.IsWhiteSpace(character))
                builder.Append('_');
        }

        return builder.Length > 0 ? builder.ToString() : "Unnamed";
    }

    Transform CreateRoutePoint(Transform routeRoot, string pointName, Vector3 position)
    {
        GameObject point = new GameObject(pointName);
        point.transform.parent = routeRoot;
        point.transform.position = new Vector3(position.x, 0.05f, position.z);
        return point.transform;
    }

    List<RoomPairPatrolCandidate> BuildRoomPairPatrolCandidates()
    {
        List<RoomPairPatrolCandidate> candidates = new List<RoomPairPatrolCandidate>();

        foreach (Connection connection in connections)
        {
            if (connection == null || !CanGenerateEnemyInRoom(connection.a) || !CanGenerateEnemyInRoom(connection.b))
                continue;

            Vector3 anchorA = GetRoomPairPatrolAnchor(connection.doorWorldPos, connection.a);
            Vector3 anchorB = GetRoomPairPatrolAnchor(connection.doorWorldPos, connection.b);
            if (Vector3.Distance(anchorA, anchorB) < 1.5f)
                continue;

            RoomPairPatrolCandidate candidate = new RoomPairPatrolCandidate
            {
                connection = connection,
                roomA = connection.a,
                roomB = connection.b,
                anchorA = anchorA,
                anchorB = anchorB
            };
            candidate.score = CalculateRoomPairPatrolScore(candidate);
            candidate.reason = GetRoomPairPatrolReason(candidate);
            candidates.Add(candidate);
        }

        candidates.Sort(CompareRoomPairPatrolCandidates);
        return candidates;
    }

    Vector3 GetRoomPairPatrolAnchor(Vector3 doorPosition, Room room)
    {
        Vector3 towardRoom = new Vector3(room.center.x - doorPosition.x, 0f, room.center.y - doorPosition.z);
        if (towardRoom.sqrMagnitude < 0.01f)
            towardRoom = new Vector3(room.center.x, 0f, room.center.y).normalized;

        Vector3 anchor = doorPosition + towardRoom.normalized * roomPairPatrolAnchorInset;
        return ClampPointInsideRoom(anchor, room);
    }

    float CalculateRoomPairPatrolScore(RoomPairPatrolCandidate candidate)
    {
        if (candidate == null || candidate.roomA == null || candidate.roomB == null)
            return float.MinValue;

        float score = CalculateEnemyRoomScore(candidate.roomA, true) + CalculateEnemyRoomScore(candidate.roomB, true);
        score *= 0.5f;
        score += Vector3.Distance(candidate.anchorA, candidate.anchorB) * 2.0f;

        if (candidate.roomA.name == primaryGoalRoomName || candidate.roomB.name == primaryGoalRoomName)
            score += 35f;

        if (finalTargetRoom == candidate.roomA || finalTargetRoom == candidate.roomB)
            score += 35f;

        if (IsNearPlayerSpawn(new Vector2(candidate.anchorA.x, candidate.anchorA.z))
            || IsNearPlayerSpawn(new Vector2(candidate.anchorB.x, candidate.anchorB.z)))
        {
            score -= 160f;
        }

        return score;
    }

    int CompareRoomPairPatrolCandidates(RoomPairPatrolCandidate a, RoomPairPatrolCandidate b)
    {
        int scoreCompare = b.score.CompareTo(a.score);
        if (scoreCompare != 0)
            return scoreCompare;

        int roomACompare = string.Compare(a.roomA.name, b.roomA.name, System.StringComparison.Ordinal);
        if (roomACompare != 0)
            return roomACompare;

        return string.Compare(a.roomB.name, b.roomB.name, System.StringComparison.Ordinal);
    }

    string GetRoomPairPatrolReason(RoomPairPatrolCandidate candidate)
    {
        if (candidate == null || candidate.roomA == null || candidate.roomB == null)
            return "RoomPairPatrolScore: unknown room pair";

        return $"RoomPairPatrolScore/patrol: rooms={candidate.roomA.name}<->{candidate.roomB.name}, zone={GetLayoutZoneLabel(candidate.roomA.layoutZone)}, wait={roomPairPatrolAnchorWaitTime:0.0}, score={candidate.score:0.0}";
    }

    RouteDefinition CreateRoomPairPatrolRoute(RouteManager routeManager, Transform routeRoot, string routeName, RoomPairPatrolCandidate candidate)
    {
        RouteDefinition route = new RouteDefinition
        {
            routeName = routeName,
            isLoop = true
        };

        string roomAToken = SanitizeNameToken(candidate.roomA.name);
        string roomBToken = SanitizeNameToken(candidate.roomB.name);
        string pairToken = roomAToken + "_to_" + roomBToken;

        route.waypoints.Add(new WaypointInfo
        {
            point = CreateRoutePoint(routeRoot, routeName + "_" + pairToken + "_WP_01", candidate.anchorA),
            waitTime = roomPairPatrolAnchorWaitTime
        });
        route.waypoints.Add(new WaypointInfo
        {
            point = CreateRoutePoint(routeRoot, routeName + "_" + pairToken + "_WP_02", candidate.anchorB),
            waitTime = roomPairPatrolAnchorWaitTime
        });

        routeManager.allRoutes.Add(route);
        return route;
    }

    Vector3[] GetPatrolPointsForRoom(Room room)
    {
        float routeInset = Mathf.Max(enemyRoomInset, patrolRouteCornerAvoidance);
        float insetX = Mathf.Min(routeInset, room.size.x * 0.4f);
        float insetZ = Mathf.Min(routeInset, room.size.y * 0.4f);
        float left = room.Left + insetX;
        float right = room.Right - insetX;
        float bottom = room.Bottom + insetZ;
        float top = room.Top - insetZ;

        if (right <= left)
            left = right = room.center.x;

        if (top <= bottom)
            bottom = top = room.center.y;

        if (Mathf.Abs(right - left) < 0.5f || Mathf.Abs(top - bottom) < 0.5f)
        {
            return new[]
            {
                new Vector3(room.center.x, 0.05f, room.center.y)
            };
        }

        return new[]
        {
            new Vector3(room.center.x, 0.05f, bottom),
            new Vector3(right, 0.05f, room.center.y),
            new Vector3(room.center.x, 0.05f, top),
            new Vector3(left, 0.05f, room.center.y)
        };
    }

    List<StandingGuardCandidate> BuildStandingGuardCandidates(HashSet<Room> loopPatrolRooms)
    {
        List<StandingGuardCandidate> candidates = new List<StandingGuardCandidate>();

        if (loopPatrolRooms != null)
        {
            foreach (Room room in loopPatrolRooms)
            {
                if (ShouldSkipEnemyRoom(room) || !CanGenerateEnemyInRoom(room))
                    continue;

                AddStandingCornerCandidatesForPatrolRoom(candidates, room);
            }
        }

        foreach (Connection connection in connections)
        {
            Room guardRoom = GetMoreSecureRoom(connection.a, connection.b);
            if (ShouldSkipEnemyRoom(guardRoom) || !CanGenerateEnemyInRoom(guardRoom))
                continue;

            if (loopPatrolRooms != null && loopPatrolRooms.Contains(guardRoom))
                continue;

            Vector3 door = connection.doorWorldPos;
            Vector3 towardRoom = new Vector3(guardRoom.center.x - door.x, 0f, guardRoom.center.y - door.z);
            if (towardRoom.sqrMagnitude < 0.01f)
                towardRoom = Vector3.forward;

            Vector3 position = door + towardRoom.normalized * standingGuardDoorOffset;
            position = ClampPointInsideRoom(position, guardRoom);
            AddStandingCandidateIfSpaced(candidates, new StandingGuardCandidate
            {
                room = guardRoom,
                position = position,
                score = CalculateStandingGuardScore(connection, guardRoom, position),
                reason = GetStandingGuardReason(connection, guardRoom, position)
            });
        }

        List<Room> fallbackRooms = GetEnemyCandidateRooms(false);
        SortRoomsByEnemyPlacementScore(fallbackRooms, false);
        int standBudget = GetStandEnemyBudget();
        foreach (Room room in fallbackRooms)
        {
            if (candidates.Count >= standBudget)
                break;

            if (loopPatrolRooms != null && loopPatrolRooms.Contains(room))
                continue;

            Vector3 position = new Vector3(room.center.x, 0.05f, room.center.y);
            AddStandingCandidateIfSpaced(candidates, new StandingGuardCandidate
            {
                room = room,
                position = position,
                score = CalculateEnemyRoomScore(room, false) - 30f,
                reason = GetRoomPlacementReason(room, false) + ", FallbackRoomCenter"
            });
        }

        candidates.Sort(CompareStandingGuardCandidates);
        return candidates;
    }

    void AddStandingCornerCandidatesForPatrolRoom(List<StandingGuardCandidate> candidates, Room room)
    {
        Vector3[] corners = GetStandingCornerPointsForRoom(room);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 position = corners[i];
            AddStandingCandidateIfSpaced(candidates, new StandingGuardCandidate
            {
                room = room,
                position = position,
                score = CalculateEnemyRoomScore(room, false) + 20f - i,
                reason = GetPatrolRoomCornerStandingReason(room, i, position)
            });
        }
    }

    Vector3[] GetStandingCornerPointsForRoom(Room room)
    {
        float marginX = Mathf.Min(enemyRoomInset, room.size.x * 0.35f);
        float marginZ = Mathf.Min(enemyRoomInset, room.size.y * 0.35f);
        float left = room.Left + marginX;
        float right = room.Right - marginX;
        float bottom = room.Bottom + marginZ;
        float top = room.Top - marginZ;

        if (right <= left)
            left = right = room.center.x;

        if (top <= bottom)
            bottom = top = room.center.y;

        return new[]
        {
            new Vector3(left, 0.05f, bottom),
            new Vector3(right, 0.05f, bottom),
            new Vector3(right, 0.05f, top),
            new Vector3(left, 0.05f, top)
        };
    }

    string GetPatrolRoomCornerStandingReason(Room room, int cornerIndex, Vector3 position)
    {
        return $"PatrolRoomCornerStand: room={room.name}, corner={cornerIndex + 1}, zone={GetLayoutZoneLabel(room.layoutZone)}, security={room.security}, score={CalculateEnemyRoomScore(room, false) + 20f - cornerIndex:0.0}";
    }

    int CompareStandingGuardCandidates(StandingGuardCandidate a, StandingGuardCandidate b)
    {
        int scoreCompare = b.score.CompareTo(a.score);
        if (scoreCompare != 0)
            return scoreCompare;

        string aRoomName = a.room != null ? a.room.name : "";
        string bRoomName = b.room != null ? b.room.name : "";
        int roomCompare = string.Compare(aRoomName, bRoomName, System.StringComparison.Ordinal);
        if (roomCompare != 0)
            return roomCompare;

        int xCompare = a.position.x.CompareTo(b.position.x);
        if (xCompare != 0)
            return xCompare;

        return a.position.z.CompareTo(b.position.z);
    }

    float CalculateStandingGuardScore(Connection connection, Room guardRoom, Vector3 position)
    {
        float score = CalculateEnemyRoomScore(guardRoom, false);
        Room otherRoom = connection.a == guardRoom ? connection.b : connection.a;
        score += Mathf.Abs(GetSecurityWeight(guardRoom.security) - GetSecurityWeight(otherRoom.security)) * 35f;

        if (connection.a.name == primaryGoalRoomName || connection.b.name == primaryGoalRoomName)
            score += 30f;

        if (IsNearPlayerSpawn(new Vector2(position.x, position.z)))
            score -= 150f;

        return score;
    }

    string GetStandingGuardReason(Connection connection, Room guardRoom, Vector3 position)
    {
        Room otherRoom = connection.a == guardRoom ? connection.b : connection.a;
        return $"DoorGuardScore: guards {guardRoom.name} door from {otherRoom.name}, zone={GetLayoutZoneLabel(guardRoom.layoutZone)}, security={guardRoom.security}, graphDepth={GetRoomDepthFromSpawn(guardRoom.name)}, score={CalculateStandingGuardScore(connection, guardRoom, position):0.0}";
    }

    Room GetMoreSecureRoom(Room a, Room b)
    {
        if (GetSecurityWeight(a.security) >= GetSecurityWeight(b.security))
            return a;

        return b;
    }

    Vector3 ClampPointInsideRoom(Vector3 point, Room room)
    {
        float marginX = Mathf.Min(enemyRoomInset, room.size.x * 0.35f);
        float marginZ = Mathf.Min(enemyRoomInset, room.size.y * 0.35f);

        return new Vector3(
            Mathf.Clamp(point.x, room.Left + marginX, room.Right - marginX),
            0.05f,
            Mathf.Clamp(point.z, room.Bottom + marginZ, room.Top - marginZ)
        );
    }

    void AddStandingCandidateIfSpaced(List<StandingGuardCandidate> candidates, StandingGuardCandidate candidate)
    {
        const float minSpacing = 2.5f;
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 delta = candidates[i].position - candidate.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < minSpacing * minSpacing)
                return;
        }

        candidates.Add(candidate);
    }

    Room FindRoomContainingPoint(Vector3 point)
    {
        foreach (Room room in rooms)
        {
            if (point.x >= room.Left && point.x <= room.Right && point.z >= room.Bottom && point.z <= room.Top)
                return room;
        }

        return null;
    }

    Vector3 GetRoomFacingDirection(Room room, Vector3 position)
    {
        Vector3 direction = new Vector3(room.center.x - position.x, 0f, room.center.y - position.z);
        if (direction.sqrMagnitude < 0.01f)
            direction = Vector3.forward;

        return direction.normalized;
    }

    GameObject CreateEnemyInstance(GameObject prefab, string enemyName, Vector3 position, Quaternion rotation, Transform parent)
    {
        GameObject enemy;

    #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            enemy = PrefabUtility.InstantiatePrefab(prefab, gameObject.scene) as GameObject;
            if (enemy == null)
                enemy = Instantiate(prefab);
        }
        else
    #endif
        {
            enemy = Instantiate(prefab);
        }

        enemy.name = enemyName;
        enemy.transform.parent = parent;
        enemy.transform.SetPositionAndRotation(position, rotation);
        return enemy;
    }

    void ConfigureEnemyAgent(GameObject enemy, string routeName, AgentDecision decision)
    {
        if (enemy == null)
            return;

        Agent agent = enemy.GetComponent<Agent>();
        if (agent != null)
        {
            agent.Name = enemy.name;
            agent.defaultState = decision == AgentDecision.PATROL ? AgentState.SEEK : AgentState.NONE;
            agent.currentState = agent.defaultState;
        }

        AgentBrain brain = enemy.GetComponent<AgentBrain>();
        if (brain != null)
        {
            if (brain.actionController == null)
                brain.actionController = enemy.GetComponent<AgentActionController>();

            if (brain.actionController != null)
            {
                brain.actionController.defaultRouteName = routeName;
                brain.actionController.startIndex = 0;
            }

            brain.defaultDecision = decision;
            brain.currentDecision = decision;
        }

        AgentNavigator navigator = enemy.GetComponent<AgentNavigator>();
        if (navigator != null)
        {
            navigator.gridMap = gridMap;
            navigator.waypointGraph = waypointGraph;
            navigator.useGridMap = false;
            navigator.ObstacleLayers = 1 << GetUnwalkableLayerSafe();
        }
    }

    void RecordEnemyPlacement(List<EnemyPlacementRecord> placements, string enemyName, string routeName, Room room, Vector3 position, string ruleReason)
    {
        generatedEnemyGizmoPositions.Add(position);

        placements.Add(new EnemyPlacementRecord
        {
            enemyName = enemyName,
            routeName = routeName,
            roomName = room != null ? room.name : "Unknown",
            ruleReason = ruleReason,
            position = position
        });
    }

    void UpdateEnemyPlacementSummary(List<EnemyPlacementRecord> placements)
    {
        StringBuilder summary = new StringBuilder();
        summary.AppendLine($"Level {currentLevel} enemy PCG layout: {placements.Count} generated agents");

        foreach (EnemyPlacementRecord placement in placements)
        {
            summary.Append("- ");
            summary.Append(placement.enemyName);
            summary.Append(" | route=");
            summary.Append(placement.routeName);
            summary.Append(" | room=");
            summary.Append(placement.roomName);
            summary.Append(" | pos=");
            summary.Append(FormatVector3(placement.position));
            summary.Append(" | ");
            summary.AppendLine(placement.ruleReason);
        }

        lastEnemyPlacementSummary = summary.ToString();

        if (logEnemyPlacementSummary)
            Debug.Log("[VillaPCG] " + lastEnemyPlacementSummary);

    #if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
    #endif
    }

    public string BuildPcgReport()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("VillaPCG v4 PCG Report");
        report.AppendLine("=======================");
        report.AppendLine();

        report.AppendLine("[Map Rules]");
        report.AppendLine(string.IsNullOrWhiteSpace(lastMapRuleSummary)
            ? "No map summary generated yet. Run Generate Current Level first."
            : lastMapRuleSummary.TrimEnd());
        report.AppendLine();

        report.AppendLine("[Enemy Layout]");
        report.AppendLine(string.IsNullOrWhiteSpace(lastEnemyPlacementSummary)
            ? "No enemy placement summary generated yet. Enable enemy generation, then run Generate Current Level first."
            : lastEnemyPlacementSummary.TrimEnd());

        return report.ToString();
    }

    string FormatVector3(Vector3 value)
    {
        return $"({value.x:0.0}, {value.y:0.0}, {value.z:0.0})";
    }

    void MarkRouteManagerDirty(RouteManager routeManager)
    {
    #if UNITY_EDITOR
        if (routeManager != null && !Application.isPlaying)
        {
            EditorUtility.SetDirty(routeManager);
            EditorSceneManager.MarkSceneDirty(routeManager.gameObject.scene);
        }
    #endif
    }

    void CreateLevelExitRoomObjects(Room exitRoom, int nextLevel)
    {
        GameObject root = new GameObject("LevelExit_" + exitRoom.name);
        root.transform.parent = transform;

        float zInset = Mathf.Min(2.0f * layoutScale, exitRoom.size.y * 0.35f);
        Vector3 exitPosition = new Vector3(exitRoom.center.x, 0.08f, exitRoom.Top - zInset);

        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "LevelExit_InteractPad";
        pad.transform.parent = root.transform;
        pad.transform.position = exitPosition;
        pad.transform.localScale = new Vector3(3.0f, 0.12f, 3.0f);
        ApplyMaterial(pad, exitMat);

        BoxCollider padCollider = pad.GetComponent<BoxCollider>();
        if (padCollider != null)
            padCollider.isTrigger = true;

        LevelExit exit = pad.AddComponent<LevelExit>();
        exit.levelManagerV4 = this;
        exit.targetLevel = nextLevel;
        exit.interactKey = levelAdvanceKey;
        exit.interactRadius = exitInteractRadius;
        exit.playerTag = playerTag;
        exit.prompt = $"Press {levelAdvanceKey} to enter Level {nextLevel}";

        GameObject terminal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        terminal.name = "LevelExit_Terminal";
        terminal.transform.parent = root.transform;
        terminal.transform.position = new Vector3(exitRoom.center.x, 0.75f, exitRoom.Top - 0.45f);
        terminal.transform.localScale = new Vector3(1.4f, 1.25f, 0.25f);
        ApplyMaterial(terminal, exitMat);
        RemoveCollider(terminal);

        GameObject label = new GameObject("LevelExit_Label");
        label.transform.parent = root.transform;
        label.transform.position = exitPosition + new Vector3(0f, 0.08f, -1.9f);
        label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        TextMesh text = label.AddComponent<TextMesh>();
        // text.text = $"LEVEL EXIT\nPRESS {levelAdvanceKey}";
        text.characterSize = 0.35f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.black;
    }

    void CreateFinalTargetObject(Room targetRoom)
    {
        GameObject targetPrefab = GetTargetAgentPrefab();
        if (generateFinalTargetAgent && targetPrefab != null)
        {
            CreateFinalTargetAgent(targetRoom, targetPrefab);
            return;
        }

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        target.name = "Final_Assassination_Target";
        target.transform.parent = transform;
        target.transform.position = new Vector3(targetRoom.center.x, 1.0f, targetRoom.center.y);
        target.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
        target.layer = GetUnwalkableLayerSafe();
        ApplyMaterial(target, targetMat);

        GameObject label = new GameObject("Label_Final_Assassination_Target");
        label.transform.parent = transform;
        label.transform.position = target.transform.position + Vector3.up * 1.6f;
        label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        TextMesh text = label.AddComponent<TextMesh>();
        // text.text = "FINAL TARGET";
        text.characterSize = 0.45f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.red;
    }

    void CreateFinalTargetAgent(Room targetRoom, GameObject targetPrefab)
    {
        GameObject root = new GameObject("Generated_FinalTargetAgent");
        root.transform.parent = transform;

        GameObject escapePoint = new GameObject("TargetEscapePoint_PlayerDeployment");
        escapePoint.transform.parent = root.transform;
        escapePoint.transform.position = currentSpawnTransform != null
            ? currentSpawnTransform.position
            : new Vector3(0f, enemySpawnHeight, -12f);

        Vector3 targetPosition = new Vector3(targetRoom.center.x, enemySpawnHeight, targetRoom.center.y);
        targetPosition = ClampPointInsideRoom(targetPosition, targetRoom);
        targetPosition.y = enemySpawnHeight;

        Quaternion targetRotation = Quaternion.LookRotation(GetRoomFacingDirection(targetRoom, targetPosition), Vector3.up);
        GameObject target = CreateEnemyInstance(targetPrefab, "TargetAgent_PCG", targetPosition, targetRotation, root.transform);

        Agent escort = null;
        if (generateTargetEscort)
        {
            GameObject escortPrefab = GetTargetEscortAgentPrefab();
            if (escortPrefab != null)
            {
                Vector3 escortPosition = targetPosition - targetRotation * Vector3.forward * targetEscortOffset;
                escortPosition = ClampPointInsideRoom(escortPosition, targetRoom);
                escortPosition.y = enemySpawnHeight;

                GameObject escortObject = CreateEnemyInstance(escortPrefab, "targetEscortEnemy_PCG_01", escortPosition, targetRotation, root.transform);
                ConfigureTargetEscortAgent(escortObject);
                escort = escortObject.GetComponent<Agent>();
            }
        }

        ConfigureTargetAgent(target, escapePoint.transform, escort);

        GameObject label = new GameObject("Label_TargetAgent_PCG");
        label.transform.parent = root.transform;
        label.transform.position = targetPosition + Vector3.up * 1.8f;
        label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        TextMesh text = label.AddComponent<TextMesh>();
        // text.text = "FINAL TARGET";
        text.characterSize = 0.45f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.red;
    }

    void ConfigureTargetAgent(GameObject target, Transform escapePoint, Agent escort)
    {
        if (target == null)
            return;

        Agent agent = target.GetComponent<Agent>();
        if (agent != null)
        {
            agent.Name = target.name;
            agent.agentType = AgentType.TARGET;
            agent.currentWeapon = KevinIglesias.SoldierWeapons.None;
            agent.currentAction = KevinIglesias.SoldierAction.Nothing;
            agent.defaultState = AgentState.NONE;
            agent.currentState = AgentState.NONE;
        }

        AgentActionController actionController = target.GetComponent<AgentActionController>();
        if (actionController != null)
        {
            actionController.runToSpecificEndpoint = true;
            actionController.escapeEndpoint = escapePoint;
        }

        AgentBrain brain = target.GetComponent<AgentBrain>();
        if (brain != null)
        {
            brain.actionController = actionController;
            brain.defaultDecision = AgentDecision.LONGREST;
            brain.currentDecision = AgentDecision.LONGREST;
        }

        AgentNavigator navigator = target.GetComponent<AgentNavigator>();
        if (navigator != null)
        {
            navigator.gridMap = gridMap;
            navigator.waypointGraph = waypointGraph;
            navigator.useGridMap = false;
            navigator.ObstacleLayers = 1 << GetUnwalkableLayerSafe();
        }

        TargetAgentController targetController = target.GetComponent<TargetAgentController>();
        if (targetController == null)
            targetController = target.AddComponent<TargetAgentController>();

        targetController.revealTargetToPlayer = true;
        targetController.ConfigureForFinalLevel(escapePoint, escort, gridMap, waypointGraph, 1 << GetUnwalkableLayerSafe());
    }

    void ConfigureTargetEscortAgent(GameObject escort)
    {
        if (escort == null)
            return;

        Agent agent = escort.GetComponent<Agent>();
        if (agent != null)
        {
            agent.Name = escort.name;
            agent.agentType = AgentType.GUARD;
            agent.defaultState = AgentState.NONE;
            agent.currentState = AgentState.NONE;
        }

        AgentBrain brain = escort.GetComponent<AgentBrain>();
        AgentActionController actionController = escort.GetComponent<AgentActionController>();
        if (brain != null)
        {
            brain.actionController = actionController;
            brain.defaultDecision = AgentDecision.LONGREST;
            brain.currentDecision = AgentDecision.LONGREST;
        }

        AgentNavigator navigator = escort.GetComponent<AgentNavigator>();
        if (navigator != null)
        {
            navigator.gridMap = gridMap;
            navigator.waypointGraph = waypointGraph;
            navigator.useGridMap = false;
            navigator.ObstacleLayers = 1 << GetUnwalkableLayerSafe();
        }
    }

    GameObject GetTargetAgentPrefab()
    {
        if (targetAgentPrefab != null)
            return targetAgentPrefab;

    #if UNITY_EDITOR
        targetAgentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Target_v1.prefab");
        if (targetAgentPrefab != null)
            EditorUtility.SetDirty(this);
    #endif

        return targetAgentPrefab;
    }

    GameObject GetTargetEscortAgentPrefab()
    {
        if (targetEscortAgentPrefab != null)
            return targetEscortAgentPrefab;

        return GetEnemyAgentPrefab();
    }

    void ApplyMaterial(GameObject go, Material mat)
    {
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null && mat != null)
            renderer.material = mat;
    }

    void RemoveCollider(GameObject go)
    {
        Collider collider = go.GetComponent<Collider>();
        if (collider == null)
            return;

        collider.enabled = false;

        if (Application.isPlaying)
            Destroy(collider);
        else
            DestroyImmediate(collider);
    }

    void TeleportPlayerToSpawn()
    {
       if (currentSpawnTransform == null)
            return;

        Transform playerTransform = FindPlayerTransform();
        CharacterController controller = playerTransform != null ? playerTransform.GetComponent<CharacterController>() : null;
        if (playerTransform == null)
        {
            Debug.LogWarning("[VillaPCG] Player was not found; auto search component with characterController.");
            controller = PlayerLocator.FindPlayerCharacterController();
            if (controller != null)
                playerTransform = controller.transform;
            else
            {
                Debug.LogWarning("[VillaPCG] Player was not found; level spawn teleport skipped.");
                return;
            }
        }

        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controller != null)
            controller.enabled = false;

        Transform playerRoot = playerTransform.root;
        Vector3 delta = currentSpawnTransform.position - playerTransform.position;
        playerRoot.position += delta;
        playerTransform.rotation = currentSpawnTransform.rotation;

        if (controller != null)
            controller.enabled = controllerWasEnabled;

        ThirdPersonController thirdPersonController = playerTransform.GetComponent<ThirdPersonController>();
        if (thirdPersonController != null)
            thirdPersonController.ResetCameraRotation(currentSpawnTransform.rotation.eulerAngles.y);

        RespawnPlayer respawnPlayer = playerTransform.GetComponent<RespawnPlayer>();
        if (respawnPlayer != null)
            respawnPlayer.SetRespawnPoint(playerTransform.position, playerTransform.rotation);
    }

    Transform FindPlayerTransform()
    {
        return PlayerLocator.FindPlayerTransform(playerTag);
    }

    void OnValidate()
    {
        ApplyNavigationDebugDisplay();
        ApplyAgentDebugDisplay();
    }

    void ApplyNavigationDebugDisplay()
    {
        if (waypointGraph != null)
            waypointGraph.showGizmos = showWaypointGraph3D;

        if (gridMap != null)
            gridMap.showGizmos = showGridMap3D;
    }

    public void SetWaypointGraph3DVisible(bool visible)
    {
        showWaypointGraph3D = visible;

        if (waypointGraph != null)
            waypointGraph.showGizmos = visible;
    }

    public void SetGridMap3DVisible(bool visible)
    {
        showGridMap3D = visible;

        if (gridMap != null)
            gridMap.showGizmos = visible;
    }

    public void SetNavigationDebugVisible(bool visible)
    {
        SetWaypointGraph3DVisible(visible);
        SetGridMap3DVisible(visible);
    }

    void ApplyAgentDebugDisplay()
    {
        foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
        {
            if (agent != null)
            {
                agent.showDebugGizmos = showAgentDebugGizmos;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    EditorUtility.SetDirty(agent);
#endif
            }
        }

        foreach (RouteManager routeManager in Object.FindObjectsByType<RouteManager>(FindObjectsSortMode.None))
        {
            if (routeManager != null)
            {
                routeManager.showRouteGizmos = showAgentDebugGizmos;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    EditorUtility.SetDirty(routeManager);
#endif
            }
        }
    }

    public void SetAgentDebugGizmosVisible(bool visible)
    {
        showAgentDebugGizmos = visible;
        ApplyAgentDebugDisplay();

#if UNITY_EDITOR
        SceneView.RepaintAll();
#endif
    }

    void OnDrawGizmos()
    {
        if (!showWaypointGizmos)
            return;

        Gizmos.color = Color.cyan;

        foreach (Vector3 wp in waypoints)
        {
            Gizmos.DrawSphere(wp + Vector3.up * 0.25f, 0.25f);
        }

        Gizmos.color = Color.yellow;

        foreach (Connection c in connections)
        {
            Vector3 a = new Vector3(c.a.center.x, 0.15f, c.a.center.y);
            Vector3 b = new Vector3(c.b.center.x, 0.15f, c.b.center.y);
            Gizmos.DrawLine(a, c.doorWorldPos + Vector3.up * 0.15f);
            Gizmos.DrawLine(c.doorWorldPos + Vector3.up * 0.15f, b);
        }

        if (showAgentDebugGizmos)
        {
            Gizmos.color = Color.red;

            foreach (Vector3 enemyPosition in generatedEnemyGizmoPositions)
            {
                Gizmos.DrawSphere(enemyPosition + Vector3.up * 0.6f, 0.45f);
            }
        }
    }

    void CreateConnectorFloors()
    {
        GameObject root = new GameObject("Generated_ConnectorFloors");
        root.transform.parent = transform;

        foreach (Connection c in connections)
        {
            Room a = c.a;
            Room b = c.b;

            bool horizontalConnection = c.sideA == Side.East || c.sideA == Side.West;

            if (horizontalConnection)
            {
                // East-West connection: 補 x 方向之間的地板
                float x1, x2;

                if (a.center.x < b.center.x)
                {
                    x1 = a.Right;
                    x2 = b.Left;
                }
                else
                {
                    x1 = b.Right;
                    x2 = a.Left;
                }

                float length = Mathf.Abs(x2 - x1);

                // 如果沒有真實 gap，就不要生成 connector floor
                if (length < 0.1f)
                    continue;

                float centerX = (x1 + x2) * 0.5f;
                float centerZ = c.doorWorldPos.z;

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "ConnectorFloor_" + a.name + "_to_" + b.name;
                
                floor.layer = GetWalkableLayerSafe();

                floor.transform.parent = root.transform;
                floor.transform.position = new Vector3(centerX, -floorThickness * 0.5f, centerZ);
                floor.transform.localScale = new Vector3(length + 0.5f, floorThickness, connectorFloorWidth);

                Renderer renderer = floor.GetComponent<Renderer>();
                renderer.material = floorMat;

                LayoutZone connectorZone = GetConnectionLayoutZone(a, b);
                CreateLayoutZoneMarker("Area_" + GetLayoutZoneNameToken(connectorZone) + "_" + floor.name, floor.transform.position, floor.transform.localScale, GetAreaLayerSafe(connectorZone), root.transform);
            }
            else
            {
                // North-South connection: 補 z 方向之間的地板
                float z1, z2;

                if (a.center.y < b.center.y)
                {
                    z1 = a.Top;
                    z2 = b.Bottom;
                }
                else
                {
                    z1 = b.Top;
                    z2 = a.Bottom;
                }

                float length = Mathf.Abs(z2 - z1);

                if (length < 0.1f)
                    continue;

                float centerX = c.doorWorldPos.x;
                float centerZ = (z1 + z2) * 0.5f;

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "ConnectorFloor_" + a.name + "_to_" + b.name;
                
                floor.layer = GetWalkableLayerSafe();
                
                floor.transform.parent = root.transform;
                floor.transform.position = new Vector3(centerX, -floorThickness * 0.5f, centerZ);
                floor.transform.localScale = new Vector3(connectorFloorWidth, floorThickness, length + 0.5f);

                Renderer renderer = floor.GetComponent<Renderer>();
                renderer.material = floorMat;

                LayoutZone connectorZone = GetConnectionLayoutZone(a, b);
                CreateLayoutZoneMarker("Area_" + GetLayoutZoneNameToken(connectorZone) + "_" + floor.name, floor.transform.position, floor.transform.localScale, GetAreaLayerSafe(connectorZone), root.transform);
            }
        }
    }

    void CreateSafetyFoundation()
    {
        if (rooms.Count == 0)
            return;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (Room r in rooms)
        {
            minX = Mathf.Min(minX, r.Left);
            maxX = Mathf.Max(maxX, r.Right);
            minZ = Mathf.Min(minZ, r.Bottom);
            maxZ = Mathf.Max(maxZ, r.Top);
        }

        float padding = 4.0f * layoutScale;

        float sizeX = (maxX - minX) + padding * 2.0f;
        float sizeZ = (maxZ - minZ) + padding * 2.0f;

        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;

        GameObject foundation = GameObject.CreatePrimitive(PrimitiveType.Cube);
        foundation.name = "Safety_Foundation";
        foundation.transform.parent = transform;

        // 比正常地板略低一點，避免和房間地板 z-fighting
        foundation.transform.position = new Vector3(centerX, -0.20f, centerZ);
        foundation.transform.localScale = new Vector3(sizeX, 0.1f, sizeZ);

        Renderer renderer = foundation.GetComponent<Renderer>();
        renderer.material = floorMat;
    }

    void CreateOnePieceFloor()
    {
        if (rooms.Count == 0)
            return;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (Room r in rooms)
        {
            minX = Mathf.Min(minX, r.Left);
            maxX = Mathf.Max(maxX, r.Right);
            minZ = Mathf.Min(minZ, r.Bottom);
            maxZ = Mathf.Max(maxZ, r.Top);
        }

        float padding = 3.0f * layoutScale;

        float sizeX = (maxX - minX) + padding * 2.0f;
        float sizeZ = (maxZ - minZ) + padding * 2.0f;

        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "OnePiece_Villa_Floor";
        floor.transform.parent = transform;

        floor.transform.position = new Vector3(centerX, -floorThickness * 0.5f - floorLayerSeparation, centerZ);
        floor.transform.localScale = new Vector3(sizeX, floorThickness, sizeZ);

        Renderer renderer = floor.GetComponent<Renderer>();
        renderer.material = floorMat;
    }

    void CreateConnectionCorridors()
    {
        GameObject root = new GameObject("Generated_Corridors");
        root.transform.parent = transform;

        foreach (Connection c in connections)
        {
            // 貼齊房間不需要走廊
            if (AreRoomsTouching(c))
                continue;

            bool eastWest = c.sideA == Side.East || c.sideA == Side.West;

            if (eastWest)
                CreateHorizontalCorridorWalls(c, root.transform);
            else
                CreateVerticalCorridorWalls(c, root.transform);
        }
    }

    void CreateHorizontalCorridorWalls(Connection c, Transform root)
    {
        Room a = c.a;
        Room b = c.b;

        float x1;
        float x2;

        if (a.center.x < b.center.x)
        {
            x1 = a.Right;
            x2 = b.Left;
        }
        else
        {
            x1 = b.Right;
            x2 = a.Left;
        }

        float length = Mathf.Abs(x2 - x1);
        if (length < 0.1f)
            return;

        float centerX = (x1 + x2) * 0.5f;
        float centerZ = c.doorWorldPos.z;

        float halfWidth = doorWidth * 0.5f;

        // corridor 上側牆
        CreateCorridorWall(
            "CorridorWall_Top_" + a.name + "_to_" + b.name,
            new Vector3(centerX, wallHeight * 0.5f, centerZ + halfWidth),
            new Vector3(length, wallHeight, wallThickness),
            root
        );

        // corridor 下側牆
        CreateCorridorWall(
            "CorridorWall_Bottom_" + a.name + "_to_" + b.name,
            new Vector3(centerX, wallHeight * 0.5f, centerZ - halfWidth),
            new Vector3(length, wallHeight, wallThickness),
            root
        );
    }

    void CreateVerticalCorridorWalls(Connection c, Transform root)
    {
        Room a = c.a;
        Room b = c.b;

        float z1;
        float z2;

        if (a.center.y < b.center.y)
        {
            z1 = a.Top;
            z2 = b.Bottom;
        }
        else
        {
            z1 = b.Top;
            z2 = a.Bottom;
        }

        float length = Mathf.Abs(z2 - z1);
        if (length < 0.1f)
            return;

        float centerX = c.doorWorldPos.x;
        float centerZ = (z1 + z2) * 0.5f;

        float halfWidth = doorWidth * 0.5f;

        // corridor 左側牆
        CreateCorridorWall(
            "CorridorWall_Left_" + a.name + "_to_" + b.name,
            new Vector3(centerX - halfWidth, wallHeight * 0.5f, centerZ),
            new Vector3(wallThickness, wallHeight, length),
            root
        );

        // corridor 右側牆
        CreateCorridorWall(
            "CorridorWall_Right_" + a.name + "_to_" + b.name,
            new Vector3(centerX + halfWidth, wallHeight * 0.5f, centerZ),
            new Vector3(wallThickness, wallHeight, length),
            root
        );
    }

    void CreateCorridorWall(string name, Vector3 position, Vector3 scale, Transform root)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;

        wall.layer = GetUnwalkableLayerSafe();

        wall.transform.parent = root;
        wall.transform.position = position;
        wall.transform.localScale = scale;

        Renderer renderer = wall.GetComponent<Renderer>();
        renderer.material = wallMat;
    }

    void CreateConnectionSideWalls()
    {
        GameObject root = new GameObject("Generated_ConnectionSideWalls");
        root.transform.parent = transform;

        foreach (Connection c in connections)
        {
            // 如果兩個房間本來就貼在一起，不需要額外封邊
            if (AreRoomsTouching(c))
                continue;

            bool eastWestConnection = c.sideA == Side.East || c.sideA == Side.West;

            if (eastWestConnection)
                CreateEastWestConnectionSideWalls(c, root.transform);
            else
                CreateNorthSouthConnectionSideWalls(c, root.transform);
        }
    }

    void CreateEastWestConnectionSideWalls(Connection c, Transform root)
    {
        Room a = c.a;
        Room b = c.b;

        float x1;
        float x2;

        if (a.center.x < b.center.x)
        {
            x1 = a.Right;
            x2 = b.Left;
        }
        else
        {
            x1 = b.Right;
            x2 = a.Left;
        }

        float gapLength = Mathf.Abs(x2 - x1);

        if (gapLength < 0.1f)
            return;

        float centerX = (x1 + x2) * 0.5f;
        float centerZ = c.doorWorldPos.z;

        float length = gapLength + connectionSideWallOverlap * 2.0f;

        float zOffset = doorWidth * 0.5f + wallThickness * 0.5f;

        CreateConnectionWall(
            "ConnectionSideWall_EW_Top_" + a.name + "_to_" + b.name,
            new Vector3(centerX, wallHeight * 0.5f, centerZ + zOffset),
            new Vector3(length, wallHeight, wallThickness),
            root
        );

        CreateConnectionWall(
            "ConnectionSideWall_EW_Bottom_" + a.name + "_to_" + b.name,
            new Vector3(centerX, wallHeight * 0.5f, centerZ - zOffset),
            new Vector3(length, wallHeight, wallThickness),
            root
        );
    }

    void CreateNorthSouthConnectionSideWalls(Connection c, Transform root)
    {
        Room a = c.a;
        Room b = c.b;

        float z1;
        float z2;

        if (a.center.y < b.center.y)
        {
            z1 = a.Top;
            z2 = b.Bottom;
        }
        else
        {
            z1 = b.Top;
            z2 = a.Bottom;
        }

        float gapLength = Mathf.Abs(z2 - z1);

        if (gapLength < 0.1f)
            return;

        float centerX = c.doorWorldPos.x;
        float centerZ = (z1 + z2) * 0.5f;

        float length = gapLength + connectionSideWallOverlap * 2.0f;

        float xOffset = doorWidth * 0.5f + wallThickness * 0.5f;

        CreateConnectionWall(
            "ConnectionSideWall_NS_Left_" + a.name + "_to_" + b.name,
            new Vector3(centerX - xOffset, wallHeight * 0.5f, centerZ),
            new Vector3(wallThickness, wallHeight, length),
            root
        );

        CreateConnectionWall(
            "ConnectionSideWall_NS_Right_" + a.name + "_to_" + b.name,
            new Vector3(centerX + xOffset, wallHeight * 0.5f, centerZ),
            new Vector3(wallThickness, wallHeight, length),
            root
        );
    }

    void CreateConnectionWall(string name, Vector3 position, Vector3 scale, Transform root)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.parent = root;
        wall.transform.position = position;
        wall.transform.localScale = scale;

        Renderer renderer = wall.GetComponent<Renderer>();
        renderer.material = wallMat;

        // 如果你有使用 Unwalkable layer，建議把這些牆也設成 Unwalkable
        wall.layer = GetUnwalkableLayerSafe();
    }

    Bounds GetGeneratedMapBounds(float padding)
    {
        if (rooms == null || rooms.Count == 0)
        {
            return new Bounds(Vector3.zero, new Vector3(50f, 5f, 50f));
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (Room r in rooms)
        {
            minX = Mathf.Min(minX, r.Left);
            maxX = Mathf.Max(maxX, r.Right);
            minZ = Mathf.Min(minZ, r.Bottom);
            maxZ = Mathf.Max(maxZ, r.Top);
        }

        minX -= padding;
        maxX += padding;
        minZ -= padding;
        maxZ += padding;

        Vector3 center = new Vector3(
            (minX + maxX) * 0.5f,
            0f,
            (minZ + maxZ) * 0.5f
        );

        Vector3 size = new Vector3(
            maxX - minX,
            5f,
            maxZ - minZ
        );

        return new Bounds(center, size);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(VillaPCG_v4))]
[CanEditMultipleObjects]
public class VillaPCG_v4Editor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        serializedObject.ApplyModifiedProperties();
    }

    void RunGenerationAction(string undoName, System.Action<VillaPCG_v4> generationAction)
    {
        serializedObject.ApplyModifiedProperties();

        foreach (Object selectedTarget in targets)
        {
            VillaPCG_v4 pcg = selectedTarget as VillaPCG_v4;
            if (pcg == null)
                continue;

            Undo.RecordObject(pcg, undoName);
            generationAction(pcg);
            EditorUtility.SetDirty(pcg);

            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(pcg.gameObject.scene);
        }

        serializedObject.Update();
        SceneView.RepaintAll();
    }

    void CopyPcgReport()
    {
        serializedObject.ApplyModifiedProperties();

        StringBuilder report = new StringBuilder();
        foreach (Object selectedTarget in targets)
        {
            if (selectedTarget is not VillaPCG_v4 pcg)
                continue;

            if (report.Length > 0)
            {
                report.AppendLine();
                report.AppendLine("---");
                report.AppendLine();
            }

            report.Append(pcg.BuildPcgReport());
        }

        EditorGUIUtility.systemCopyBuffer = report.ToString();
        Debug.Log("[VillaPCG_v4] PCG report copied to clipboard.");
    }

    void GenerateValidateAndSaveSelectedScenes()
    {
        serializedObject.ApplyModifiedProperties();

        if (Application.isPlaying)
        {
            Debug.LogWarning("[VillaPCG_v4] Scene save is disabled while playing.");
            return;
        }

        foreach (Object selectedTarget in targets)
        {
            if (selectedTarget is not VillaPCG_v4 pcg)
                continue;

            Undo.RecordObject(pcg, "Generate Validate And Save Enemy PCG Scene");
            pcg.Generate();

            if (!ValidateEnemyPcgLayout(pcg))
                continue;

            EditorUtility.SetDirty(pcg);
            EditorSceneManager.MarkSceneDirty(pcg.gameObject.scene);

            if (EditorSceneManager.SaveScene(pcg.gameObject.scene))
                Debug.Log($"[VillaPCG_v4] Saved generated enemy PCG scene: {pcg.gameObject.scene.path}");
            else
                Debug.LogError($"[VillaPCG_v4] Failed to save generated enemy PCG scene: {pcg.gameObject.scene.path}");
        }

        serializedObject.Update();
        SceneView.RepaintAll();
    }

    bool ValidateEnemyPcgLayout(VillaPCG_v4 pcg)
    {
        if (pcg == null)
            return false;

        if (!pcg.generateEnemyAgents)
        {
            Debug.LogWarning("[VillaPCG_v4 Validation] Enemy generation is disabled.");
            return false;
        }

        bool passed = true;
        Transform enemyRoot = pcg.transform.Find("Generated_EnemyAgents");
        Transform routeRoot = pcg.transform.Find("Generated_EnemyRoutes");
        int patrolEnemyCount = CountChildrenWithNamePrefix(enemyRoot, "patrolEnemyTest_PCG_");
        int standEnemyCount = CountChildrenWithNamePrefix(enemyRoot, "standEnemyTest_PCG_");
        int routePointCount = routeRoot != null ? routeRoot.childCount : 0;
        int generatedRouteCount = CountGeneratedEnemyRoutes(pcg);
        RouteManager routeManager = Object.FindFirstObjectByType<RouteManager>();

        if (enemyRoot == null)
        {
            Debug.LogError("[VillaPCG_v4 Validation] Missing Generated_EnemyAgents root.");
            passed = false;
        }

        if (routeRoot == null)
        {
            Debug.LogError("[VillaPCG_v4 Validation] Missing Generated_EnemyRoutes root.");
            passed = false;
        }

        if (patrolEnemyCount + standEnemyCount <= 0)
        {
            Debug.LogError("[VillaPCG_v4 Validation] No generated patrol or standing enemy agents found.");
            passed = false;
        }

        if (routePointCount <= 0 || generatedRouteCount <= 0)
        {
            Debug.LogError("[VillaPCG_v4 Validation] No generated enemy route data found.");
            passed = false;
        }

        if (routeManager == null)
        {
            Debug.LogError("[VillaPCG_v4 Validation] RouteManager is missing.");
            passed = false;
        }

        if (string.IsNullOrWhiteSpace(pcg.lastMapRuleSummary))
        {
            Debug.LogError("[VillaPCG_v4 Validation] lastMapRuleSummary is empty.");
            passed = false;
        }
        else if (!pcg.lastMapRuleSummary.Contains("map PCG rules") || !pcg.lastMapRuleSummary.Contains("reachability"))
        {
            Debug.LogError("[VillaPCG_v4 Validation] lastMapRuleSummary does not include map rule and reachability data.");
            passed = false;
        }

        if (enemyRoot != null && routeManager != null)
        {
            if (!ValidateGeneratedEnemyAgents(pcg, enemyRoot, routeManager))
                passed = false;
        }

        if (string.IsNullOrWhiteSpace(pcg.lastEnemyPlacementSummary))
        {
            Debug.LogError("[VillaPCG_v4 Validation] lastEnemyPlacementSummary is empty.");
            passed = false;
        }
        else if (!pcg.lastEnemyPlacementSummary.Contains("RoomThreatScore")
            && !pcg.lastEnemyPlacementSummary.Contains("RoomPairPatrolScore")
            && !pcg.lastEnemyPlacementSummary.Contains("DoorGuardScore"))
        {
            Debug.LogError("[VillaPCG_v4 Validation] Enemy placement summary does not include PCG rule score names.");
            passed = false;
        }

        if (passed)
        {
            Debug.Log($"[VillaPCG_v4 Validation] Passed. patrol={patrolEnemyCount}, stand={standEnemyCount}, routePoints={routePointCount}, routes={generatedRouteCount}\n{pcg.lastEnemyPlacementSummary}");
        }

        return passed;
    }

    bool ValidateGeneratedEnemyAgents(VillaPCG_v4 pcg, Transform enemyRoot, RouteManager routeManager)
    {
        bool passed = true;

        for (int i = 0; i < enemyRoot.childCount; i++)
        {
            GameObject enemy = enemyRoot.GetChild(i).gameObject;
            bool isPatrol = enemy.name.StartsWith("patrolEnemyTest_PCG_", System.StringComparison.Ordinal);
            bool isStand = enemy.name.StartsWith("standEnemyTest_PCG_", System.StringComparison.Ordinal);

            if (!isPatrol && !isStand)
                continue;

            Agent agent = enemy.GetComponent<Agent>();
            AgentBrain brain = enemy.GetComponent<AgentBrain>();
            AgentNavigator navigator = enemy.GetComponent<AgentNavigator>();

            if (agent == null)
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} is missing Agent.");
                passed = false;
            }

            if (brain == null)
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} is missing AgentBrain.");
                passed = false;
                continue;
            }

            if (navigator == null)
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} is missing AgentNavigator.");
                passed = false;
            }
            else
            {
                if (pcg.gridMap != null && navigator.gridMap != pcg.gridMap)
                {
                    Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} AgentNavigator.gridMap does not match VillaPCG_v4.gridMap.");
                    passed = false;
                }

                if (pcg.waypointGraph != null && navigator.waypointGraph != pcg.waypointGraph)
                {
                    Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} AgentNavigator.waypointGraph does not match VillaPCG_v4.waypointGraph.");
                    passed = false;
                }
            }

            AgentDecision expectedDecision = isPatrol ? AgentDecision.PATROL : AgentDecision.LONGREST;
            if (brain.defaultDecision != expectedDecision || brain.currentDecision != expectedDecision)
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} decision mismatch. expected={expectedDecision}, default={brain.defaultDecision}, current={brain.currentDecision}.");
                passed = false;
            }

            if (brain.actionController == null)
                brain.actionController = enemy.GetComponent<AgentActionController>();

            if (brain.actionController == null)
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} is missing AgentActionController.");
                passed = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(brain.actionController.defaultRouteName))
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} has an empty defaultRouteName.");
                passed = false;
                continue;
            }

            RouteDefinition route = routeManager.GetRoute(brain.actionController.defaultRouteName);
            if (route == null)
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} route '{brain.actionController.defaultRouteName}' was not found in RouteManager.");
                passed = false;
                continue;
            }

            if (route.waypoints == null || route.waypoints.Count == 0)
            {
                Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} route '{route.routeName}' has no waypoints.");
                passed = false;
                continue;
            }

            for (int routePointIndex = 0; routePointIndex < route.waypoints.Count; routePointIndex++)
            {
                if (route.waypoints[routePointIndex] == null || route.waypoints[routePointIndex].point == null)
                {
                    Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} route '{route.routeName}' has a missing waypoint at index {routePointIndex}.");
                    passed = false;
                }
                else if (!RoutePointNameHasRoomToken(route.routeName, route.waypoints[routePointIndex].point.name))
                {
                    Debug.LogError($"[VillaPCG_v4 Validation] {enemy.name} route waypoint '{route.waypoints[routePointIndex].point.name}' does not include a room token. Run Generate Current Level to rebuild stale generated routes.");
                    passed = false;
                }
            }
        }

        return passed;
    }

    public static bool RoutePointNameHasRoomToken(string routeName, string pointName)
    {
        if (string.IsNullOrEmpty(routeName) || string.IsNullOrEmpty(pointName))
            return false;

        string prefix = routeName + "_";
        if (!pointName.StartsWith(prefix, System.StringComparison.Ordinal))
            return false;

        string suffix = pointName.Substring(prefix.Length);
        if (suffix.StartsWith("WP_", System.StringComparison.Ordinal) || suffix == "Hold")
            return false;

        return suffix.Contains("_WP_") || suffix.EndsWith("_Hold", System.StringComparison.Ordinal);
    }

    int CountChildrenWithNamePrefix(Transform root, string prefix)
    {
        if (root == null)
            return 0;

        int count = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name.StartsWith(prefix, System.StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    int CountGeneratedEnemyRoutes(VillaPCG_v4 pcg)
    {
        RouteManager routeManager = Object.FindFirstObjectByType<RouteManager>();
        if (routeManager == null || routeManager.allRoutes == null)
            return 0;

        int count = 0;
        foreach (RouteDefinition route in routeManager.allRoutes)
        {
            if (route == null || string.IsNullOrEmpty(route.routeName))
                continue;

            bool hasConfiguredPrefix = !string.IsNullOrEmpty(pcg.generatedEnemyRoutePrefix)
                && route.routeName.StartsWith(pcg.generatedEnemyRoutePrefix, System.StringComparison.Ordinal);

            if (hasConfiguredPrefix || route.routeName.StartsWith("PCG_Enemy_", System.StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    void SetWaypointGraphVisible(bool visible)
    {
        foreach (Object selectedTarget in targets)
        {
            VillaPCG_v4 pcg = selectedTarget as VillaPCG_v4;
            if (pcg == null)
                continue;

            Undo.RecordObject(pcg, "Toggle Waypoint Graph 3D Display");
            pcg.SetWaypointGraph3DVisible(visible);
            EditorUtility.SetDirty(pcg);

            if (pcg.waypointGraph != null)
            {
                Undo.RecordObject(pcg.waypointGraph, "Toggle Waypoint Graph 3D Gizmos");
                pcg.waypointGraph.showGizmos = visible;
                EditorUtility.SetDirty(pcg.waypointGraph);
            }
            else
            {
                Debug.LogWarning("[VillaPCG] WaypointGraph3D reference is missing.");
            }
        }

        SceneView.RepaintAll();
    }

    void SetGridMapVisible(bool visible)
    {
        foreach (Object selectedTarget in targets)
        {
            VillaPCG_v4 pcg = selectedTarget as VillaPCG_v4;
            if (pcg == null)
                continue;

            Undo.RecordObject(pcg, "Toggle Grid Map 3D Display");
            pcg.SetGridMap3DVisible(visible);
            EditorUtility.SetDirty(pcg);

            if (pcg.gridMap != null)
            {
                Undo.RecordObject(pcg.gridMap, "Toggle Grid Map 3D Gizmos");
                pcg.gridMap.showGizmos = visible;
                EditorUtility.SetDirty(pcg.gridMap);
            }
            else
            {
                Debug.LogWarning("[VillaPCG] GridMap3D reference is missing.");
            }
        }

        SceneView.RepaintAll();
    }

    void SetBothVisible(bool visible)
    {
        SetWaypointGraphVisible(visible);
        SetGridMapVisible(visible);
    }
}

public static class VillaPCG_v4ValidationRunner
{
    const string UltimatePcgV2ScenePath = "Assets/Scenes/UltimatePCG_v2.unity";

    public static void ValidateUltimatePcgV2EnemyPcg()
    {
        GenerateAndValidateUltimatePcgV2EnemyPcg(saveScene: false);
    }

    public static void GenerateValidateSaveUltimatePcgV2EnemyPcg()
    {
        GenerateAndValidateUltimatePcgV2EnemyPcg(saveScene: true);
    }

    static void GenerateAndValidateUltimatePcgV2EnemyPcg(bool saveScene)
    {
        bool passed = false;

        try
        {
            EditorSceneManager.OpenScene(UltimatePcgV2ScenePath);
            VillaPCG_v4 pcg = Object.FindFirstObjectByType<VillaPCG_v4>();
            if (pcg == null)
                throw new System.InvalidOperationException($"No VillaPCG_v4 found in {UltimatePcgV2ScenePath}.");

            pcg.Generate();
            passed = ValidateGeneratedState(pcg, out string report);

            if (passed && saveScene)
            {
                EditorUtility.SetDirty(pcg);
                EditorSceneManager.MarkSceneDirty(pcg.gameObject.scene);

                if (EditorSceneManager.SaveScene(pcg.gameObject.scene))
                    Debug.Log($"[VillaPCG_v4 Batch Validation] Saved generated enemy PCG scene: {pcg.gameObject.scene.path}");
                else
                {
                    Debug.LogError($"[VillaPCG_v4 Batch Validation] Failed to save generated enemy PCG scene: {pcg.gameObject.scene.path}");
                    passed = false;
                }
            }

            if (passed)
                Debug.Log("[VillaPCG_v4 Batch Validation] Passed.\n" + report);
            else
                Debug.LogError("[VillaPCG_v4 Batch Validation] Failed.\n" + report);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            passed = false;
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(passed ? 0 : 1);
    }

    static bool ValidateGeneratedState(VillaPCG_v4 pcg, out string report)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        bool passed = true;

        Transform enemyRoot = pcg.transform.Find("Generated_EnemyAgents");
        Transform routeRoot = pcg.transform.Find("Generated_EnemyRoutes");
        RouteManager routeManager = Object.FindFirstObjectByType<RouteManager>();

        int patrolEnemyCount = CountChildrenWithNamePrefix(enemyRoot, "patrolEnemyTest_PCG_");
        int standEnemyCount = CountChildrenWithNamePrefix(enemyRoot, "standEnemyTest_PCG_");
        int routePointCount = routeRoot != null ? routeRoot.childCount : 0;
        int generatedRouteCount = CountGeneratedEnemyRoutes(pcg, routeManager);

        AppendCheck(builder, enemyRoot != null, "Generated_EnemyAgents root exists", ref passed);
        AppendCheck(builder, routeRoot != null, "Generated_EnemyRoutes root exists", ref passed);
        AppendCheck(builder, patrolEnemyCount + standEnemyCount > 0, $"Generated enemy agents exist: patrol={patrolEnemyCount}, stand={standEnemyCount}", ref passed);
        AppendCheck(builder, routePointCount > 0, $"Generated route points exist: {routePointCount}", ref passed);
        AppendCheck(builder, generatedRouteCount > 0, $"Generated RouteManager routes exist: {generatedRouteCount}", ref passed);
        AppendCheck(builder, !string.IsNullOrWhiteSpace(pcg.lastMapRuleSummary), "lastMapRuleSummary is populated", ref passed);
        AppendCheck(builder, pcg.lastMapRuleSummary.Contains("map PCG rules") && pcg.lastMapRuleSummary.Contains("reachability"), "map summary contains rule and reachability data", ref passed);
        AppendCheck(builder, !string.IsNullOrWhiteSpace(pcg.lastEnemyPlacementSummary), "lastEnemyPlacementSummary is populated", ref passed);
        AppendCheck(builder,
            pcg.lastEnemyPlacementSummary.Contains("RoomThreatScore")
            || pcg.lastEnemyPlacementSummary.Contains("RoomPairPatrolScore")
            || pcg.lastEnemyPlacementSummary.Contains("DoorGuardScore"),
            "summary contains PCG score rule names",
            ref passed);

        if (enemyRoot != null && routeManager != null)
            passed &= ValidateGeneratedEnemyAgents(pcg, enemyRoot, routeManager, builder);
        else
            passed = false;

        builder.AppendLine();
        builder.AppendLine(pcg.lastMapRuleSummary);
        builder.AppendLine(pcg.lastEnemyPlacementSummary);
        report = builder.ToString();
        return passed;
    }

    static void AppendCheck(System.Text.StringBuilder builder, bool condition, string label, ref bool passed)
    {
        builder.Append(condition ? "[PASS] " : "[FAIL] ");
        builder.AppendLine(label);

        if (!condition)
            passed = false;
    }

    static bool ValidateGeneratedEnemyAgents(VillaPCG_v4 pcg, Transform enemyRoot, RouteManager routeManager, System.Text.StringBuilder builder)
    {
        bool passed = true;

        for (int i = 0; i < enemyRoot.childCount; i++)
        {
            GameObject enemy = enemyRoot.GetChild(i).gameObject;
            bool isPatrol = enemy.name.StartsWith("patrolEnemyTest_PCG_", System.StringComparison.Ordinal);
            bool isStand = enemy.name.StartsWith("standEnemyTest_PCG_", System.StringComparison.Ordinal);
            if (!isPatrol && !isStand)
                continue;

            Agent agent = enemy.GetComponent<Agent>();
            AgentBrain brain = enemy.GetComponent<AgentBrain>();
            AgentNavigator navigator = enemy.GetComponent<AgentNavigator>();
            AgentDecision expectedDecision = isPatrol ? AgentDecision.PATROL : AgentDecision.LONGREST;

            AppendCheck(builder, agent != null, $"{enemy.name} has Agent", ref passed);
            AppendCheck(builder, brain != null, $"{enemy.name} has AgentBrain", ref passed);
            AppendCheck(builder, navigator != null, $"{enemy.name} has AgentNavigator", ref passed);

            if (brain == null)
                continue;

            AppendCheck(builder, brain.defaultDecision == expectedDecision && brain.currentDecision == expectedDecision, $"{enemy.name} decision is {expectedDecision}", ref passed);

            if (brain.actionController == null)
                brain.actionController = enemy.GetComponent<AgentActionController>();

            AppendCheck(builder, brain.actionController != null, $"{enemy.name} has AgentActionController", ref passed);
            if (brain.actionController == null)
                continue;

            AppendCheck(builder, !string.IsNullOrWhiteSpace(brain.actionController.defaultRouteName), $"{enemy.name} has defaultRouteName", ref passed);

            if (!string.IsNullOrWhiteSpace(brain.actionController.defaultRouteName))
            {
                RouteDefinition route = routeManager.GetRoute(brain.actionController.defaultRouteName);
                AppendCheck(builder, route != null, $"{enemy.name} route exists: {brain.actionController.defaultRouteName}", ref passed);

                if (route != null)
                {
                    AppendCheck(builder, route.waypoints != null && route.waypoints.Count > 0, $"{enemy.name} route has waypoints", ref passed);
                    if (route.waypoints != null)
                    {
                        for (int routePointIndex = 0; routePointIndex < route.waypoints.Count; routePointIndex++)
                        {
                            bool waypointExists = route.waypoints[routePointIndex] != null && route.waypoints[routePointIndex].point != null;
                            AppendCheck(builder, waypointExists, $"{enemy.name} route waypoint {routePointIndex} exists", ref passed);

                            if (waypointExists)
                                AppendCheck(builder, VillaPCG_v4Editor.RoutePointNameHasRoomToken(route.routeName, route.waypoints[routePointIndex].point.name), $"{enemy.name} route waypoint {routePointIndex} includes room token; regenerate if stale", ref passed);
                        }
                    }
                }
            }

            if (navigator != null)
            {
                AppendCheck(builder, pcg.gridMap == null || navigator.gridMap == pcg.gridMap, $"{enemy.name} navigator gridMap matches", ref passed);
                AppendCheck(builder, pcg.waypointGraph == null || navigator.waypointGraph == pcg.waypointGraph, $"{enemy.name} navigator waypointGraph matches", ref passed);
            }
        }

        return passed;
    }

    static int CountChildrenWithNamePrefix(Transform root, string prefix)
    {
        if (root == null)
            return 0;

        int count = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name.StartsWith(prefix, System.StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    static int CountGeneratedEnemyRoutes(VillaPCG_v4 pcg, RouteManager routeManager)
    {
        if (routeManager == null || routeManager.allRoutes == null)
            return 0;

        int count = 0;
        foreach (RouteDefinition route in routeManager.allRoutes)
        {
            if (route == null || string.IsNullOrEmpty(route.routeName))
                continue;

            bool hasConfiguredPrefix = !string.IsNullOrEmpty(pcg.generatedEnemyRoutePrefix)
                && route.routeName.StartsWith(pcg.generatedEnemyRoutePrefix, System.StringComparison.Ordinal);

            if (hasConfiguredPrefix || route.routeName.StartsWith("PCG_Enemy_", System.StringComparison.Ordinal))
                count++;
        }

        return count;
    }
}
#endif
