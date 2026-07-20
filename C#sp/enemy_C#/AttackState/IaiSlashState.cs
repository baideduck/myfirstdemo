using System.Collections;
using UnityEngine;

public class IaiSlashState : State<EnemyController>
{
    private EnemyController enemy;
    private bool attackFinished = false;
    private Coroutine routine;
    private bool isFirstAwakenedIai = false;

    [Header("居合时间参数")]
    public float animTotalTime = 1.167f;
    public float chargePauseFrame = 0.083f;
    public float hitWindowStart = 0f;
    public float hitWindowDuration = 0.05f;
    public float sheathFrame = 1.0f;

    [Header("起手暂停")]
    public float iaiFreezeDuration = 0.8f;

    [Header("命中慢放")]
    public float hitSlowMoScale = 0.2f;
    public float hitSlowMoDuration = 0.15f;

    [Header("伤害")]
    public int damage = 62;

    [Header("位移参数")]
    public float teleportDistance = 1f;       // 闪到玩家面前 1 米

    [Header("距离修正")]
    [SerializeField] private float closeRangeThreshold = 10f;
    [SerializeField] private float longRangeThreshold = 20f;
    [SerializeField] private float closeRangeDamageMult = 0.8f;

    [Header("招式属性")]
    public bool canBeBlocked = true;
    public bool isGuardBreak = true;  // 击破格挡
    public bool hasSuperArmor = true;

    private Vector3 lockedPlayerPosition;

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

        isFirstAwakenedIai = enemy.iaiAwakened && !enemy.iaiUsed;
        if (isFirstAwakenedIai)
        {
            enemy.iaiUsed = true;
        }

        enemy.HasSuperArmor = true;
        lockedPlayerPosition = enemy.GetPlayerPosition();
        enemy.FaceTarget(lockedPlayerPosition);

        enemy.AttachWeaponToHand();
        enemy.anim.SetBool("isIai", true);
        routine = enemy.StartCoroutine(IaiRoutine());
        enemy.RegisterAttackRoutine(routine);
    }

    IEnumerator IaiRoutine()
    {
        enemy.anim.speed = 1f;
        float animStartTime = Time.time;

        // ===== 阶段1：直接位移（临时关 Root Motion） =====
        bool wasRootMotion = enemy.anim.applyRootMotion;
        enemy.anim.applyRootMotion = false;

        // ===== 第5帧暂停（仅限普通居合，角力不经过此状态机） =====
        float timeToPause = Mathf.Max(0, chargePauseFrame - (Time.time - animStartTime));
        yield return new WaitForSeconds(timeToPause);
        enemy.anim.speed = 0f;
        yield return new WaitForSecondsRealtime(iaiFreezeDuration);
        enemy.anim.speed = 1f;

        // ===== 暂停结束后瞬间闪现到玩家面前 1 米处 =====
        lockedPlayerPosition = enemy.GetPlayerPosition();
        Vector3 dirToPlayer = (lockedPlayerPosition - enemy.transform.position).normalized;
        dirToPlayer.y = 0;
        enemy.FaceTarget(lockedPlayerPosition);

        Vector3 dashTargetPos = lockedPlayerPosition - dirToPlayer * teleportDistance;
        dashTargetPos.y = enemy.transform.position.y;
        enemy.transform.position = dashTargetPos;

        // ===== 命中窗口 =====
        enemy.currentAttackDamage = damage;
        enemy.EnableWeaponHitBox(true, false);

        // 慢放（不影响伤害窗口）
        enemy.anim.speed = hitSlowMoScale;

        CameraController camCtrl = Camera.main?.GetComponent<CameraController>();
        if (camCtrl != null)
        {
            Vector3 playerPos = enemy.GetPlayerPosition();
            camCtrl.TriggerIaiShake(playerPos);
        }

        // 伤害窗口持续 0.5 秒（不受慢放影响，用真实时间）
        yield return new WaitForSecondsRealtime(0.5f);

        enemy.EnableWeaponHitBox(false, false);
        enemy.anim.speed = 1f;

        // ✅ 命中窗口结束后再还原 Root Motion
        enemy.anim.applyRootMotion = wasRootMotion;

        // ===== 阶段4：收刀（统一正常速度） =====
        float elapsed = Time.time - animStartTime;
        float timeToSheath = Mathf.Max(0, sheathFrame - elapsed);
        var phaseMgr = enemy.GetComponent<BossPhaseManager>();
        bool isBurst = phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;

        if (isBurst)
            enemy.nextMoveAfterSheath = FindObjectOfType<BossDecisionEngine>()?.ForceDecide();

        yield return new WaitForSeconds(timeToSheath);

        enemy.anim.SetBool("isIai", false);

        // ===== 阶段5：尾段 =====
        elapsed = Time.time - animStartTime;
        float remaining = Mathf.Max(0, animTotalTime - elapsed);
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        attackFinished = true;
    }

    public override void Execute()
    {
        if (enemy == null) return;
        if (attackFinished)
            enemy.OnAttackFinished();
    }

    public override void Exit()
    {
        if (routine != null) enemy.StopCoroutine(routine);
        enemy.RegisterAttackRoutine(null);
        enemy.HasSuperArmor = false;
        if (enemy != null && enemy.anim != null)
            enemy.anim.speed = 1f;
        enemy.EnableWeaponHitBox(false, false);
        enemy.ForceWeaponToSheath();
    }
}