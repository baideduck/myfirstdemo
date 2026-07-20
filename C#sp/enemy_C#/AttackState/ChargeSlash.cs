using System.Collections;
using UnityEngine;

public class ChargeSlashState : State<EnemyController>
{
    private EnemyController enemy;
    private bool attackFinished = false;
    private Coroutine routine;

    [Header("����նʱ���ᣨ195֡��60fps��")]
    public float sheathTime = 2.583f;            // 收刀时刻：155 / 60
    public float hitWindowStart = 0f;        // 伤害窗口开始：第10帧
    public float hitWindowDuration = 0.333f;     // 伤害窗口持续：20帧（10→30）

    [Header("����ͣ�٣������д��ڿ���ʱͣ�٣�")]
    public float chargePauseDuration = 0.15f;     // 在第五帧暂停的时间（可在Inspector微调）

    [Header("�˺�")]
    public int damageFirst = 15;                  // ��һ���˺�
    public int damageSecond = 40;                 // �ڶ����˺��������Ҫ�����ٿ�һ�δ��ڣ�������ֻ��һ�δ����

    [Header("��ʽ����")]
    public bool canBeBlocked = true;
    public bool isGuardBreak = false;
    public bool hasSuperArmor = false;

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

        if (routine != null) enemy.StopCoroutine(routine);
        enemy.AttachWeaponToHand();
        enemy.anim.SetBool("isChargeSlash", true);

        routine = enemy.StartCoroutine(ChargeSlashRoutine());
        enemy.RegisterAttackRoutine(routine);   // ע�ᵽ������

        // ֹͣ�ƶ����������
        enemy.FacePlayer();
    }

    IEnumerator ChargeSlashRoutine()
    {
        float animStartTime = Time.time;

        // 1. �ȴ��������д��ڿ���ʱ������5֡��
        float timeToHit = Mathf.Max(0, hitWindowStart - (Time.time - animStartTime));
        yield return new WaitForSeconds(timeToHit);
        float pauseElapsed = 0f;
        while (pauseElapsed < chargePauseDuration)
        {
            float t = pauseElapsed / chargePauseDuration;
            // ʹ��һ�����ߣ���0���ٽ���Ȼ������
            float curve = Mathf.Sin(t * Mathf.PI * 0.5f); // ʾ��
            enemy.anim.speed = Mathf.Lerp(0f, 1f, curve);
            pauseElapsed += Time.deltaTime;
            yield return null;
        }
        enemy.anim.speed = 1f;

        // 3. ����������ײ�壨��һ���˺���
        enemy.currentAttackDamage = damageFirst;
        enemy.EnableWeaponHitBox(true, false);

        // 4. ������ײ�忪�� hitWindowDuration ��
        yield return new WaitForSeconds(hitWindowDuration);

        // 5. �ر���ײ�壨�����п��ܻ�����ŷ����ڶ����˺�����û�У������Թ���
        enemy.EnableWeaponHitBox(false, false);

        // 6. �����Ϊ�����˺������������ٴο��������Ӧ����֡��Ŀǰ�Ե���Ϊ����

        // 收刀阶段：统一正常速度 + 决策
        float timeToSheath = Mathf.Max(0, sheathTime - hitWindowStart - hitWindowDuration);
        var phaseMgr = enemy.GetComponent<BossPhaseManager>();
        bool isBurst = phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;

        if (isBurst)
            enemy.nextMoveAfterSheath = FindObjectOfType<BossDecisionEngine>()?.ForceDecide();

        yield return new WaitForSeconds(timeToSheath);
        if (enemy.shouldAbortAttack) yield break;

        // 9. �ȴ�����β����195-155=40֡��0.667�룩
        float tailTime = (195f / 60f) - sheathTime;
        if (tailTime > 0) yield return new WaitForSeconds(tailTime);
        if (enemy.shouldAbortAttack) yield break;

        enemy.anim.SetBool("isChargeSlash", false);
        yield return null;
        attackFinished = true;
    }

    public override void Execute()
    {
        if (enemy == null) return;
        enemy.FacePlayer();

        if (attackFinished)
            enemy.OnAttackFinished();
    }

    public override void Exit()
    {
        if (routine != null) enemy.StopCoroutine(routine);
        enemy.RegisterAttackRoutine(null);      // ע��
        // ȷ���뿪ʱ�����ٶȻָ�����
        if (enemy != null && enemy.anim != null)
            enemy.anim.speed = 1f;
        enemy.EnableWeaponHitBox(false, false);
    }
}