using UnityEngine;
using UnityEngine.InputSystem;

public class LevelExit : MonoBehaviour
{
    public VillaPCG_v2 levelManager;
    public VillaPCG_v3 levelManagerV3;
    public VillaPCG_v4 levelManagerV4;
    public int targetLevel = 2;
    public Key interactKey = Key.E;
    public float interactRadius = 2.2f;
    public string playerTag = "Player";
    public string prompt = "Press E to enter next level";

    private Transform player;
    private ILevelNavigator levelNavigator;
    private bool activated;
    private GUIStyle promptStyle;

    void Awake()
    {
        if (levelManager == null)
            levelManager = Object.FindFirstObjectByType<VillaPCG_v2>();

        if (levelManagerV3 == null)
            levelManagerV3 = Object.FindFirstObjectByType<VillaPCG_v3>();

        if (levelManagerV4 == null)
            levelManagerV4 = Object.FindFirstObjectByType<VillaPCG_v4>();

        levelNavigator = ResolveLevelNavigator();
    }

    void Update()
    {
        if (activated)
            return;

        EnsurePlayerReference();

        if (!IsPlayerInRange())
            return;

        if (Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame)
        {
            levelNavigator ??= ResolveLevelNavigator();
            if (levelNavigator == null)
            {
                Debug.LogWarning("[LevelExit] No ILevelNavigator found. Assign a VillaPCG manager or add ILevelNavigator to the level manager.");
                return;
            }

            activated = true;
            levelNavigator.GoToLevel(targetLevel);
        }
    }

    ILevelNavigator ResolveLevelNavigator()
    {
        if (levelManagerV4 != null)
            return levelManagerV4;

        if (levelManagerV3 != null)
            return levelManagerV3;

        if (levelManager != null)
            return levelManager;

        MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is ILevelNavigator navigator)
                return navigator;
        }

        return null;
    }

    void EnsurePlayerReference()
    {
        if (player != null)
            return;

        player = PlayerLocator.FindPlayerTransform(playerTag);
    }

    bool IsPlayerInRange()
    {
        if (player == null)
            return false;

        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= interactRadius * interactRadius;
    }

    void OnGUI()
    {
        EnsurePlayerReference();

        if (!IsPlayerInRange())
            return;

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.box);
            promptStyle.alignment = TextAnchor.MiddleCenter;
            promptStyle.fontSize = 18;
            promptStyle.normal.textColor = Color.white;
        }

        float width = 360f;
        float height = 48f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 120f, width, height);
        GUI.Box(rect, prompt, promptStyle);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
