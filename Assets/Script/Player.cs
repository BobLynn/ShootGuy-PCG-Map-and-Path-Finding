using UnityEngine;
using InventorySystem;

public class Player : MonoBehaviour
{
    public string playerName = "Player";
    public enum EquipmentType
    {
        None,
        Gun,
        Coin,
        medkit
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
            currentEquipment = EquipmentType.Gun;
            Debug.Log("Equipped Gun");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            currentEquipment = EquipmentType.Coin;
            Debug.Log("Equipped Coin");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            currentEquipment = EquipmentType.medkit;
            Debug.Log("Equipped Medkit");
        }
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
}