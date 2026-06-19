using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class SimpleProjectile : MonoBehaviour
{
    public float speed = 20f;
    public float lifeTime = 5f;
    public int damage = 10;

    public bool onTriggerStimulus = true; // 是否在觸發時廣播刺激
    public bool showDebugLogs = false;
    
    private Vector3 moveDirection;
    private bool isFired = false;
    private float currentLifeTimer = 0f; // 用來自己計時
    private int obstacleLayer;
    private bool hasLoggedMissingStimulusManager;
    private bool hasLoggedMissingBulletPool;

    void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true;
        obstacleLayer = LayerMask.NameToLayer("Obstacle");
    }

    // 當子彈被 Pool 拿出來並 SetActive(true) 時會觸發
    void OnEnable()
    {
        currentLifeTimer = 0f; // 每次拿出來都要重置計時器
    }

    public void Fire(Vector3 direction, bool callStimulus = false)
    {
        moveDirection = direction.normalized;
        isFired = true;
        if (callStimulus)
        {
            TryBroadcastAudioStimulus(30f, "BulletFire");
        }
        // 移除了原本的 Destroy(gameObject, lifeTime);
    }

    void Update()
    {
        if (isFired)
        {
            transform.position += moveDirection * speed * Time.deltaTime;

            // 自行計算生命週期，超時就回收
            currentLifeTimer += Time.deltaTime;
            if (currentLifeTimer >= lifeTime)
            {
                DisableAndReturn();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        LogDebug($"Hit collider {other.gameObject.name} (Layer: {LayerMask.LayerToName(other.gameObject.layer)})");
        if (other.gameObject.layer == obstacleLayer)
        {
            DisableAndReturn(); // 撞牆，回收
            if (onTriggerStimulus)
            {
                TryBroadcastAudioStimulus(20f, "BulletTrigger");
            }
        }
        else if (other.CompareTag("Player"))
        {
            LogDebug("Hit Player.");
            // other.GetComponent<PlayerHealth>().TakeDamage(damage);
            DisableAndReturn(); // 擊中玩家，回收
        }
        else if (other.CompareTag("Enemy"))
        {
            LogDebug("Hit Enemy.");
            other.GetComponent<Agent>().TakeDamage(damage, transform.position); // 傳入子彈位置讓敵人知道從哪裡被打到的
            DisableAndReturn(); // 擊中敵人，回收
        }
    }

    // 負責將子彈狀態重置並還給 Pool
    private void DisableAndReturn()
    {
        isFired = false;

        if (BulletPool.Instance != null)
        {
            BulletPool.Instance.ReturnBullet(gameObject);
            return;
        }

        if (!hasLoggedMissingBulletPool)
        {
            hasLoggedMissingBulletPool = true;
            UnityEngine.Debug.LogWarning("[SimpleProjectile] Cannot return to pool: BulletPool is missing. Disabling projectile.");
        }

        gameObject.SetActive(false);
    }

    private void LogDebug(string message)
    {
        if (!showDebugLogs)
            return;

        UnityEngine.Debug.Log($"[SimpleProjectile] {message}");
    }

    private void TryBroadcastAudioStimulus(float radius, string stimulusType)
    {
        if (StimulusManager.Instance != null)
        {
            StimulusManager.Instance.BroadcastAudioStimulus(transform.position, radius, stimulusType, gameObject);
            return;
        }

        if (hasLoggedMissingStimulusManager)
            return;

        hasLoggedMissingStimulusManager = true;
        UnityEngine.Debug.LogWarning($"[SimpleProjectile] Cannot broadcast {stimulusType}: StimulusManager is missing.");
    }
}
