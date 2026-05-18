using System.Diagnostics;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class SimpleProjectile : MonoBehaviour
{
    public float speed = 20f;
    public float lifeTime = 5f;
    public int damage = 10;
    
    private Vector3 moveDirection;
    private bool isFired = false;
    private float currentLifeTimer = 0f; // 用來自己計時

    void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true; 
    }

    // 當子彈被 Pool 拿出來並 SetActive(true) 時會觸發
    void OnEnable()
    {
        currentLifeTimer = 0f; // 每次拿出來都要重置計時器
    }

    public void Fire(Vector3 direction)
    {
        moveDirection = direction.normalized;
        isFired = true;
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
        UnityEngine.Debug.Log($"子彈碰到了 {other.gameObject.name} (Layer: {LayerMask.LayerToName(other.gameObject.layer)})");
        if (other.gameObject.layer == LayerMask.NameToLayer("Obstacle"))
        {
            DisableAndReturn(); // 撞牆，回收
        }
        else if (other.CompareTag("Player"))
        {
            UnityEngine.Debug.Log("Hit Player!");
            // other.GetComponent<PlayerHealth>().TakeDamage(damage);
            DisableAndReturn(); // 擊中玩家，回收
        }
    }

    // 負責將子彈狀態重置並還給 Pool
    private void DisableAndReturn()
    {
        isFired = false;
        BulletPool.Instance.ReturnBullet(gameObject);
    }
}