using System.Collections.Generic;
using UnityEngine;

public class BulletPool : MonoBehaviour
{
    // 使用 Singleton 模式，方便其他腳本直接呼叫
    public static BulletPool Instance { get; private set; }

    [Header("Pool Settings")]
    public GameObject bulletPrefab;
    public GameObject coinPrefab;
    public GameObject explosionPrefab;
    public int initialPoolSize = 20; // 一開始準備幾顆子彈

    [Header("Debug")]
    public bool showDebugLogs = false;
    private bool hasLoggedMissingBulletPrefab;
    private bool hasLoggedMissingCoinPrefab;
    private bool hasLoggedMissingExplosionPrefab;

    // 存放子彈的佇列 (Queue 適合先進先出)
    private Queue<GameObject> bulletPool = new Queue<GameObject>();
    private Queue<GameObject> coinPool = new Queue<GameObject>();
    private Queue<GameObject> explosionPool = new Queue<GameObject>();

    void Awake()
    {
        // 設定 Singleton
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 初始化物件池
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewBullet();
            CreateNewCoin();
            // CreateNewExplosion();
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private GameObject CreateNewBullet()
    {
        if (bulletPrefab == null)
        {
            LogMissingPrefabOnce(ref hasLoggedMissingBulletPrefab, "bulletPrefab");
            return null;
        }

        // 生成子彈，並把 BulletPoolManager 當作它的父物件 (保持 Hierarchy 乾淨)
        GameObject obj = Instantiate(bulletPrefab, transform);
        obj.SetActive(false); // 先隱藏
        bulletPool.Enqueue(obj);
        return obj;
    }
    private GameObject CreateNewCoin()
    {
        if (coinPrefab == null)
        {
            LogMissingPrefabOnce(ref hasLoggedMissingCoinPrefab, "coinPrefab");
            return null;
        }

        GameObject obj = Instantiate(coinPrefab, transform);
        obj.SetActive(false);
        coinPool.Enqueue(obj);
        return obj;
    }
    private GameObject CreateNewExplosion()
    {
        if (explosionPrefab == null)
        {
            LogMissingPrefabOnce(ref hasLoggedMissingExplosionPrefab, "explosionPrefab");
            return null;
        }

        GameObject obj = Instantiate(explosionPrefab, transform);
        obj.SetActive(false);
        explosionPool.Enqueue(obj);
        return obj;
    }
    /// <summary>
    /// 跟池子借一顆子彈
    /// </summary>
    public GameObject GetBullet(Vector3 position, Quaternion rotation)
    {
        GameObject bullet;

        // 如果池子裡還有子彈，就拿出來；如果沒有了(例如射速太快)，就臨時生一顆
        if (bulletPool.Count > 0)
        {
            bullet = bulletPool.Dequeue();
        }
        else
        {
            bullet = CreateNewBullet();
            if (bullet != null)
                bulletPool.Dequeue(); // 因為 CreateNewBullet 會把它加進 Queue，所以要立刻拿出來
        }

        if (bullet == null)
            return null;

        // 設定位置與旋轉，並啟動它
        bullet.transform.position = position;
        bullet.transform.rotation = rotation;
        bullet.SetActive(true);

        LogDebug($"BulletPool: provided bullet. Remaining bullets = {bulletPool.Count}");

        return bullet;
    }
    public GameObject GetCoin(Vector3 position, Quaternion rotation)
    {
        GameObject coin;

        if (coinPool.Count > 0)
        {
            coin = coinPool.Dequeue();
        }
        else
        {
            coin = CreateNewCoin();
            if (coin != null)
                coinPool.Dequeue();
        }

        if (coin == null)
            return null;

        coin.transform.position = position;
        coin.transform.rotation = rotation;
        coin.SetActive(true);

        LogDebug($"CoinPool: provided coin. Remaining coins = {coinPool.Count}");
        return coin;
    }
    public GameObject GetExplosion(Vector3 position, Quaternion rotation)
    {
        GameObject explosion;

        if (explosionPool.Count > 0)
        {
            explosion = explosionPool.Dequeue();
        }
        else
        {
            explosion = CreateNewExplosion();
            if (explosion != null)
                explosionPool.Dequeue();
        }

        if (explosion == null)
            return null;

        explosion.transform.position = position;
        explosion.transform.rotation = rotation;
        explosion.SetActive(true);

        LogDebug($"ExplosionPool: provided explosion. Remaining explosions = {explosionPool.Count}");
        return explosion;
    }

    /// <summary>
    /// 把子彈還給池子
    /// </summary>
    public void ReturnBullet(GameObject bullet)
    {
        bullet.SetActive(false); // 隱藏子彈
        bulletPool.Enqueue(bullet);    // 塞回佇列末端
    }
    public void ReturnCoin(GameObject coin)
    {
        coin.SetActive(false);
        coinPool.Enqueue(coin);
    }
    public void ReturnExplosion(GameObject explosion)
    {
        explosion.SetActive(false);
        explosionPool.Enqueue(explosion);
    }

    private void LogDebug(string message)
    {
        if (!showDebugLogs)
            return;

        Debug.Log($"[BulletPool] {message}");
    }

    private void LogMissingPrefabOnce(ref bool hasLogged, string fieldName)
    {
        if (hasLogged)
            return;

        hasLogged = true;
        Debug.LogWarning($"[BulletPool] Missing {fieldName}; cannot create pooled object.");
    }
}
