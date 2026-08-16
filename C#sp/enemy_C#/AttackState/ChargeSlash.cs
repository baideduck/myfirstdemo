using System.Collections;
using UnityEngine;

/// <summary>
/// ChargeSlashState — 蓄力斩（两段蓄力）
/// </summary>
public class ChargeSlashState : AttackStateBase
{
    [Header("Timing")]
    public float totalAnimTime = 3.25f;
    public float chargePauseStart = 0f;
    public float chargePauseDuration = 0.15f;
    public float hitWindowStart = 0.15f;
    public float hitWindowDuration = 0.333f;
    public float sheathTime = 2.583f;

    [Header("Damage")]
    public int damage = 40;
    public bool isGuardBreak;

    [Header("Animation")]
    public string animName = "ChargeSlash";
    public string animBool = "isChargeSlash";

    protected override EnemyStates AttackType => EnemyStates.ChargeSlash;

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
        yield return StartCoroutine(WaitToPause(startTime));
        if (combat.shouldAbortAttack) yield break;
        yield return StartCoroutine(ChargePause());
        if (combat.shouldAbortAttack) yield break;
        yield return StartCoroutine(HitWindow());
        if (combat.shouldAbortAttack) yield break;
        if (combat.isDerivedMove)
        {
            // ★ 要求：攻击动画完整播完 → 马上接下一招（不再 0.2s 快速收刀掐断动画）
            yield return WaitAttackAnimationEnd();
            yield break;   // 攻击结束，链队列立即接下一招
        }
        yield return StartCoroutine(SheathPhase(startTime));
        if (combat.shouldAbortAttack) yield break;

        float elapsed = Time.time - startTime;
        float remaining = Mathf.Max(0, totalAnimTime - elapsed);
        if (remaining > 0) yield return new WaitForSeconds(remaining);
        if (combat.shouldAbortAttack) yield break;

        anim.SetBool(animBool, false);
        yield return null;
    }

    IEnumerator WaitToPause(float startTime)
    {
        float wait = Mathf.Max(0, chargePauseStart - (Time.time - startTime));
        if (wait > 0) yield return new WaitForSeconds(wait);
    }

    IEnumerator ChargePause()
    {
        float elapsed = 0f;
        while (elapsed < chargePauseDuration)
        {
            if (combat.shouldAbortAttack) yield break;
            float t = elapsed / chargePauseDuration;
            anim.speed = Mathf.Lerp(0f, 1f, 1f - (1f - t) * (1f - t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        anim.speed = 1f;
    }

    IEnumerator HitWindow()
    {
        combat.SetAttackDamage(damage);
        combat.EnableWeaponHitBox(true, false);
        float elapsed = 0f;
        while (elapsed < hitWindowDuration)
        {
            if (combat.shouldAbortAttack) { combat.EnableWeaponHitBox(false, false); yield break; }
            // ★ 派生模式：动画到衔接点立即结束判定（先到者，保证连招节奏不被长判定窗口卡住）
            if (combat.isDerivedMove && AnimAtLinkPoint())
            {
                combat.EnableWeaponHitBox(false, false);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        combat.EnableWeaponHitBox(false, false);
    }

    IEnumerator SheathPhase(float startTime)
    {
        float wait = Mathf.Max(0, sheathTime - (Time.time - startTime));
        if (wait > 0) yield return new WaitForSeconds(wait);
        var phaseMgr = controller.GetComponent<BossPhaseManager>();
        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst && decisionEngine != null)
            controller.nextMoveAfterSheath = decisionEngine.ForceDecide();
    }
}
