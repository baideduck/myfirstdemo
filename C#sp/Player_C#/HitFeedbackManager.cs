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

        // 震屏：按蓄力等级三级分派（MHW大剑手感）
        CameraController cam = Camera.main?.GetComponent<CameraController>();
        if (cam != null)
        {
            if (chargeLevel >= 2)
            {
                cam.TriggerHeavySlashImpact(hitPoint);     // 真蓄：重震
                cam.TriggerImpactFOV(5f, 0.3f);            // FOV收缩5°
            }
            else if (chargeLevel == 2)
            {
                cam.TriggerTier2ChargeShake();             // 二蓄：中震
                cam.TriggerImpactFOV(3f, 0.2f);            // FOV收缩3°
            }
            else
            {
                cam.TriggerTier1ChargeShake();             // 一蓄/普攻：轻震
                cam.TriggerImpactFOV(1.5f, 0.15f);         // FOV收缩1.5°
            }
        }
    }
}
