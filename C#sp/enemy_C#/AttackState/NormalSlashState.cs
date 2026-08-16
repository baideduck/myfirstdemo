using System.Collections;
using UnityEngine;

/// <summary>
/// NormalSlashState — 普通斩击
/// </summary>
public class NormalSlashState : AttackStateBase
{
    [Header("Timing")]
    public float hitWindowDuration = 0.2f;
    public float sheathTime = 1.933f;
    public float tailTime = 0.1f;

    [Header("Damage")]
    public int damage = 25;
    public bool isGuardBreak;

    [Header("Animation")]
    public string animBool = "isSlashing";

    protected override EnemyStates AttackType => EnemyStates.NormalSlash;

    protected override void SetupAnimation()
    {
        anim.SetLayerWeight(attackLayer, 1f);
        anim.SetBool(animBool, true);
    }

    protected override void CleanupAnimation()
    {
        anim.SetBool(animBool, false);
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
        if (combat.shouldAbortAttack) yield break;

        if (combat.isDerivedMove)
        {
            // ★ 要求：攻击动画完整播完 → 马上接下一招（不再 0.2s 快速收刀掐断动画）
            yield return WaitAttackAnimationEnd();
            yield break;   // 攻击结束，链队列立即接下一招
        }
        float wait = Mathf.Max(0, sheathTime - (Time.time - startTime));
        if (wait > 0) yield return new WaitForSeconds(wait);

        float remaining = Mathf.Max(0, tailTime);
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        anim.SetBool(animBool, false);
        yield return null;
    }
}
