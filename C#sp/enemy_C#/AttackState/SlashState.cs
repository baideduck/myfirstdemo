using System.Collections;
using UnityEngine;

/// <summary>
/// ���ƣ�Kanpo������������ͨ�񵲣�ֻ�������񵲿��ơ�
/// ע�⣺�ļ����������� SlashState����ʵ�ʶ�Ӧ Kanpo ��ʽ��
/// </summary>
public class SlashState : State<EnemyController>
{
    private EnemyController enemy;
    private bool attackFinished = false;
    private Coroutine routine;

    [Header("Kanpo time params (60fps, 171 frames total)")]
    public float sheathTime = 2.167f;           // 收刀：130/60
    public float hitWindowStart = 0.167f;       // 开：10/60
    public float hitWindowDuration = 0.25f;     // 15帧：25-10/60
    public int damage = 25;                     // �����˺�

    [Header("��ʽ����")]
    public bool canBeBlocked = true;
    public bool isGuardBreak = true;            // �Ʒ���������ͨ��
    public bool hasSuperArmor = false;

    public override void Enter(EnemyController owner)
    {
        if (owner == null) return;
        enemy = owner;
        attackFinished = false;
        enemy.AttachWeaponToHand();

        if (enemy.anim == null)
        {
            enemy.ChangeState(EnemyStates.Idle);
            return;
        }

        if (routine != null) enemy.StopCoroutine(routine);
        enemy.AttachWeaponToHand();
        enemy.anim.SetBool("isKanpo", true);

        // 跟 ThrustSlash 一致：在 Enter 内设权重 + 强制播放，跳过过渡
        int attackLayer = enemy.anim.GetLayerIndex("Attack Layer");
        if (attackLayer == -1) attackLayer = 0;
        enemy.anim.SetLayerWeight(attackLayer, 1f);
        enemy.anim.Play("Kanpo", attackLayer, 0f);

        routine = enemy.StartCoroutine(KanpoRoutine());
        enemy.RegisterAttackRoutine(routine);

        // 1. ֹͣ�ƶ����������
        enemy.FacePlayer();
    }

    IEnumerator KanpoRoutine()
    {
        float animStartTime = Time.time;
        enemy.anim.Update(0f);

        // �ȴ����д���
        float timeToHit = Mathf.Max(0, hitWindowStart - (Time.time - animStartTime));
        yield return new WaitForSeconds(timeToHit);

        // �����˺���������ײ��
        enemy.currentAttackDamage = damage;
        enemy.EnableWeaponHitBox(true, false);

        // ���ּ��̵Ĵ���
        yield return new WaitForSeconds(hitWindowDuration);

        // �ر���ײ��
        enemy.EnableWeaponHitBox(false, false);

        // 收刀阶段：统一正常速度 + 决策
        float timeToSheath = Mathf.Max(0, sheathTime - hitWindowStart - hitWindowDuration);
        var phaseMgr = enemy.GetComponent<BossPhaseManager>();
        bool isBurst = phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;

        if (isBurst)
            enemy.nextMoveAfterSheath = FindObjectOfType<BossDecisionEngine>()?.ForceDecide();

        yield return new WaitForSeconds(timeToSheath);

        // �ȴ�����β��
        float tailTime = (171f / 60f) - sheathTime;
        if (tailTime > 0) yield return new WaitForSeconds(tailTime);
        enemy.anim.SetBool("isKanpo", false);
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
        enemy.EnableWeaponHitBox(false, false);
        enemy.ForceWeaponToSheath();
    }
}