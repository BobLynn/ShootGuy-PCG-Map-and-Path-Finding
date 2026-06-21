using UnityEngine;
using System;

// 定義一個聲音刺激的資料結構
public struct AudioStimulus
{
    public Vector3 position;    // 聲音發出的位置
    public float radius;        // 聲音傳播的最大半徑
    public string type;         // 聲音類型 (例如 "Footstep", "CoinDrop", "Gunshot")
    public GameObject source;   // 是誰發出這個聲音的
    public GameObject target;   // 聲音指涉的具體目標 (例如被發現的威脅)
}

public class StimulusManager : MonoBehaviour
{
    // 單例模式，方便全域呼叫
    public static StimulusManager Instance { get; private set; }

    // 定義一個委派事件，所有的感知系統都會「訂閱」這個事件
    public static event Action<AudioStimulus> OnAudioStimulusCreated;

    [Header("Environment Validation")]
    [Tooltip("哪些圖層算是會卡死聲音的障礙物 (例如 Wall, Obstacle)")]
    public LayerMask obstacleLayers;
    
    [Tooltip("如果聲音卡在牆內，沿著反方向退後尋找空地的最大距離")]
    public float searchRadius = 3.0f;
    [Tooltip("最多切分幾步來退後尋找合法座標")]
    public int maxRetryCount = 15;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this);
        else Instance = this;
    }

    /// <summary>
    /// 全域廣播聲音刺激
    /// </summary>
    public void BroadcastAudioStimulus(Vector3 pos, float radius, string type, GameObject source, GameObject target = null)
    {
        Vector3 validPosition = GetValidStimulusPosition(pos, source);

        AudioStimulus stimulus = new AudioStimulus
        {
            position = validPosition,
            radius = radius,
            type = type,
            source = source,
            target = target
        };

        OnAudioStimulusCreated?.Invoke(stimulus);

        // Debug 視覺化：畫出聲音的傳播範圍 (黃色代表原點，綠色代表修正後的實際廣播點)
        if (!ShouldDrawStimulusDebug(source))
            return;

        Debug.DrawRay(pos, Vector3.up * radius, Color.yellow, 1.0f);
        if (pos != validPosition)
        {
            Debug.DrawLine(pos, validPosition, Color.red, 1.0f); // 畫出退後的軌跡
            Debug.DrawRay(validPosition, Vector3.up * (radius * 0.5f), Color.green, 1.0f);
        }
    }

    private bool ShouldDrawStimulusDebug(GameObject source)
    {
        if (source == null)
            return true;

        Agent sourceAgent = source.GetComponent<Agent>();
        return sourceAgent == null || sourceAgent.showDebugGizmos;
    }

    /// <summary>
    /// 檢查座標是否在牆內。若是，則沿著物件來時的方向退後，並確保踩在 Default Layer 上。
    /// </summary>
    private Vector3 GetValidStimulusPosition(Vector3 originalPos, GameObject source)
    {
        Vector3 checkCenter = originalPos + Vector3.up * 1.0f;
        
        // 1. 如果原本的位置沒有跟障礙物重疊，就直接回傳原位置
        if (!Physics.CheckSphere(checkCenter, 0.5f, obstacleLayers))
        {
            return originalPos; 
        }

        // 2. 找出物件「來時的方向」
        Vector3 incomingDir = Vector3.zero;
        if (source != null)
        {
            if (source.TryGetComponent<Rigidbody>(out Rigidbody rb) && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.1f)
            {
                incomingDir = rb.linearVelocity.normalized;
            }
            else
            {
                incomingDir = source.transform.forward;
            }
        }

        if (incomingDir == Vector3.zero) incomingDir = Vector3.forward;

        // 往「來時的反方向」退後 (Backtracking)
        Vector3 backtrackDir = -incomingDir;
        backtrackDir.y = 0;
        backtrackDir.Normalize();

        if (backtrackDir == Vector3.zero) backtrackDir = Vector3.back;

        // 3. 沿著來時的路徑往後退，尋找合法空地
        float step = searchRadius / maxRetryCount; 
        int defaultLayer = LayerMask.NameToLayer("Default");

        // ✨ 建立專屬的射線過濾遮罩：只看 Default 層 和 障礙物層
        int checkMask = (1 << defaultLayer) | obstacleLayers.value;

        for (int i = 1; i <= maxRetryCount; i++)
        {
            Vector3 testPos = originalPos + backtrackDir * (step * i);
            Vector3 testCheckCenter = testPos + Vector3.up * 1.0f;

            if (!Physics.CheckSphere(testCheckCenter, 0.5f, obstacleLayers))
            {
                Vector3 rayStart = testPos + Vector3.up * 2.0f;
                
                // ✨ 加上 checkMask 與 QueryTriggerInteraction.Ignore
                // 這條射線會直接穿透你設在空中的「其他 Layer」與「所有 Trigger」，直達地板！
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 5.0f, checkMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.gameObject.layer == defaultLayer)
                    {
                        return hit.point;
                    }
                    else 
                    {
                        // 因為射線只會打到 Default 或 Obstacle，所以走到這裡代表腳下是障礙物
                        UnityEngine.Debug.Log($"[StimulusManager] 位置 {testPos} 腳下是障礙物 ({hit.collider.name})，繼續退後尋找...");
                    }
                }
            }
        }

        Debug.LogWarning($"[StimulusManager] 聲音座標 {originalPos} 沿路退後找不到合法的 Default Layer 空地！");
        return originalPos;
    }
}
