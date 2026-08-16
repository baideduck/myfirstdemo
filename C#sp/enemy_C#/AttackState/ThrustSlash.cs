using System.Collections;
using UnityEngine;

/// <summary>
/// ThrustSlashState — 开场突刺
/// </summary>
public class ThrustSlashState : AttackStateBase
{
    // ═══════════════════ 参数 ═══════════════════
    [Header("Timing")]
    public float dashDelay = 0.1f;
    public float dashDuration = 0.3f;
    public float hitWindowDuration = 0.2f;
    public float sheathTime = 1.2f;
    public float totalTime = 1.5f;

    [Header("Movement")]
    public float dashDistance = 10f;
    public float stopDistance = 1f;
    public AnimationCurve dashCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Damage")]
    public int damage = 35;
    public bool isGuardBreak;

    [Header("Animation")]
    public string animName = "ThrustSlash";
    public string animBool = "isThrust";

    // ═══════════════════ 基类实现 ═══════════════════
    protected override EnemyStates AttackType => EnemyStates.ThrustSlash;

    protected override void SetupAnimation()
    {
        anim.SetLayerWeight(attackLayer, 1f);
        anim.SetBool(animBool, true);
        // Inspector 中 animName 可能被错误配置（如 "Thrust"），这里强制使用 Animator 中真实存在的状态名
        anim.Play("ThrustSlash", attackLayer, 0f);
    }

    protected override void CleanupAnimation()
    {
        anim.SetBool(animBool, false);
    }

    protected override IEnumerator AttackRoutine => AttackRoutineImpl();

    IEnumerator AttackRoutineImpl()
    {
        yield return null;
        float checkStart = Time.time;
        // 强制使用 Animator 中真实存在的状态名（Inspector 里的 animName 可能配置错误）
        const string REAL_ANIM = "ThrustSlash";
        while (!anim.GetCurrentAnimatorStateInfo(attackLayer).IsName(REAL_ANIM))
        {
            if (Time.time - checkStart > 0.2f)
            {
                Debug.LogWarning($"[ThrustSlash] Animation '{REAL_ANIM}' not found, aborting");
                yield break;
            }
            yield return null;
        }

        float startTime = Time.time;
        if (combat.shouldAbortAttack) yield break;

        // ── 冲刺 ──
        yield return new WaitForSeconds(dashDelay);
        if (combat.shouldAbortAttack) yield break;
        yield return StartCoroutine(Dash());
        if (combat.shouldAbortAttack) yield break;

        // ── 命中判定 ──
        yield return StartCoroutine(HitWindow());
        if (combat.shouldAbortAttack) yield break;

        // ── 收刀 ──
        if (combat.isDerivedMove)
        {
            // ★ 要求：攻击动画完整播完 → 马上接下一招（不再 0.2s 快速收刀掐断动画）
            yield return WaitAttackAnimationEnd();
            yield break;   // 攻击结束，链队列立即接下一招
        }
        yield return StartCoroutine(SheathPhase(startTime));
        if (combat.shouldAbortAttack) yield break;

        // ── 动画尾段 ──
        float elapsed = Time.time - startTime;
        float remaining = Mathf.Max(0, totalTime - elapsed);
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        anim.SetBool(animBool, false);
        yield return null;
    }

    // ═══════════════════ 冲刺 ═══════════════════
    IEnumerator Dash()
    {
        // 1. 加速动画
        anim.speed = 2f;

        Vector3 startPos = controller.transform.position;
        Vector3 playerPos = controller.GetPlayerPosition();
        playerPos.y = startPos.y;

        // 2. 计算朝向，处理零向量的情况
        Vector3 dirToPlayer = (playerPos - startPos).normalized;
        if (dirToPlayer.sqrMagnitude < 0.0001f)
            dirToPlayer = controller.transform.forward;

        controller.transform.rotation = Quaternion.LookRotation(dirToPlayer);

        float distToPlayer = Vector3.Distance(startPos, playerPos);

        // 3. 如果已经足够近，无需冲刺，直接结束
        if (distToPlayer <= stopDistance)
        {
            anim.speed = 1f;
            yield break;
        }

        // 4. 计算正确的目标位置：停在玩家前方 stopDistance 处
        Vector3 idealTarget = playerPos - dirToPlayer * stopDistance;
        float idealDist = Vector3.Distance(startPos, idealTarget);

        Vector3 targetPos;
        if (idealDist <= dashDistance)
        {
            targetPos = idealTarget;
        }
        else
        {
            // 超过最大冲刺距离，则只冲 dashDistance 远
            targetPos = startPos + dirToPlayer * dashDistance;
        }

        // 5. 使用 CharacterController.Move 安全移动，避免破坏碰撞状态
        CharacterController cc = controller.GetComponent<CharacterController>();
        float elapsed = 0f;

        while (elapsed < dashDuration)
        {
            if (combat.shouldAbortAttack)
            {
                anim.speed = 1f;
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dashDuration);
            Vector3 desiredPos = Vector3.Lerp(startPos, targetPos, dashCurve.Evaluate(t));
            Vector3 moveDelta = desiredPos - controller.transform.position;

            if (cc != null)
                cc.Move(moveDelta);
            else
                controller.transform.position = desiredPos;

            // 提前终止：已进入玩家 stopDistance 范围内
            if (Vector3.Distance(controller.transform.position, playerPos) <= stopDistance)
                break;

            yield return null;
        }

        // 6. 恢复动画速度
        anim.speed = 1f;
    }

    // ═══════════════════ 命中判定 ═══════════════════
    IEnumerator HitWindow()
    {
        combat.SetAttackDamage(damage);
        combat.AttachWeaponToHand();
        combat.EnableWeaponHitBox(true, false);

        float endTime = Time.time + hitWindowDuration;
        while (Time.time < endTime)
        {
            if (combat.shouldAbortAttack) { combat.EnableWeaponHitBox(false, false); yield break; }
            // ★ 派生模式：动画到衔接点立即结束判定（先到者，保证连招节奏不被长判定窗口卡住）
            if (combat.isDerivedMove && AnimAtLinkPoint())
            {
                combat.EnableWeaponHitBox(false, false);
                yield break;
            }
            yield return null;
        }
        combat.EnableWeaponHitBox(false, false);
    }

    // ═══════════════════ 收刀 ═══════════════════
    IEnumerator SheathPhase(float startTime)
    {
        float elapsed = Time.time - startTime;
        float wait = Mathf.Max(0, sheathTime - elapsed);
        if (wait > 0) yield return new WaitForSeconds(wait);

        var phaseMgr = controller.GetComponent<BossPhaseManager>();
        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst && decisionEngine != null)
            controller.nextMoveAfterSheath = decisionEngine.ForceDecide();
    }
}
