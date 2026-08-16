using System.Collections;
using UnityEngine;

/// <summary>
/// IaiSlashState — 居合斩
/// </summary>
public class IaiSlashState : AttackStateBase
{
    [Header("Timing")]
    public float pauseStartTime = 0.083f;
    public float pauseDuration = 0.8f;
    public float hitWindowDuration = 0.5f;
    public float slowMoScale = 0.2f;
    public float sheathTime = 1.0f;
    public float tailDuration = 0.167f;

    [Header("Damage")]
    public int damage = 62;
    public bool isGuardBreak = true;

    [Header("Teleport")]
    public float teleportDistance = 1f;

    [Header("Animation")]
    public string animBool = "isIai";

    protected override EnemyStates AttackType => EnemyStates.IaiSlash;

    protected override void SetupAnimation()
    {
        anim.SetLayerWeight(attackLayer, 1f);
        anim.SetBool(animBool, true);
        // ★ 狂暴打断支持：攻击中任意阶段硬切（isIai 过渡只挂在 Empty_Attack，攻击中只 SetBool 不会切状态；
        //   同 ThrustSlashState 模式直接 Play，正常流程从 Empty_Attack 进入时同样生效）
        anim.Play("Iai", attackLayer, 0f);
    }

    protected override void CleanupAnimation()
    {
        anim.SetBool(animBool, false);
    }

    protected override IEnumerator AttackRoutine => AttackRoutineImpl();

    IEnumerator AttackRoutineImpl()
    {
        float startTime = Time.time;

        // 暂停前等待
        float wait = Mathf.Max(0, pauseStartTime - (Time.time - startTime));
        if (wait > 0) yield return new WaitForSeconds(wait);
        if (combat.shouldAbortAttack) yield break;

        // 起手暂停
        anim.speed = 0.1f;
        yield return new WaitForSeconds(pauseDuration);
        anim.speed = 1f;
        if (combat.shouldAbortAttack) yield break;

        // 闪现
        Vector3 playerPos = controller.GetPlayerPosition();
        Vector3 dir = (playerPos - controller.transform.position).normalized;
        dir.y = 0;
        if (dir.magnitude > 0.01f)
        {
            controller.transform.position = playerPos - dir * teleportDistance;
            controller.transform.rotation = Quaternion.LookRotation(dir);
        }
        if (combat.shouldAbortAttack) yield break;

        // 伤害窗口（慢放）
        anim.speed = slowMoScale;
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
        anim.speed = 1f;
        if (combat.shouldAbortAttack) yield break;

        // 收刀
        if (combat.isDerivedMove)
        {
            // ★ 要求：攻击动画完整播完 → 马上接下一招（不再 0.2s 快速收刀掐断动画）
            yield return WaitAttackAnimationEnd();
            yield break;   // 攻击结束，链队列立即接下一招
        }
        float sheathWait = Mathf.Max(0, sheathTime - (Time.time - startTime));
        if (sheathWait > 0) yield return new WaitForSeconds(sheathWait);
        if (combat.shouldAbortAttack) yield break;

        // 尾段
        if (tailDuration > 0) yield return new WaitForSeconds(tailDuration);

        anim.SetBool(animBool, false);
        yield return null;
    }
}
