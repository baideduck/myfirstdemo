using System.Collections;
using UnityEngine;

/// <summary>
/// SlashState (KanPo) — 看破斩
/// </summary>
public class SlashState : AttackStateBase
{
    [Header("Timing")]
    public float hitWindowStart = 0.167f;
    public float hitWindowDuration = 0.25f;
    public float sheathTime = 2.167f;
    public float totalTime = 2.85f;

    [Header("Damage")]
    public int damage = 25;
    public bool isGuardBreak = true;

    [Header("Animation")]
    public string animName = "Kanpo";
    public string animBool = "isKanpo";

    protected override EnemyStates AttackType => EnemyStates.KanPo;

    protected override void SetupAnimation()
    {
        anim.SetLayerWeight(attackLayer, 1f);
        anim.SetBool(animBool, true);
        anim.Play(animName, attackLayer, 0f);
    }

    protected override void CleanupAnimation()
    {
        anim.SetBool(animBool, false);
    }

    protected override IEnumerator AttackRoutine => AttackRoutineImpl();

    IEnumerator AttackRoutineImpl()
    {
        float startTime = Time.time;
        float wait = Mathf.Max(0, hitWindowStart - (Time.time - startTime));
        if (wait > 0) yield return new WaitForSeconds(wait);
        if (combat.shouldAbortAttack) yield break;

        combat.SetAttackDamage(damage);
        combat.EnableWeaponHitBox(true, false);

        float hitEnd = startTime + hitWindowStart + hitWindowDuration;
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
        float waitToSheath = Mathf.Max(0, sheathTime - (Time.time - startTime));
        if (waitToSheath > 0) yield return new WaitForSeconds(waitToSheath);

        float remaining = Mathf.Max(0, totalTime - (Time.time - startTime));
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        anim.SetBool(animBool, false);
        yield return null;
    }
}
