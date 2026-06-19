#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

using System.Collections.Generic;
using UnityEngine;

public class VillaPCG_v1 : MonoBehaviour
{
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

    [Header("Navigation Rebuild")]

    //WaypointGraph 設定
    public WaypointGraph3D waypointGraph;
    public bool rebuildWaypointGraphAfterGenerate = true;

    //GridMap 設定
    public GridMap3D gridMap;
    public bool rebuildGridMapAfterGenerate = true;
    public float gridMapPadding = 4.0f;

    int GetLayerSafe(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer == -1)
        {
            Debug.LogWarning($"[VillaPCG] Layer '{layerName}' does not exist. Using Default layer.");
            return 0;
        }
        return layer;
    }

    [Header("Generation Mode")]
    public bool generateOnPlay = false;

    [Header("Connection Blocking")]
    public bool generateConnectionSideWalls = true;
    public float connectionSideWallOverlap = 0.35f;

    [Header("Obstacle Density")]
    [Tooltip("Public room obstacle density multiplier")]
    public float publicObstacleDensity = 0.35f;

    [Tooltip("SemiRestricted room obstacle density multiplier")]
    public float semiRestrictedObstacleDensity = 0.65f;

    [Tooltip("Restricted room obstacle density multiplier")]
    public float restrictedObstacleDensity = 1.0f;

    [Tooltip("Critical room obstacle density multiplier")]
    public float criticalObstacleDensity = 1.2f;

    public int maxObstaclesPerRoom = 8;

    [Header("Geometry")]
    public float wallHeight = 3.0f;
    public float wallThickness = 0.25f;
    public float doorWidth = 4.0f;
    public float floorThickness = 0.15f;
    const float floorLayerSeparation = 0.02f;

    [Header("Visual Debug")]
    public bool showRoomLabels = true;
    public bool showWaypointGizmos = true;
    public bool generateCoverObjects = true;

    [Header("Materials")]
    public Material floorMat;
    public Material wallMat;
    public Material restrictedFloorMat;
    public Material coverMat;

    [Header("Layout Scale")]
    public float layoutScale = 2.0f;          // 房間與地圖整體放大 2 倍
    public float connectorFloorWidth = 3.5f;  // 房間之間連接地板寬度
    public bool generateConnectorFloors = true;
    public bool generateSafetyFoundation = true;

    void Start()
    {
        if (Application.isPlaying && generateOnPlay)
        {
            Generate();
        }
    }

    private List<Room> rooms = new List<Room>();
    private List<Connection> connections = new List<Connection>();
    private List<Vector3> waypoints = new List<Vector3>();
    private Dictionary<string, List<string>> roomGraph = new Dictionary<string, List<string>>();

    enum Side
    {
        North,
        South,
        East,
        West
    }

    enum SecurityLevel
    {
        Public,
        SemiRestricted,
        Restricted,
        Critical
    }

    class Room
    {
        public string name;
        public Vector2 center;
        public Vector2 size;
        public SecurityLevel security;

        public Room(string name, Vector2 center, Vector2 size, SecurityLevel security)
        {
            this.name = name;
            this.center = center;
            this.size = size;
            this.security = security;
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

    // void Start()
    // {
    //     Generate();
    // }

    [ContextMenu("Generate Villa")]
    public void Generate()
    {
        ClearOldMap();

        if (randomizeSeedOnPlay)
            seed = Random.Range(0, 999999);

        Random.InitState(seed);

        CreateMaterialsIfMissing();
        GenerateVillaLayout();
        BuildRoomGraph();
        ValidateReachability();

        // 視覺用大地板，可選，但不要讓 pathfinding 使用它
        CreateOnePieceFloor();

        // 真正的 rooms / walls / obstacles
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

        Physics.SyncTransforms();

    #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    #endif

        Debug.Log("[VillaPCG] Runtime villa map generated.");

        RebuildNavigationAfterGenerate();
    }

    [ContextMenu("Clear Generated Villa")]
    public void ClearGeneratedVilla()
    {
        ClearOldMap();
    }

    [ContextMenu("Regenerate With New Seed")]
    public void RegenerateWithNewSeed()
    {
        seed = Random.Range(0, 999999);
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
                Bounds mapBounds = GetGeneratedMapBounds(gridMapPadding);

                gridMap.gridCenter = mapBounds.center;
                gridMap.width = mapBounds.size.x;
                gridMap.length = mapBounds.size.z;

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
                waypointGraph.GenerateGraph();
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
        List<GameObject> toDelete = new List<GameObject>();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            toDelete.Add(transform.GetChild(i).gameObject);
        }

        foreach (GameObject obj in toDelete)
        {
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
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
            restrictedFloorMat.color = new Color(0.45f, 0.35f, 0.35f);
        }

        if (coverMat == null)
        {
            coverMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coverMat.color = new Color(0.35f, 0.22f, 0.12f);
        }
    }

    void GenerateVillaLayout()
    {
        // Small random variation, but keep the villa readable and connected.
        float lobbyW = Random.Range(8.0f, 10.0f);
        float hallW = Random.Range(7.0f, 9.0f);
        float sideW = Random.Range(6.0f, 7.5f);

        Room courtyard = AddRoom("Courtyard / Entrance", new Vector2(0, -9), new Vector2(10, 5), SecurityLevel.Public);
        Room lobby = AddRoom("Lobby", new Vector2(0, -3), new Vector2(lobbyW, 6), SecurityLevel.Public);
        Room mainHall = AddRoom("Main Hall", new Vector2(0, 4), new Vector2(hallW, 8), SecurityLevel.SemiRestricted);
        Room targetRoom = AddRoom("Target Room", new Vector2(0, 12), new Vector2(8, 5), SecurityLevel.Critical);

        Room westWing = AddRoom("West Wing", new Vector2(-8, 3), new Vector2(sideW, 6), SecurityLevel.SemiRestricted);
        Room eastWing = AddRoom("East Wing", new Vector2(8, 3), new Vector2(sideW, 6), SecurityLevel.SemiRestricted);

        Room guardRoom = AddRoom("Guard Room", new Vector2(-8, 10), new Vector2(6, 5), SecurityLevel.Restricted);
        Room safeRoom = AddRoom("Safe Room / Exit", new Vector2(8, 10), new Vector2(6, 5), SecurityLevel.Restricted);

        Room storage = AddRoom("Storage", new Vector2(-7, -4), new Vector2(5, 4), SecurityLevel.Public);
        Room kitchen = AddRoom("Kitchen", new Vector2(7, -4), new Vector2(5, 4), SecurityLevel.Public);

        ConnectRooms(courtyard, lobby, Side.North, Side.South);
        ConnectRooms(lobby, mainHall, Side.North, Side.South);
        ConnectRooms(mainHall, targetRoom, Side.North, Side.South);

        ConnectRooms(lobby, storage, Side.West, Side.East);
        ConnectRooms(lobby, kitchen, Side.East, Side.West);

        ConnectRooms(mainHall, westWing, Side.West, Side.East);
        ConnectRooms(mainHall, eastWing, Side.East, Side.West);

        ConnectRooms(westWing, guardRoom, Side.North, Side.South);
        ConnectRooms(eastWing, safeRoom, Side.North, Side.South);

        ConnectRooms(guardRoom, targetRoom, Side.East, Side.West);
        ConnectRooms(safeRoom, targetRoom, Side.West, Side.East);
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
            z = a.Top;
        else if (sideA == Side.South)
            z = a.Bottom;
        else if (sideA == Side.East)
            x = a.Right;
        else if (sideA == Side.West)
            x = a.Left;

        return new Vector3(x, 0, z);
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
        bool canReachTarget = IsReachable("Courtyard / Entrance", "Target Room");
        bool targetCanReachSafe = IsReachable("Target Room", "Safe Room / Exit");

        if (!canReachTarget)
            Debug.LogWarning("[VillaPCG] Invalid map: Player cannot reach target room.");

        if (!targetCanReachSafe)
            Debug.LogWarning("[VillaPCG] Invalid map: Target cannot reach safe room.");

        if (canReachTarget && targetCanReachSafe)
            Debug.Log("[VillaPCG] Reachability validation passed.");
    }

    bool IsReachable(string start, string goal)
    {
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

            if (generateCoverObjects)
                CreateRoomCover(r);

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

        floor.layer = GetLayerSafe(walkableLayerName);

        Renderer renderer = floor.GetComponent<Renderer>();
        renderer.material = IsRestricted(r) ? restrictedFloorMat : floorMat;
    }

    bool IsRestricted(Room r)
    {
        return r.security == SecurityLevel.Restricted || r.security == SecurityLevel.Critical;
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

        wall.layer = GetLayerSafe(unwalkableLayerName);

        wall.transform.parent = transform;
        wall.transform.position = position;
        wall.transform.localScale = scale;

        Renderer renderer = wall.GetComponent<Renderer>();
        renderer.material = wallMat;
    }

    void CreateRoomCover(Room r)
    {
        float roomArea = r.size.x * r.size.y;
        float density = GetObstacleDensityBySecurity(r.security);

        // 根據房間面積與安全等級決定 obstacle 數量
        // 面積越大，障礙物越多；安全等級越高，密度越高
        int count = Mathf.RoundToInt((roomArea / 80.0f) * density);

        count = Mathf.Clamp(count, 0, maxObstaclesPerRoom);

        // Public 區域至少不強制生成很多，避免入口/大廳太堵
        if (r.security == SecurityLevel.Public)
        {
            count = Mathf.Clamp(count, 0, 2);
        }

        // Restricted / Critical 區域至少有一定遮蔽物
        if (r.security == SecurityLevel.Restricted)
        {
            count = Mathf.Max(count, 3);
        }
        else if (r.security == SecurityLevel.Critical)
        {
            count = Mathf.Max(count, 4);
        }

        for (int i = 0; i < count; i++)
        {
            CreateSingleObstacleInRoom(r, i);
        }
    }

    float GetObstacleDensityBySecurity(SecurityLevel security)
    {
        switch (security)
        {
            case SecurityLevel.Public:
                return publicObstacleDensity;

            case SecurityLevel.SemiRestricted:
                return semiRestrictedObstacleDensity;

            case SecurityLevel.Restricted:
                return restrictedObstacleDensity;

            case SecurityLevel.Critical:
                return criticalObstacleDensity;

            default:
                return 0.5f;
        }
    }

    void CreateRoomLabel(Room r)
    {
        GameObject labelObj = new GameObject("Label_" + r.name);
        labelObj.transform.parent = transform;
        labelObj.transform.position = new Vector3(r.center.x, 0.05f, r.center.y);

        TextMesh text = labelObj.AddComponent<TextMesh>();
        text.text = r.name + "\n" + r.security.ToString();
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

        Room courtyard = rooms.Find(r => r.name == "Courtyard / Entrance");
        if (courtyard != null)
        {
            spawn.transform.position = new Vector3(courtyard.center.x, 0.5f, courtyard.Bottom + 1.0f);
        }
        else
        {
            spawn.transform.position = new Vector3(0, 0.5f, -12);
        }
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
                
                floor.layer = GetLayerSafe(walkableLayerName);

                floor.transform.parent = root.transform;
                floor.transform.position = new Vector3(centerX, -floorThickness * 0.5f, centerZ);
                floor.transform.localScale = new Vector3(length + 0.5f, floorThickness, connectorFloorWidth);

                Renderer renderer = floor.GetComponent<Renderer>();
                renderer.material = floorMat;
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
                
                floor.layer = GetLayerSafe(walkableLayerName);
                
                floor.transform.parent = root.transform;
                floor.transform.position = new Vector3(centerX, -floorThickness * 0.5f, centerZ);
                floor.transform.localScale = new Vector3(connectorFloorWidth, floorThickness, length + 0.5f);

                Renderer renderer = floor.GetComponent<Renderer>();
                renderer.material = floorMat;
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

        wall.layer = GetLayerSafe(unwalkableLayerName);

        wall.transform.parent = root;
        wall.transform.position = position;
        wall.transform.localScale = scale;

        Renderer renderer = wall.GetComponent<Renderer>();
        renderer.material = wallMat;
    }

    void CreateSingleObstacleInRoom(Room r, int index)
    {
        float margin = 1.8f;

        if (r.size.x < margin * 2.0f || r.size.y < margin * 2.0f)
            return;

        float x = Random.Range(r.Left + margin, r.Right - margin);
        float z = Random.Range(r.Bottom + margin, r.Top - margin);

        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "Obstacle_" + r.name + "_" + index;

        obstacle.layer = GetLayerSafe(unwalkableLayerName);

        obstacle.transform.parent = transform;

        float width;
        float depth;

        // Public 區域障礙物小一點，不阻塞玩家
        if (r.security == SecurityLevel.Public)
        {
            width = Random.Range(1.0f, 1.8f);
            depth = Random.Range(1.0f, 1.8f);
        }
        // SemiRestricted 中等
        else if (r.security == SecurityLevel.SemiRestricted)
        {
            width = Random.Range(1.2f, 2.4f);
            depth = Random.Range(1.2f, 2.4f);
        }
        // Restricted / Critical 可以更大，更像掩體或大型家具
        else
        {
            width = Random.Range(1.5f, 3.0f);
            depth = Random.Range(1.5f, 3.0f);
        }

        float height = wallHeight;

        obstacle.transform.localScale = new Vector3(width, height, depth);
        obstacle.transform.position = new Vector3(x, height * 0.5f, z);

        Renderer renderer = obstacle.GetComponent<Renderer>();
        renderer.material = coverMat;
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
        int unwalkableLayer = LayerMask.NameToLayer(unwalkableLayerName);
        if (unwalkableLayer >= 0)
            wall.layer = unwalkableLayer;
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
