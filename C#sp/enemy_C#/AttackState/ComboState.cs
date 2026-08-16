using System.Collections;
using UnityEngine;

/// <summary>
/// ComboState — 连斩
/// </summary>
public class ComboState : AttackStateBase
{
    [Header("Timing")]
    public float hitWindowDuration = 4.25f;
    public float sheathTime = 4.25f;
    public float totalTime = 5.333f;

    [Header("Damage")]
    public int damage = 30;
    public bool isGuardBreak;

    [Header("Animation")]
    public string animBool = "isCombo";

    private Vector3 startPosition;

    protected override EnemyStates AttackType => EnemyStates.Combo;

    protected override void SetupAnimation()
    {
        anim.applyRootMotion = true;
        anim.SetBool(animBool, true);
    }

    protected override void CleanupAnimation()
    {
        anim.applyRootMotion = false;
        anim.SetBool(animBool, false);
    }

    public override void Enter(EnemyController owner)
    {
        startPosition = owner.transform.position;
        base.Enter(owner);
    }

    protected override IEnumerator AttackRoutine => AttackRoutineImpl();

    IEnumerator AttackRoutineImpl()
    {
        float startTime = Time.time;
        combat.SetAttackDamage(damage);
        combat.EnableWeaponHitBox(true, false);

        float hitEnd = startTime + hitWindowDuration;
        while (Time.time < hitEnd)
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

        float elapsed = Time.time - startTime;
        if (combat.isDerivedMove)
        {
            // ★ 要求：攻击动画完整播完 → 马上接下一招（不再 0.2s 快速收刀掐断动画）
            yield return WaitAttackAnimationEnd();
            yield break;   // 攻击结束，链队列立即接下一招
        }
        float preSheathWait = Mathf.Max(0, sheathTime - elapsed);
        if (preSheathWait > 0)
        {
            while (preSheathWait > 0 && !combat.shouldAbortAttack) { preSheathWait -= Time.deltaTime; yield return null; }
        }
        if (combat.shouldAbortAttack) yield break;

        var phaseMgr = controller.GetComponent<BossPhaseManager>();
        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst && decisionEngine != null)
            controller.nextMoveAfterSheath = decisionEngine.ForceDecide();

        elapsed = Time.time - startTime;
        float remaining = Mathf.Max(0, totalTime - elapsed);
        if (remaining > 0)
        {
            while (remaining > 0 && !combat.shouldAbortAttack) { remaining -= Time.deltaTime; yield return null; }
        }
        if (combat.shouldAbortAttack) yield break;

        anim.SetBool(animBool, false);
        anim.applyRootMotion = false;
        controller.transform.position = startPosition;
        combat.AttachWeaponToSheath();
        yield return null;
    }
}
