using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

public class PlayerAimController : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private GameObject normalCamera;
    [SerializeField] private GameObject aimCamera;

    [Header("Cinemachine Target")]
    [Tooltip("Drag the Main Camera GameObject here to read its direction.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Animation")]
    [Tooltip("Drag your player character model here (the one with the Animator component).")]
    [SerializeField] private Animator animator;

    [Header("Weapon Settings")]
    [Tooltip("Drag the Gun GameObject that is parented under the Right Hand bone here.")]
    [SerializeField] private GameObject weaponProp;

    [Header("UI Crosshair Canvas Element")]
    [Tooltip("Drag your Canvas Crosshair Game Object here.")]
    [SerializeField] private GameObject uiCrosshair;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = true;

        // Auto-fetch the animator on the same object if you forgot to assign it
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (weaponProp != null)
        {
            weaponProp.SetActive(false);
        }
    }

    private void Update()
    {
        // Safety check to make sure you dragged them into the inspector slots
        if (normalCamera == null || aimCamera == null) return;

        // Auto-fetch the main camera transform if you forgot to assign it
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Grab the Cinemachine components off your GameObjects
        var normalCinemachine = normalCamera.GetComponent<CinemachineVirtualCamera>();
        var aimCinemachine = aimCamera.GetComponent<CinemachineVirtualCamera>();

        // Check if the mouse exists, then check the right button
        bool isRightClickPressed = Mouse.current != null && Mouse.current.rightButton.isPressed;

        if (isRightClickPressed)
        {
            aimCinemachine.Priority.Value = 15;
            // If we are aiming, force the character model to face the exact same direction the camera is looking!
            if (cameraTransform != null)
            {
                // Get the camera's current forward angle, but wipe out the up/down tilt (Y-axis only)
                Vector3 targetForward = cameraTransform.forward;
                targetForward.y = 0; // Stops the player from tilting face-first into the dirt when looking down

                if (targetForward != Vector3.zero)
                {
                    // Create the rotation rotation rotation mapping
                    Quaternion targetRotation = Quaternion.LookRotation(targetForward);

                    // Instantly snap (or smoothly slide) the player's body to match the camera
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 100f);
                }
            }
        }
        else
        {
            aimCinemachine.Priority.Value = 5;
        }

        // --- NEW ANIMATION CODE ---
        // Pass the right-click state straight to the animator parameter!
        if (animator != null)
        {
            animator.SetBool("IsAiming", isRightClickPressed);
        }

        // Toggle the central UI crosshair to match your aim state perfectly!
        if (uiCrosshair != null)
        {
            uiCrosshair.SetActive(isRightClickPressed);
        }

        if (weaponProp != null)
        {
            weaponProp.SetActive(isRightClickPressed);
        }

        if (isRightClickPressed && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (animator != null)
            {
                // Tell the animator to instantly play the shooting animation clip
                animator.SetTrigger("Shoot");
            }
        }
    }
}