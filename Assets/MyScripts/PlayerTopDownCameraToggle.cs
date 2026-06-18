using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerTopDownCameraToggle : MonoBehaviour
{
    [Header("Input")]
    public Key toggleKey = Key.O;

    [Header("Cameras")]
    public Camera gameplayCamera;
    public Camera topDownCamera;
    public GameObject gameplayCameraRig;
    public GameObject topDownCameraRig;

    [Header("Top-Down Settings")]
    public Vector3 topDownCenter = Vector3.zero;
    public float topDownHeight = 60f;
    public float orthographicSize = 45f;
    public Transform mapCenter;

    [Header("Top-Down Behavior")]
    public bool createTopDownCameraIfMissing = true;
    public bool freezeWorldTime = true;
    public bool disablePlayerControl = true;

    [Tooltip("把玩家控制相關 script 拖進來，例如 PlayerInput / ThirdPersonController / StarterAssetsInputs")]
    public MonoBehaviour[] playerControlScriptsToDisable;

    [Tooltip("如果有整個控制 Rig / Input 物件，也可以拖進來一起停用")]
    public GameObject[] objectsToDisableInTopDown;

    [Header("You Are Here Marker")]
    public bool showYouAreHereMarker = true;
    public float markerGroundOffset = 0.05f;
    public float markerScale = 1.0f;
    public Color markerColor = Color.red;
    public float labelHeightOffset = 2.0f;

    private bool isTopDown = false;
    private float originalFixedDeltaTime;
    private GameObject markerRoot;
    private GUIStyle labelStyle;

    [Header("Top-Down Zoom")]
    public bool enableTopDownZoom = true;
    public float zoomSpeed = 3.0f;
    public float minOrthographicSize = 15.0f;
    public float maxOrthographicSize = 90.0f;

    void Start()
    {
        originalFixedDeltaTime = Time.fixedDeltaTime;

        if (gameplayCamera == null)
        {
            gameplayCamera = Camera.main;
        }

        if (topDownCamera == null && createTopDownCameraIfMissing)
        {
            CreateTopDownCamera();
        }

        SetupTopDownCameraPosition();

        if (showYouAreHereMarker)
        {
            CreateYouAreHereMarker();
        }

        SetTopDownMode(false);
    }

    void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            SetTopDownMode(!isTopDown);
        }

        if (isTopDown && enableTopDownZoom)
        {
            HandleTopDownZoom();
        }

        if (showYouAreHereMarker && markerRoot != null)
        {
            UpdateMarkerTransform();
        }
    }

    void CreateTopDownCamera()
    {
        GameObject camObj = new GameObject("TopDownCamera_Runtime");
        topDownCamera = camObj.AddComponent<Camera>();

        topDownCamera.orthographic = true;
        topDownCamera.orthographicSize = orthographicSize;
        topDownCamera.clearFlags = CameraClearFlags.Skybox;
        topDownCamera.depth = 10;
        topDownCamera.enabled = false;

        AudioListener listener = camObj.GetComponent<AudioListener>();
        if (listener != null)
        {
            Destroy(listener);
        }
    }

    void SetupTopDownCameraPosition()
    {
        if (topDownCamera == null)
            return;

        Vector3 center = mapCenter != null ? mapCenter.position : topDownCenter;

        topDownCamera.transform.position = new Vector3(center.x, topDownHeight, center.z);
        topDownCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        topDownCamera.orthographic = true;
        topDownCamera.orthographicSize = orthographicSize;
    }

    void SetTopDownMode(bool enableTopDown)
    {
        isTopDown = enableTopDown;

        // Camera switching
        if (gameplayCamera != null)
            gameplayCamera.enabled = !enableTopDown;

        if (topDownCamera != null)
            topDownCamera.enabled = enableTopDown;

        if (gameplayCameraRig != null)
            gameplayCameraRig.SetActive(!enableTopDown);

        if (topDownCameraRig != null)
            topDownCameraRig.SetActive(enableTopDown);

        if (enableTopDown)
            SetupTopDownCameraPosition();

        // Disable player control
        if (disablePlayerControl)
        {
            if (playerControlScriptsToDisable != null)
            {
                foreach (MonoBehaviour mb in playerControlScriptsToDisable)
                {
                    if (mb != null)
                        mb.enabled = !enableTopDown;
                }
            }

            if (objectsToDisableInTopDown != null)
            {
                foreach (GameObject go in objectsToDisableInTopDown)
                {
                    if (go != null)
                        go.SetActive(!enableTopDown);
                }
            }
        }

        // Freeze time
        if (freezeWorldTime)
        {
            if (enableTopDown)
            {
                Time.timeScale = 0f;
                Time.fixedDeltaTime = 0f;
                AudioListener.pause = true;
            }
            else
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = originalFixedDeltaTime;
                AudioListener.pause = false;
            }
        }

        // Marker visible only in top-down
        if (markerRoot != null)
            markerRoot.SetActive(enableTopDown);

        Debug.Log(enableTopDown ? "[Camera] Top-down view ON" : "[Camera] Gameplay view ON");
    }

    void CreateYouAreHereMarker()
    {
        markerRoot = new GameObject("YouAreHereMarker");
        markerRoot.transform.SetParent(null);

        Material mat = CreateMarkerMaterial();

        // Arrow shaft
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaft.name = "ArrowShaft";
        shaft.transform.SetParent(markerRoot.transform, false);
        shaft.transform.localScale = new Vector3(0.18f, 0.02f, 0.7f) * markerScale;
        shaft.transform.localPosition = new Vector3(0f, 0f, 0f);
        ApplyMarkerMaterial(shaft, mat);

        // Arrow head left
        GameObject headL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        headL.name = "ArrowHeadLeft";
        headL.transform.SetParent(markerRoot.transform, false);
        headL.transform.localScale = new Vector3(0.14f, 0.02f, 0.38f) * markerScale;
        headL.transform.localPosition = new Vector3(-0.12f * markerScale, 0f, 0.25f * markerScale);
        headL.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        ApplyMarkerMaterial(headL, mat);

        // Arrow head right
        GameObject headR = GameObject.CreatePrimitive(PrimitiveType.Cube);
        headR.name = "ArrowHeadRight";
        headR.transform.SetParent(markerRoot.transform, false);
        headR.transform.localScale = new Vector3(0.14f, 0.02f, 0.38f) * markerScale;
        headR.transform.localPosition = new Vector3(0.12f * markerScale, 0f, 0.25f * markerScale);
        headR.transform.localRotation = Quaternion.Euler(0f, -45f, 0f);
        ApplyMarkerMaterial(headR, mat);

        // Optional center dot
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dot.name = "MarkerDot";
        dot.transform.SetParent(markerRoot.transform, false);
        dot.transform.localScale = new Vector3(0.22f, 0.01f, 0.22f) * markerScale;
        dot.transform.localPosition = new Vector3(0f, -0.01f, -0.08f);
        ApplyMarkerMaterial(dot, mat);

        UpdateMarkerTransform();
        markerRoot.SetActive(false);
    }

    void UpdateMarkerTransform()
    {
        if (markerRoot == null)
            return;

        Vector3 pos = transform.position;
        pos.y = markerGroundOffset;
        markerRoot.transform.position = pos;

        // 箭頭朝向 player 的 forward
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;

        if (flatForward.sqrMagnitude > 0.0001f)
        {
            markerRoot.transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        }
    }

    Material CreateMarkerMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", markerColor);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", markerColor);

        return mat;
    }

    void ApplyMarkerMaterial(GameObject go, Material mat)
    {
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material = mat;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Destroy(col);
    }

    void OnGUI()
    {
        if (!isTopDown || !showYouAreHereMarker || topDownCamera == null)
            return;

        Vector3 worldPos = transform.position + Vector3.up * labelHeightOffset;
        Vector3 screenPos = topDownCamera.WorldToScreenPoint(worldPos);

        if (screenPos.z <= 0f)
            return;

        float guiX = screenPos.x;
        float guiY = Screen.height - screenPos.y;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 10;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = Color.red;
        }

        Rect rect = new Rect(guiX - 80f, guiY - 35f, 160f, 30f);
        GUI.Label(rect, "YOU ARE HERE", labelStyle);
    }

    void OnDisable()
    {
        // 保險：如果 script 被停用，恢復時間
        Time.timeScale = 1f;
        Time.fixedDeltaTime = originalFixedDeltaTime;
        AudioListener.pause = false;
    }

    void HandleTopDownZoom()
    {
        if (topDownCamera == null)
            return;

        if (Mouse.current == null)
            return;

        float scrollY = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(scrollY) < 0.01f)
            return;

        // 滾輪往上通常是 zoom in，所以 orthographicSize 要變小
        float zoomDelta = -scrollY * zoomSpeed * 0.01f;

        float newSize = topDownCamera.orthographicSize + zoomDelta;

        newSize = Mathf.Clamp(
            newSize,
            minOrthographicSize,
            maxOrthographicSize
        );

        topDownCamera.orthographicSize = newSize;

        // 記住目前縮放，下次切回俯瞰時沿用
        orthographicSize = newSize;
    }
}