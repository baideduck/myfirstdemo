using UnityEngine;

public class BossPosture : MonoBehaviour
{
    [Header("����ֵ")]
    [SerializeField] private float maxPosture = 400f;
    [SerializeField] private float currentPosture;

    [Header("����")]
    [SerializeField] private float posturePerHit = 20f;         // ÿ�α���ҹ�����������

    [Header("硬直节拍（跨档小硬直）")]
    [SerializeField] private float[] flinchThresholds = { 100f, 200f, 300f };  // 累计削减跨过这些值 → 触发小硬直
    [SerializeField] private float flinchLockDuration = 2.5f;                  // 跨档后锁定：削减照常但不触发新档（防连续硬直锁死）
    [SerializeField] private float flinchHitStop = 0.12f;                      // 小硬直顿帧

    private int flinchIndex = 0;
    private float flinchLockUntil = 0f;

    private EnemyController enemyController;
    private bool exhaustTriggered = false;   // 力竭防重入：架势清零只触发一次力竭，防止力竭中被击反复重播力竭动画

    public float CurrentPosture { get => currentPosture; set => currentPosture = value; }
    public float MaxPosture => maxPosture;

    private void Awake()
    {
        enemyController = GetComponent<EnemyController>();
        currentPosture = maxPosture;
    }

    /// <summary>
    /// ����ҹ�������ʱ����
    /// </summary>
    public void OnPlayerHit()
    {
        ApplyPostureDamage(posturePerHit);
    }

    /// <summary>
    /// 完美格挡时扣除架势（扣量 = 该招式的体力基础消耗）
    /// </summary>
    public void OnPerfectBlocked(float drainAmount)
    {
        ApplyPostureDamage(drainAmount);
    }

    /// <summary>
    /// 统一架势结算：削减 + 硬直节拍跨档检测 + 力竭检查
    /// </summary>
    private void ApplyPostureDamage(float amount)
    {
        if (IsInPhaseTransition()) return;   // 二阶段转场中不结算架势，防止力竭打断转场
        currentPosture -= amount;

        // ★ 硬直节拍：累计削减跨过阈值 → Boss 小硬直（玩家持续输出赢取的小窗口）
        //   跨档后锁定 flinchLockDuration：期间削减照常但不再触发新档（防连续硬直锁死 Boss）
        //   排除：力竭中（currentPosture<=0）与完美格挡弹刀后摇（IsInBreakRecovery，防二次打断破坏演出）
        if (Time.time >= flinchLockUntil && flinchIndex < flinchThresholds.Length &&
            currentPosture > 0f && !IsInBreakRecovery() &&
            currentPosture <= maxPosture - flinchThresholds[flinchIndex])
        {
            flinchIndex++;
            if (enemyController != null) enemyController.combat.PlayFlinch(flinchHitStop);
            flinchLockUntil = Time.time + flinchLockDuration;
        }

        CheckBreak();
    }

    // 完美格挡弹刀后摇中不触发跨档硬直（BreakChain 正在播 Hit_Large_F，二次打断会覆盖演出）
    private bool IsInBreakRecovery()
    {
        BossComboChain cc = GetComponent<BossComboChain>();
        return cc != null && cc.IsInBreakRecovery;
    }

    // 二阶段转场期间（Block_Hit→Roll→隐藏→出现）免疫架势结算
    private bool IsInPhaseTransition()
    {
        BossPhaseManager pm = GetComponent<BossPhaseManager>();
        return pm != null && pm.IsInPhaseTransition;
    }

    private void CheckBreak()
    {
        if (currentPosture <= 0f)
        {
            currentPosture = 0f;
            if (!exhaustTriggered)
            {
                exhaustTriggered = true;   // 只触发一次力竭，力竭中再受击不再重复触发
                enemyController.OnPostureBroken();
            }
        }
    }

    /// <summary>
    /// ���߽������������� 
    /// </summary>
    public void ResetPosture()
    {
        currentPosture = maxPosture;
        exhaustTriggered = false;   // 起身后允许再次触发力竭
        flinchIndex = 0;            // 硬直节拍重置：重新从第一档开始
        flinchLockUntil = 0f;
    }
}