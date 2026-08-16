using UnityEngine;

public class IdleState : State<EnemyController>
{
    private EnemyController enemy;

    [Header("完美弹刀后最小停留时间")]
    [SerializeField] private float minHoldAfterParry = 1.0f;
    private float enterTime;

    [Header("��������ٶ�")]
    [SerializeField] private float facePlayerSpeed = 5f;

    public override void Enter(EnemyController owner)
    {
        enemy = owner;
        enterTime = Time.time;

        // 重置决策计时器，防止攻击期间的累积时间导致切回 Idle 立即决策
        BossDecisionEngine de = enemy.GetComponent<BossDecisionEngine>();
        if (de != null) de.ResetTimer();

        // ���������־
        enemy.isParryAnimating = false;
        enemy.shouldAbortAttack = false;

        // ֹͣ���в���Э��
        enemy.StopAllCoroutines();

        // �ر�������ײ��
        enemy.EnableWeaponHitBox(false, false);

        // ���Ŵ�������
        enemy.anim.Play("Idle", 0, 0f);
        enemy.anim.Update(0f);  // 立即执行，跳过过渡

        // ֻ����δ�����ֵ���ʱ�Ž���������
        if (!enemy.lockWeaponInHand)
            enemy.ForceWeaponToSheath();
    }

    public override void Execute()
    {
        if (enemy == null) return;

        // ���������ڼ���ȫ����
        if (enemy.isParryAnimating) return;

        // �����׶ζ���
        if (enemy.isExecutionFrozen) return;

        // ������ң������κ�λ�ƣ�λ���� DodgeState ͳһ������
        if (!enemy.lockFacing)
            enemy.FacePlayer();
    }

    public override void Exit()
    {
        // ForceWeaponToSheath �ڲ����� lockWeaponInHand ��飬
        // ����δ�������������ʣ���סʱ����ִ�У������빥��״̬��ͻ��
        enemy.ForceWeaponToSheath();
    }
}