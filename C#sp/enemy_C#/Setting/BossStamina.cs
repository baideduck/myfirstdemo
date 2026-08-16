using System.Collections;
using UnityEngine;

public class BossStamina : MonoBehaviour
{
    [Header("��������")]
    [SerializeField] private float maxStamina = 1000f;
    [SerializeField] private float currentStamina;

    [Header("交互倍率")]
    [SerializeField][Tooltip("玩家闪避/直接受伤")] private float directMultiplier = 1.0f;
    [SerializeField][Tooltip("普通格挡")] private float blockMultiplier = 1.2f;
    [SerializeField][Tooltip("完美格挡")] private float perfectBlockMultiplier = 1.5f;

    [Header("���ѻָ�����")]
    [SerializeField] private float recoverInterval = 0.12f;       // ÿ 0.12s �ָ�һ��
    [SerializeField] private float recoverAmount = 20f;            // ÿ�λָ� 20
    [SerializeField] private float negativePenaltyDelay = 0.5f;    // �����������ӳ� 0.5s
    [SerializeField] private float phaseTwoRecoverMin = 600f;      // ��2������ָ�Ŀ����Сֵ
    [SerializeField] private float phaseTwoRecoverMax = 1000f;     // ��2������ָ�Ŀ�������ֵ

    // 招式基础消耗（按威胁等级：Dodge=10，每级+5）
    private const float DODGE_BASE = 10f;
    private const float NORMAL_BASE = 15f;
    private const float THRUST_BASE = 20f;
    private const float CHARGE_BASE = 25f;
    private const float QUICK_BASE = 35f;
    private const float KANPO_BASE = 35f;
    private const float COMBO_BASE = 40f;
    private const float IAIS_BASE = 40f;

    private EnemyController enemyController;
    private BossPhaseManager phaseManager;
    private bool isExhausted = false;
    private bool wasNegativeAtExhaust = false;   // 力竭时体力是否为负数（透支）
    private Coroutine recoveryCoroutine = null;
    private float currentRecoveryTarget = 1000f;  // 当前恢复目标值
    private bool recoveryComplete = false;         // 恢复是否已经完成

    public bool IsExhausted { get => isExhausted; set => isExhausted = value; }
    public float CurrentStamina { get => currentStamina; set => currentStamina = value; }
    public float MaxStamina => maxStamina;
    public bool RecoveryComplete => recoveryComplete;
    public bool IsRecovering => recoveryCoroutine != null;

    /// <summary>
    /// 获取当前攻击招式的体力基础消耗值（也用于完美格挡扣架势）
    /// </summary>
    public float GetCurrentAttackBaseDrain()
    {
        return GetBaseDrainByCurrentState();
    }

    public enum StaminaDrainType
    {
        DirectHit,
        Dodge,
        Block,
        PerfectBlock
    }

    private void Awake()
    {
        enemyController = GetComponent<EnemyController>();
        phaseManager = GetComponent<BossPhaseManager>();
        currentStamina = maxStamina;
    }

    /// <summary>
    /// 根据当前 Boss 攻击状态 + 玩家应对方式计算体力消耗
    /// </summary>
    public void ConsumeStamina(StaminaDrainType type)
    {
        if (isExhausted) return;

        float baseDrain = GetBaseDrainByCurrentState();
        float multiplier = type switch
        {
            StaminaDrainType.DirectHit => directMultiplier,
            StaminaDrainType.Dodge => directMultiplier,
            StaminaDrainType.Block => blockMultiplier,
            StaminaDrainType.PerfectBlock => perfectBlockMultiplier,
            _ => 1.0f
        };

        currentStamina -= baseDrain * multiplier;

        if (currentStamina <= 0f)
        {
            // 记录是否透支（负值），然后钳到 0
            wasNegativeAtExhaust = currentStamina < 0f;
            currentStamina = 0f;
            OnStaminaEmpty();
        }
    }

    /// <summary>
    /// 按固定数值扣体力（狂暴打断代价专用）。
    /// 扣空后走标准力竭链路（归零 → OnStaminaEmpty → 力竭）。
    /// </summary>
    public void ConsumeStaminaFlat(float amount)
    {
        if (isExhausted || amount <= 0f) return;

        currentStamina -= amount;
        if (currentStamina <= 0f)
        {
            // 记录是否透支（负值），然后钳到 0
            wasNegativeAtExhaust = currentStamina < 0f;
            currentStamina = 0f;
            OnStaminaEmpty();
        }
    }

    /// <summary>
    /// 开始气绝恢复（由 ExhaustedState 进入时调用）
    /// </summary>
    public void StartRecovery()
    {
        if (recoveryCoroutine != null) return;
        recoveryComplete = false;

        // 判断当前阶段，设定恢复目标
        bool isPhaseTwo = phaseManager != null &&
            phaseManager.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;

        if (isPhaseTwo)
        {
            currentRecoveryTarget = Random.Range(phaseTwoRecoverMin, phaseTwoRecoverMax);
        }
        else
        {
            currentRecoveryTarget = maxStamina; // 1000
        }

        recoveryCoroutine = StartCoroutine(RecoveryRoutine());
    }

    private IEnumerator RecoveryRoutine()
    {
        // 透支惩罚：等待 0.5s 后才开始恢复
        if (wasNegativeAtExhaust)
        {
            yield return new WaitForSeconds(negativePenaltyDelay);
        }

        // 每 0.12s 恢复 20，直到达到目标
        while (currentStamina < currentRecoveryTarget)
        {
            yield return new WaitForSeconds(recoverInterval);
            currentStamina = Mathf.Min(currentStamina + recoverAmount, currentRecoveryTarget);
        }

        // 恢复完成
        recoveryComplete = true;
        recoveryCoroutine = null;

        // Phase 2 狂暴：起身后体力不再自动恢复，冻结在当前值
        if (phaseManager != null &&
            phaseManager.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst)
        {
            // 体力保持当前值，不再变化（直到下次清空重新掷随机数）
        }
    }

    /// <summary>
    /// 力竭结束后重置状态（ExhaustedState 退出时调用）
    /// </summary>
    public void ResetExhaustState()
    {
        isExhausted = false;
        wasNegativeAtExhaust = false;
        recoveryComplete = false;
        recoveryCoroutine = null;
    }

    private float GetBaseDrainByCurrentState()
    {
        if (enemyController == null || enemyController.StateMachine == null || enemyController.StateMachine.CurrentState == null)
            return DODGE_BASE;

        return enemyController.StateMachine.CurrentState switch
        {
            NormalSlashState => NORMAL_BASE,
            ThrustSlashState => THRUST_BASE,
            ChargeSlashState => CHARGE_BASE,
            QuickSlashState => QUICK_BASE,
            SlashState => KANPO_BASE,
            ComboState => COMBO_BASE,
            IaiSlashState => IAIS_BASE,
            _ => DODGE_BASE
        };
    }

    private void OnStaminaEmpty()
    {
        isExhausted = true;
        enemyController.OnStaminaDepleted();
    }

    public void SetRageMode(bool rage) { }
}