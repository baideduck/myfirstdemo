using System.Collections;
using UnityEngine;

public class QuickSlashState : State<EnemyController>
{
    private EnemyController enemy;
    private bool attackFinished = false;
    private Coroutine routine;

    [Header("����ʱ����")]
    public float sheathTime = 3.117f;
    public float sheathSlowStartTime = 1.5f;     // 慢放起点（Inspector设）
    public float hitWindowStart = 0f;
    public float hitWindowDuration = 1.25f;
    public int damage = 18;

    [Header("��������")]
    public float slowSpeed = 0.2f;
    public float slowDuration = 0.5f;
    public float teleportDistance = 1.5f;

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

        enemy.AttachWeaponToHand();
        if (routine != null) enemy.StopCoroutine(routine);
        routine = enemy.StartCoroutine(QuickSlashRoutine());
        enemy.RegisterAttackRoutine(routine);

        enemy.anim.SetBool("isQuick", true);
        enemy.FacePlayer();
    }

    IEnumerator QuickSlashRoutine()
    {
        // ����
        enemy.anim.speed = slowSpeed;
        yield return new WaitForSeconds(slowDuration);

        // 闪现（临时关 Root Motion 防覆盖）
        bool wasRootMotion = enemy.anim.applyRootMotion;
        enemy.anim.applyRootMotion = false;
        enemy.anim.speed = 1f;
        Vector3 playerPos = enemy.GetPlayerPosition();
        Vector3 dirToPlayer = (playerPos - enemy.transform.position).normalized;
        dirToPlayer.y = 0;
        Vector3 teleportPos = playerPos - dirToPlayer * teleportDistance;
        teleportPos.y = enemy.transform.position.y;
        enemy.transform.position = teleportPos;
        enemy.FaceTarget(playerPos);
        enemy.anim.applyRootMotion = wasRootMotion;

        float animStartTime = Time.time;
        float timeToHit = Mathf.Max(0, hitWindowStart);
        yield return new WaitForSeconds(timeToHit);

        enemy.currentAttackDamage = damage;
        enemy.EnableWeaponHitBox(true, false);

        yield return new WaitForSeconds(hitWindowDuration);
        enemy.EnableWeaponHitBox(false, false);

        float elapsed = Time.time - animStartTime;
        // 先等到慢放起点
        float timeToSlowStart = Mathf.Max(0, sheathSlowStartTime - elapsed);
        if (timeToSlowStart > 0f) yield return new WaitForSeconds(timeToSlowStart);

        // 收刀段：统一正常速度 + 决策
        float timeToSheath = Mathf.Max(0, sheathTime - sheathSlowStartTime);
        var phaseMgr = enemy.GetComponent<BossPhaseManager>();
        bool isBurst = phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;

        if (isBurst)
            enemy.nextMoveAfterSheath = FindObjectOfType<BossDecisionEngine>()?.ForceDecide();

        yield return new WaitForSeconds(timeToSheath);

        if (enemy.shouldAbortAttack) yield break;

        float tailTime = (229f / 60f) - sheathTime;
        if (tailTime > 0) yield return new WaitForSeconds(tailTime);

        enemy.anim.SetBool("isQuick", false);
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
        enemy.EnableWeaponHitBox(false, false);
        enemy.ForceWeaponToSheath();
    }
}