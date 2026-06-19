using StarterAssets;
using UnityEngine;

public static class PlayerLocator
{
    public static Transform FindPlayerTransform(string playerTag)
    {
        if (!string.IsNullOrWhiteSpace(playerTag))
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject != null)
                return playerObject.transform;
        }

        ThirdPersonController thirdPersonController = Object.FindFirstObjectByType<ThirdPersonController>();
        if (thirdPersonController != null)
            return thirdPersonController.transform;

        CharacterController controller = FindPlayerCharacterController();
        return controller != null ? controller.transform : null;
    }

    public static CharacterController FindPlayerCharacterController()
    {
        CharacterController[] controllers = Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
        foreach (CharacterController controller in controllers)
        {
            if (controller.GetComponent<Agent>() != null)
                continue;

            return controller;
        }

        return null;
    }
}
