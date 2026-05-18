using UnityEngine;
using System;

// 定義一個聲音刺激的資料結構
public struct AudioStimulus
{
    public Vector3 position;    // 聲音發出的位置
    public float radius;        // 聲音傳播的最大半徑
    public string type;         // 聲音類型 (例如 "Footstep", "CoinDrop", "Gunshot")
    public GameObject source;   // 是誰發出這個聲音的
}

public class StimulusManager : MonoBehaviour
{
    // 單例模式，方便全域呼叫
    public static StimulusManager Instance { get; private set; }

    // 定義一個委派事件，所有的感知系統都會「訂閱」這個事件
    public static event Action<AudioStimulus> OnAudioStimulusCreated;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this);
        else Instance = this;
    }

    /// <summary>
    /// 全域廣播聲音刺激
    /// 例如：玩家丟硬幣落地時，呼叫 StimulusManager.Instance.BroadcastAudioStimulus(...)
    /// </summary>
    public void BroadcastAudioStimulus(Vector3 pos, float radius, string type, GameObject source)
    {
        AudioStimulus stimulus = new AudioStimulus
        {
            position = pos,
            radius = radius,
            type = type,
            source = source
        };

        // 如果有人訂閱這個事件，就把聲音廣播給他們
        OnAudioStimulusCreated?.Invoke(stimulus);

        // Debug 視覺化：畫出聲音的傳播範圍 (只在 Editor 中顯示短短一瞬間)
        Debug.DrawRay(pos, Vector3.up * radius, Color.yellow, 1.0f);
    }
}