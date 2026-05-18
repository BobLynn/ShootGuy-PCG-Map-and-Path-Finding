using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class BulletPool : MonoBehaviour
{
    // 使用 Singleton 模式，方便其他腳本直接呼叫
    public static BulletPool Instance { get; private set; }

    [Header("Pool Settings")]
    public GameObject bulletPrefab;
    public int initialPoolSize = 20; // 一開始準備幾顆子彈

    // 存放子彈的佇列 (Queue 適合先進先出)
    private Queue<GameObject> pool = new Queue<GameObject>();

    void Awake()
    {
        // 設定 Singleton
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 初始化物件池
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewBullet();
        }
    }

    private GameObject CreateNewBullet()
    {
        // 生成子彈，並把 BulletPoolManager 當作它的父物件 (保持 Hierarchy 乾淨)
        GameObject obj = Instantiate(bulletPrefab, transform);
        obj.SetActive(false); // 先隱藏
        pool.Enqueue(obj);
        return obj;
    }

    /// <summary>
    /// 跟池子借一顆子彈
    /// </summary>
    public GameObject GetBullet(Vector3 position, Quaternion rotation)
    {
        GameObject bullet;

        // 如果池子裡還有子彈，就拿出來；如果沒有了(例如射速太快)，就臨時生一顆
        if (pool.Count > 0)
        {
            bullet = pool.Dequeue();
        }
        else
        {
            bullet = CreateNewBullet();
            pool.Dequeue(); // 因為 CreateNewBullet 會把它加進 Queue，所以要立刻拿出來
        }

        // 設定位置與旋轉，並啟動它
        bullet.transform.position = position;
        bullet.transform.rotation = rotation;
        bullet.SetActive(true);

        UnityEngine.Debug.Log($"BulletPool: 提供了一顆子彈，目前池子裡還有 {pool.Count} 顆");

        return bullet;
    }

    /// <summary>
    /// 把子彈還給池子
    /// </summary>
    public void ReturnBullet(GameObject bullet)
    {
        bullet.SetActive(false); // 隱藏子彈
        pool.Enqueue(bullet);    // 塞回佇列末端
    }
}