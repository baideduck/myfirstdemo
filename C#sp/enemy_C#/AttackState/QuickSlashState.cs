using System.Collections;
using UnityEngine;

/// <summary>
/// QuickSlashState — 速斩/闪现斩
/// </summary>
public class QuickSlashState : AttackStateBase
{
    [Header("Timing")]
    public float slowDuration = 0.5f;
    public float slowSpeed = 0.2f;
    public float hitWindowDuration = 1.25f;
    public float sheathTime = 3.117f;
    public float totalTime = 3.817f;

    [Header("Damage")]
    public int damage = 18;
    public bool isGuardBreak;

    [Header("Teleport")]
    public float teleportDistance = 1.5f;

    [Header("Animation")]
    public string animBool = "isQuick";

    protected override EnemyStates AttackType => EnemyStates.QuickSlash;

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

        // 起手慢放
        float slowEnd = startTime + slowDuration;
        while (Time.time < slowEnd)
        {
            if (combat.shouldAbortAttack) yield break;
            anim.speed = slowSpeed;
            yield return null;
        }
        anim.speed = 1f;
        if (combat.shouldAbortAttack) yield break;

        // 闪现到玩家前方
        Vector3 playerPos = controller.GetPlayerPosition();
        Vector3 dir = (playerPos - controller.transform.position).normalized;
        dir.y = 0;
        if (dir.magnitude > 0.01f)
        {
            controller.transform.position = playerPos - dir * teleportDistance;
            controller.transform.rotation = Quaternion.LookRotation(dir);
        }
        if (combat.shouldAbortAttack) yield break;

        // 伤害窗口
        combat.SetAttackDamage(damage);
        combat.EnableWeaponHitBox(true, false);
        float hitEnd = Time.time + hitWindowDuration;
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

        // 收刀
        if (combat.isDerivedMove)
        {
            // ★ 要求：攻击动画完整播完 → 马上接下一招（不再 0.2s 快速收刀掐断动画）
            yield return WaitAttackAnimationEnd();
            yield break;   // 攻击结束，链队列立即接下一招
        }
        float wait = Mathf.Max(0, sheathTime - (Time.time - startTime));
        if (wait > 0) yield return new WaitForSeconds(wait);

        float remaining = Mathf.Max(0, totalTime - (Time.time - startTime));
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        anim.SetBool(animBool, false);
        yield return null;
    }
}
