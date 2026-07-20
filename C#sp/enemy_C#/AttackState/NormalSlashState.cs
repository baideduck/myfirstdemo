using System.Collections;
using UnityEngine;

public class NormalSlashState : State<EnemyController>
{
    private EnemyController enemy;
    private bool attackFinished = false;
    private Coroutine routine;

    [Header("攻击参数")]
    public int damage = 25;
    public float sheathTime = 1.933f;              // 收刀时刻（秒）
    public float sheathSlowStartTime = 0.7f;       // 从这一秒开始慢放到收刀结束
    public float hitWindowStart = 0f;
    public float hitWindowDuration = 0.2f;

    public override void Enter(EnemyController owner)
    {
        if (owner == null) return;
        enemy = owner;
        attackFinished = false;

        if (enemy.anim == null)
        {
            enemy.ChangeState(EnemyStates.Idle);
            return;
        }

        // �� 1. ��ȷ����������
        enemy.AttachWeaponToHand();
        enemy.weaponLockedInHand = true;   // 首刀后刀锁在手上，不回鞘

        // 2. ����Э�̣�����������
        if (routine != null) enemy.StopCoroutine(routine);
        routine = enemy.StartCoroutine(SlashRoutine());
        enemy.RegisterAttackRoutine(routine);

        // 3. ��������
        enemy.anim.SetBool("isSlashing", true);
        enemy.FacePlayer();
    }

    IEnumerator SlashRoutine()
    {
        if (enemy.shouldAbortAttack) yield break;

        // �ȴ���������ײ�忪��ʱ��
        float timeToHit = Mathf.Max(0, hitWindowStart);
        yield return new WaitForSeconds(timeToHit);

        if (enemy.shouldAbortAttack) yield break;

        // ����������ײ��
        enemy.currentAttackDamage = damage;
        enemy.EnableWeaponHitBox(true, false);

        yield return new WaitForSeconds(hitWindowDuration);
        enemy.EnableWeaponHitBox(false, false);

        if (enemy.shouldAbortAttack) yield break;

        // 先等攻击帧结束，再等慢放起点
        float timeToSlowStart = Mathf.Max(0, sheathSlowStartTime - hitWindowStart - hitWindowDuration);
        if (timeToSlowStart > 0f)
            yield return new WaitForSeconds(timeToSlowStart);

        // 收刀阶段：统一正常速度 + 决策
        float timeToSheath = Mathf.Max(0, sheathTime - sheathSlowStartTime);
        var phaseMgr = enemy.GetComponent<BossPhaseManager>();
        bool isBurst = phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;

        if (isBurst)
            enemy.nextMoveAfterSheath = FindObjectOfType<BossDecisionEngine>()?.ForceDecide();

        yield return new WaitForSeconds(timeToSheath);

        if (enemy.shouldAbortAttack) yield break;
        enemy.anim.SetBool("isSlashing", false);
        yield return null;
        attackFinished = true;
    }

    public override void Execute()
    {
        if (enemy == null) return;
        enemy.FacePlayer();
        if (attackFinished) enemy.OnAttackFinished();
    }

    public override void Exit()
    {
        if (routine != null) StopCoroutine(routine);
        enemy.RegisterAttackRoutine(null);
        // �ر�������ײ��ǿ�ƹ���
        enemy.EnableWeaponHitBox(false, false);
        enemy.ForceWeaponToSheath();
    }
}