using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

public class LevelExit : MonoBehaviour
{
    public VillaPCG_v2 levelManager;
    public VillaPCG_v3 levelManagerV3;
    public int targetLevel = 2;
    public Key interactKey = Key.E;
    public float interactRadius = 2.2f;
    public string playerTag = "Player";
    public string prompt = "Press E to enter next level";

    private Transform player;
    private bool activated;
    private GUIStyle promptStyle;

    void Awake()
    {
        if (levelManager == null)
            levelManager = Object.FindFirstObjectByType<VillaPCG_v2>();

        if (levelManagerV3 == null)
            levelManagerV3 = Object.FindFirstObjectByType<VillaPCG_v3>();
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
            activated = true;
            if (levelManagerV3 != null)
                levelManagerV3.GoToLevel(targetLevel);
            else if (levelManager != null)
                levelManager.GoToLevel(targetLevel);
        }
    }

    void EnsurePlayerReference()
    {
        if (player != null)
            return;

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObject != null)
        {
            player = playerObject.transform;
            return;
        }

        ThirdPersonController thirdPersonController = Object.FindFirstObjectByType<ThirdPersonController>();
        if (thirdPersonController != null)
        {
            player = thirdPersonController.transform;
            return;
        }

        CharacterController[] controllers = Object.FindObjectsOfType<CharacterController>();
        foreach (CharacterController controller in controllers)
        {
            if (controller.GetComponent<Agent>() != null)
                continue;

            player = controller.transform;
            return;
        }
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
