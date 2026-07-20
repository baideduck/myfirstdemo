using System.Collections;
using UnityEngine;

public class ThrustSlashState : State<EnemyController>
{
    private EnemyController enemy;
    private bool attackFinished = false;
    private Coroutine routine;
    private int attackLayer;

    [Header("Thrust time params")]
    public float animTotalTime = 1.5f;
    public float dashStartTime = 0.1f;
    public float dashDuration = 0.3f;
    public float hitWindowStart = 0f;
    public float hitWindowDuration = 0.2f;
    public float sheathTime = 1.2f;

    [Header("Movement")]
    public float dashDistance = 10f;
    public AnimationCurve dashCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Damage")]
    public int damage = 35;

    [Header("Move properties")]
    public bool canBeBlocked = true;
    public bool isGuardBreak = false;
    public bool hasSuperArmor = true;

    [Header("Collision detection")]
    [SerializeField] private float stopDistance = 1f;

    private Vector3 dashDirection;

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

        Vector3 playerPos = enemy.GetPlayerPosition();
        dashDirection = (playerPos - enemy.transform.position).normalized;
        dashDirection.y = 0;
        if (dashDirection.magnitude < 0.01f) dashDirection = enemy.transform.forward;

        enemy.anim.SetBool("isThrust", true);
        enemy.HasSuperArmor = true;

        attackLayer = enemy.anim.GetLayerIndex("Attack Layer");
        if (attackLayer == -1) attackLayer = 0;
        enemy.anim.SetLayerWeight(attackLayer, 1f);
        enemy.anim.Play("Thrust", attackLayer, 0f);

        routine = enemy.StartCoroutine(ThrustSlashRoutine());
        enemy.RegisterAttackRoutine(routine);
    }

    IEnumerator ThrustSlashRoutine()
    {
        enemy.anim.Play("Thrust", attackLayer, 0f);
        enemy.anim.Update(0);
        yield return null;

        float checkStart = Time.time;
        while (!enemy.anim.GetCurrentAnimatorStateInfo(attackLayer).IsName("Thrust"))
        {
            if (Time.time - checkStart > 0.2f)
                break;
            yield return null;
        }

        float animStartTime = Time.time;
        if (enemy.shouldAbortAttack) yield break;

        float timeToDash = Mathf.Max(0, dashStartTime);
        yield return new WaitForSeconds(timeToDash);
        if (enemy.shouldAbortAttack) yield break;

        enemy.anim.speed = 2f;
        Vector3 startPos = enemy.transform.position;
        Vector3 playerPos = enemy.GetPlayerPosition();
        playerPos.y = startPos.y;
        float distToPlayer = Vector3.Distance(startPos, playerPos);
        Vector3 dirToPlayer = (playerPos - startPos).normalized;
        enemy.transform.rotation = Quaternion.LookRotation(dirToPlayer);

        // 目标：停在玩家面前 stopDistance 处
        Vector3 targetPos = playerPos - dirToPlayer * stopDistance;
        float dashLength = Vector3.Distance(startPos, targetPos);

        // 玩家太近（目标在身后）→ 前冲最小距离
        if (dashLength > distToPlayer)
        {
            if (distToPlayer < 1f)
                targetPos = startPos + dirToPlayer * distToPlayer * 0.5f;  // 贴脸时微动
            else
                targetPos = startPos + dirToPlayer * Mathf.Max(distToPlayer * 0.5f, 1f);
        }
        // 限制最大突进距离
        else if (dashLength > dashDistance)
            targetPos = startPos + dirToPlayer * dashDistance;

        float actualDashDistance = Vector3.Distance(startPos, targetPos);

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            if (enemy.shouldAbortAttack) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dashDuration);
            enemy.transform.position = Vector3.Lerp(startPos, targetPos, dashCurve.Evaluate(t));
            float currentDist = Vector3.Distance(enemy.transform.position, playerPos);
            if (currentDist <= stopDistance) break;
            yield return null;
        }

        enemy.anim.speed = 1f;
        if (enemy.shouldAbortAttack) yield break;

        float elapsedSinceStart = Time.time - animStartTime;
        float timeToHit = Mathf.Max(0, hitWindowStart - elapsedSinceStart);
        yield return new WaitForSeconds(timeToHit);
        if (enemy.shouldAbortAttack) yield break;

        enemy.currentAttackDamage = damage;
        enemy.EnableWeaponHitBox(true, false);

        yield return new WaitForSeconds(hitWindowDuration);
        enemy.EnableWeaponHitBox(false, false);
        if (enemy.shouldAbortAttack) yield break;

        elapsedSinceStart = Time.time - animStartTime;
        float timeToSheath = Mathf.Max(0, sheathTime - elapsedSinceStart);
        var phaseMgr = enemy.GetComponent<BossPhaseManager>();
        bool isBurst = phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;
        if (isBurst)
            enemy.nextMoveAfterSheath = FindObjectOfType<BossDecisionEngine>()?.ForceDecide();

        yield return new WaitForSeconds(timeToSheath);

        elapsedSinceStart = Time.time - animStartTime;
        float remaining = Mathf.Max(0, animTotalTime - elapsedSinceStart);
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        enemy.anim.SetBool("isThrust", false);
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
        enemy.RegisterAttackRoutine(null);
        enemy.HasSuperArmor = false;
        enemy.EnableWeaponHitBox(false, false);
        enemy.ForceWeaponToSheath();
        if (enemy != null && enemy.anim != null)
        {
            enemy.anim.speed = 1f;
            enemy.anim.SetBool("isThrust", false);
        }
    }
}
