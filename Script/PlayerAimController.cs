using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

public class PlayerAimController : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private GameObject normalCamera;
    [SerializeField] private GameObject aimCamera;

    private void Update()
    {
        // Safety check to make sure you dragged them into the inspector slots
        if (normalCamera == null || aimCamera == null) return;

        // Grab the Cinemachine components off your GameObjects
        var normalCinemachine = normalCamera.GetComponent<CinemachineVirtualCamera>();
        var aimCinemachine = aimCamera.GetComponent<CinemachineVirtualCamera>();

        // Check if the mouse exists, then check the right button
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            aimCinemachine.Priority.Value = 15;
        }
        else
        {
            aimCinemachine.Priority.Value = 5;
        }
    }
}