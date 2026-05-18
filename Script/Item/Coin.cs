using UnityEngine;

public class Coin : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        // 當硬幣撞到地板時
        if (collision.relativeVelocity.magnitude > 2)
        {
            StimulusManager.Instance.BroadcastAudioStimulus(transform.position, 20f, "CoinDrop", gameObject);
        }
    }
}
