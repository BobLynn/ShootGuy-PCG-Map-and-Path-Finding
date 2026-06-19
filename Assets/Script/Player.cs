using UnityEngine;

public class Player : MonoBehaviour
{
    public string playerName = "Player";

    [Header("Debug")]
    public bool showDebugLogs = false;

    public enum EquipmentType
    {
        None,
        Gun,
        Coin,
        Medkit
    }
    public EquipmentType currentEquipment = EquipmentType.None;
    // This is used for player status, including health, ammo, coins, etc.
    public int health = 100;
    public int coinCount = 10;
    public int bulletCount = 30;
    public int medkitCount = 5;

    // Update is called once per frame
    void Update()
    {
        // For testing purposes, we can use keyboard input to simulate picking up and using items
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Equip(EquipmentType.Gun);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            Equip(EquipmentType.Coin);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            Equip(EquipmentType.Medkit);
        }
    }

    public void Equip(EquipmentType equipment)
    {
        if (currentEquipment == equipment)
            return;

        currentEquipment = equipment;
        LogDebug($"Equipped {equipment}");
    }

    public int getEquipmentCount(string type)
    {
        switch (type)
        {
            case "Bullet":
                return bulletCount;
            case "Coin":
                return coinCount;
            case "Med":
                return medkitCount;
            default:
                return 0;
        }
    }

    private void LogDebug(string message)
    {
        if (!showDebugLogs)
            return;

        Debug.Log($"[Player] {message}");
    }
}
