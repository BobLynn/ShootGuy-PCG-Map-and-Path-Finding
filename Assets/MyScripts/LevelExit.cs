using UnityEngine;
using UnityEngine.InputSystem;

public class LevelExit : MonoBehaviour
{
    public VillaPCG_v2 levelManager;
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
            if (levelManager != null)
                levelManager.GoToLevel(targetLevel);
        }
    }

    void EnsurePlayerReference()
    {
        if (player != null)
            return;

        CharacterController controller = Object.FindFirstObjectByType<CharacterController>();
        if (controller != null)
        {
            player = controller.transform;
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObject != null)
            player = playerObject.transform;
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
