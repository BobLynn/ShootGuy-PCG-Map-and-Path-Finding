using UnityEngine;
using InventorySystem; // 幫你移除了原本重複的 using

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
    
    [Header("Trespassing Detection")]
    public bool isTrespassing = false; // Indicates if the player is trespassing in a restricted area
    public LayerMask restrictedLayer;  // 設定哪些圖層是「禁區」(Restricted Area)
    public float detectRadius = 0.5f;  // 檢測半徑
    public Vector3 detectOffset = new Vector3(0, 1f, 0); // 將檢測球體抬高到玩家的身體/腰部位置

    // Update is called once per frame
    void Update()
    {
        // ==========================================
        // 1. 禁區判定 (每幀 dt 執行)
        // ==========================================
        // 以玩家位置 + 偏移量為中心，打出一顆虛擬球體，檢查是否與 restrictedLayer 產生重疊
        Vector3 checkCenter = transform.position + detectOffset;
        isTrespassing = Physics.CheckSphere(checkCenter, detectRadius, restrictedLayer);


        // ==========================================
        // 2. 測試用的裝備輸入
        // ==========================================
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

    public int getEquipmentCount(string equipmentType)
    {
        Debug.Log($"Getting count for equipment type: {equipmentType}");
        switch (equipmentType)
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

    // ==========================================
    // 編輯器視覺化 (Visual Debugging)
    // ==========================================
    private void OnDrawGizmosSelected()
    {
        // 畫出檢測禁區的球體範圍
        // 如果正在禁區內顯示紅色，安全則顯示綠色
        Gizmos.color = isTrespassing ? new Color(1, 0, 0, 0.5f) : new Color(0, 1, 0, 0.5f);
        Vector3 checkCenter = transform.position + detectOffset;
        Gizmos.DrawSphere(checkCenter, detectRadius);
    }
}