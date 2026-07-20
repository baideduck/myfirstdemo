using UnityEngine;

public class WaitState : State<EnemyController>
{
    private EnemyController enemy;

    public override void Enter(EnemyController owner)
    {
        enemy = owner;

        // 确保 Attack Layer 初始权重为 0，不会被它覆盖 Base Layer
        enemy.DisableAttackLayer();

        // 强制武器归鞘
        if (enemy.weaponModel != null)
        {
            enemy.AttachWeaponToSheath();
            enemy.weaponModel.SetActive(true);
        }
    }
    public override void Execute()
    {
        if (enemy == null) return;
        enemy.FacePlayer();        // ֻ������ң����߶�
    }

    public override void Exit()
    {
    }
}