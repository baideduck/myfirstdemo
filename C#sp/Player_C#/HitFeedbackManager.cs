using System.Collections;
using UnityEngine;

public class HitFeedbackManager : MonoBehaviour
{
    public static HitFeedbackManager Instance { get; private set; }

    [Header("特效")]
    public GameObject normalHitSpark;
    public GameObject heavyHitSpark;
    public GameObject perfectBlockSpark;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 只负责特效和震屏，顿帧由 HitReactionRoutine 统一管理
    /// </summary>
    public void TriggerHitFeedback(Vector3 hitPoint, bool isHeavy, int chargeLevel = 0)
    {
        // 火花特效
        GameObject spark = (chargeLevel >= 2) ? heavyHitSpark : normalHitSpark;
        if (spark != null) Instantiate(spark, hitPoint, Quaternion.identity);

        // 震屏
        CameraController cam = Camera.main?.GetComponent<CameraController>();
        if (cam != null)
        {
            if (chargeLevel >= 2) cam.TriggerHeavySlashImpact(hitPoint);
            else if (chargeLevel == 1) cam.TriggerTier2ChargeShake();
            else cam.TriggerTier2ChargeShake();
        }
    }
}
