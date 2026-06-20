using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class Coin : MonoBehaviour
{
    private Rigidbody rb;
    private SphereCollider sphereCollider;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();
        
        // 設為 Trigger，讓它走 OnTriggerEnter
        sphereCollider.isTrigger = true; 
    }

    public void Toss(double initialForce, Vector3 direction)
    {
        // 確保剛體受到物理與重力影響 (拋物線)
        rb.isKinematic = false; 
        
        rb.linearVelocity = Vector3.zero;
        Vector3 initialVelocity = direction.normalized * (float)initialForce;
        rb.linearVelocity = initialVelocity; 
    }

    void OnTriggerEnter(Collider other)
    {
        // 由於 Trigger 沒有 collision.relativeVelocity，我們直接取剛體當下的速度來判斷
        if (rb.linearVelocity.magnitude > 2 && (other.gameObject.layer == LayerMask.NameToLayer("Default") || other.gameObject.layer == LayerMask.NameToLayer("Obstacles")))
        {
            StimulusManager.Instance.BroadcastAudioStimulus(transform.position, 15f, "CoinDrop", gameObject);
        }

        // 碰到地板/障礙物時，立刻凍結它，避免它穿模掉下去
        // (假設你的地板 Layer 叫 Obstacle，或你可以用 Tag 判斷)
        if (other.gameObject.layer == LayerMask.NameToLayer("Default"))
        {
            // rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true; // 關閉物理運算，硬幣就會停在 Trigger 觸發的位置
        }
        else if (other.gameObject.layer == LayerMask.NameToLayer("Obstacles"))
        {
            rb.isKinematic = true; // 碰到障礙物也停下來
        }
    }
}