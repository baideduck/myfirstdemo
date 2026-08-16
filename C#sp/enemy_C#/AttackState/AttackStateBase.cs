using System.Collections;
using UnityEngine;

/// <summary>
/// 所有攻击状态的公共基类 —— 消除 Enter/Exit 重复骨架
/// </summary>
public abstract class AttackStateBase : State<EnemyController>
{
    protected EnemyController controller;
    protected EnemyCombat   combat;
    protected BossEvents    events;
    protected BossDecisionEngine decisionEngine;
    protected Animator      anim;
    protected int           attackLayer;
    protected Coroutine     routine;
    protected bool          attackFinished;
    protected int           emptyAttackHash;   // 攻击层中转状态 Empty_Attack 的 hash（衔接检测排除用）

    // ── 子类必须提供 ──
    protected abstract EnemyStates  AttackType    { get; }
    protected abstract IEnumerator   AttackRoutine { get; }   // 改名避免和 Run() 冲突

    // ── 可选覆盖的动画钩子 ──
    protected virtual void SetupAnimation()    { }
    protected virtual void CleanupAnimation()  { }


    // ═══════════════════ Enter ═══════════════════
    public override void Enter(EnemyController owner)
    {
        controller      = owner;
        combat          = owner.GetComponent<EnemyCombat>();
        events          = owner.GetComponent<BossEvents>();
        decisionEngine  = owner.GetComponent<BossDecisionEngine>();
        anim            = owner.anim;
        attackFinished  = false;

        if (anim == null || combat == null)
        {
            Debug.LogError($"[{GetType().Name}] Enter FAILED — anim={anim != null} combat={combat != null}");
            owner.ChangeState(EnemyStates.Idle);
            return;
        }

        attackLayer = anim.GetLayerIndex("Attack Layer");
        if (attackLayer == -1) attackLayer = 0;
        emptyAttackHash = Animator.StringToHash("Empty_Attack");   // 缓存中转状态 hash（衔接检测排除用）

        combat.AttachWeaponToHand();

        SetupAnimation();
        events?.FireAttackStarted(AttackType);

        routine = owner.StartCoroutine(RunWrapper());
        combat.RegisterAttackRoutine(routine);
    }

    // ═══════════════════ Execute ═══════════════════
    public override void Execute()
    {
        if (controller == null) return;
        if (attackFinished)
        {
            combat.OnAttackFinished();
            return;
        }

        // 攻击协程被外部硬杀（StopCoroutine）→ 状态机必须复位，否则卡死在攻击状态、霸体永不解除
        if (routine != null && !combat.IsAttackRoutineActive)
        {
            attackFinished = true;
            combat.OnAttackFinished();
            return;
        }

        controller.FacePlayer();
    }

    // ═══════════════════ Exit ═══════════════════
    public override void Exit()
    {
        if (routine != null && controller != null)
            controller.StopCoroutine(routine);
        combat.RegisterAttackRoutine(null);
        combat.EnableWeaponHitBox(false, false);
        combat.ForceWeaponToSheath();

        CleanupAnimation();

        if (anim != null)
        {
            anim.speed = 1f;
            anim.applyRootMotion = false;
        }
    }

    /// <summary>
    /// 派生链衔接：攻击动画播到全局衔接点（BossComboChain.linkTransitionNormalized，默认 20%）即接下一招，
    /// 实现"连绵不断一招接一招"。不再等动画完整收招（那会导致每招之间"顿一下"）。
    /// 保留状态切走（Animator 自动退回 Empty_Attack）与超时兜底。
    /// </summary>
    protected IEnumerator WaitAttackAnimationEnd()
    {
        int attackAnimHash = anim.GetCurrentAnimatorStateInfo(attackLayer).shortNameHash;
        // ★ 衔接点从 BossComboChain 全局读取：Inspector 一处调，7 个攻击状态全部生效（默认 0.2 = 20%）
        float linkAt = combat != null ? combat.LinkTransitionNormalized : 0.9f;
        float maxWait = 8f;
        float elapsed = 0f;
        while (elapsed < maxWait)
        {
            AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(attackLayer);
            // 动画播到衔接点，或已切走（自动退回 Empty_Attack）→ 立即衔接下一招
            if (st.normalizedTime >= linkAt || st.shortNameHash != attackAnimHash) break;
            elapsed += Time.deltaTime;
            yield return null;
        }
        // ★ 不额外 yield null：立即结束 → 同帧走 OnAttackFinished → 链直接 Play 下一招，省 1 帧站姿等待
    }

    // 派生衔接点检测：攻击动画 normalizedTime 达到全局衔接点（BossComboChain.linkTransitionNormalized，默认 20%）
    // 供各攻击状态的伤害窗口循环调用——衔接点到了立即结束判定窗口（先到者），保证连招节奏不被长判定窗口卡住。
    // ★ 排除 Empty_Attack：攻击层非攻击时停在 Empty_Attack（10s Idle），若其 normalizedTime 已 ≥ 衔接点，
    //   攻击 Enter 后动画尚在过渡中会误判"已到衔接点"导致刚起手就跳招。
    protected bool AnimAtLinkPoint()
    {
        AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(attackLayer);
        if (st.shortNameHash == emptyAttackHash) return false;   // 还在 Empty_Attack（过渡/中转）→ 不衔接
        float linkAt = combat != null ? combat.LinkTransitionNormalized : 0.9f;
        return st.normalizedTime >= linkAt;
    }

    // ═══════════════════ 内部包装 ═══════════════════
    private IEnumerator RunWrapper()
    {
        yield return AttackRoutine;
        attackFinished = true;
        combat.OnAttackFinished();
    }
}
