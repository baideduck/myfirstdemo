using System.Collections;
using UnityEngine;

public class DodgeState : State<EnemyController>
{
    private EnemyController enemy;
    private bool finished = false;
    private Vector3 dashDirection;
    private float dodgeDistance;
    private float dodgeDuration;
    private bool previousRootMotion;
    private string dodgeAnimName = "Dodge_B";

    [Header("探索阶段 (退/侧闪)")]
    public float phaseOneDistance = 4f;
    public float phaseOneDuration = 0.4f;
    [Range(0f, 1f)] public float phaseOneSideStepChance = 0.2f;

    [Header("狂暴阶段")]
    public float phaseTwoDistance = 5f;
    public float phaseTwoDuration = 0.35f;

    [Header("宗师阶段 (微距)")]
    public float phaseThreeDistance = 1.5f;
    public float phaseThreeDuration = 0.2f;
    public float phaseThreeTriggerDistance = 2.5f;

    [Header("位移曲线")]
    public AnimationCurve dodgeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("预留特效音效")]
    public GameObject dodgeEffect;
    public AudioClip dodgeSound;

    public override void Enter(EnemyController owner)
    {
        enemy = owner;

        // 最后防线：Dodge 冷却中或已在 Dodge → 直接退回 Idle
        if (Time.time < enemy.noDodgeUntil || enemy.isDodging)
        {
            enemy.ChangeState(EnemyStates.Idle);
            return;
        }

        finished = false;
        enemy.isDodging = true;

        enemy.EnableWeaponHitBox(false, false);
        enemy.DisableAttackLayer();

        DetermineDodgeParameters();

        // 保底：如果算出来的位移距离太短，强行走后1米
        float estimatedDist = dodgeCurve.Evaluate(1f) * dodgeDistance;
        if (estimatedDist < 0.5f)
        {
            dashDirection = -enemy.transform.forward;
            dodgeDistance = 1f;
            dodgeDuration = 0.25f;
        }

        enemy.anim.Play(dodgeAnimName, 0, 0f);

        if (dashDirection.magnitude < 0.01f)
            dashDirection = -enemy.transform.forward;

        if (dodgeSound != null)
            AudioSource.PlayClipAtPoint(dodgeSound, enemy.transform.position);

        if (dodgeEffect != null)
            Object.Instantiate(dodgeEffect, enemy.transform.position, Quaternion.identity);

        previousRootMotion = enemy.anim.applyRootMotion;
        enemy.anim.applyRootMotion = false;

        enemy.StartCoroutine(PerformDodge());
    }

    private void DetermineDodgeParameters()
    {
        BossPhaseManager phaseMgr = enemy?.GetComponent<BossPhaseManager>();

        float distance, duration;
        if (phaseMgr == null) { distance = phaseOneDistance; duration = phaseOneDuration; }
        else
        {
            switch (phaseMgr.CurrentPhase)
            {
                case BossPhaseManager.BossPhase.PhaseTwoBurst:
                    distance = phaseTwoDistance; duration = phaseTwoDuration; break;
                case BossPhaseManager.BossPhase.PhaseThreeMaster:
                    distance = phaseThreeDistance; duration = phaseThreeDuration; break;
                default:
                    distance = phaseOneDistance; duration = phaseOneDuration; break;
            }
        }

        // 随机方向：直后 / 左后 / 右后
        Vector3 toPlayer = (enemy.GetPlayerPosition() - enemy.transform.position).normalized;
        toPlayer.y = 0;
        if (toPlayer.magnitude < 0.01f) toPlayer = enemy.transform.forward;

        float roll = Random.value;
        if (roll < 0.33f)
        {
            dodgeAnimName = "Dodge_B";
            dashDirection = -toPlayer;
        }
        else if (roll < 0.66f)
        {
            dodgeAnimName = "Dodge_B_L";
            Vector3 left = Vector3.Cross(toPlayer, Vector3.up).normalized;
            dashDirection = (-toPlayer + left * 0.5f).normalized;
        }
        else
        {
            dodgeAnimName = "Dodge_B_R";
            Vector3 right = -Vector3.Cross(toPlayer, Vector3.up).normalized;
            dashDirection = (-toPlayer + right * 0.5f).normalized;
        }

        dodgeDistance = distance;
        dodgeDuration = duration;
    }

    IEnumerator PerformDodge()
    {
        // 关掉 CharacterController，避免它干扰手动位移
        CharacterController cc = enemy.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Vector3 startPos = enemy.transform.position;
        float elapsed = 0f;

        while (elapsed < dodgeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dodgeDuration;
            enemy.transform.position = startPos + dashDirection * (dodgeCurve.Evaluate(t) * dodgeDistance);
            yield return null;
        }

        enemy.transform.position = startPos + dashDirection * dodgeDistance;

        // 恢复 CharacterController
        if (cc != null) cc.enabled = true;

        finished = true;
    }

    public override void Execute()
    {
        if (enemy == null) return;
        if (finished)
            enemy.ChangeState(EnemyStates.Idle);
    }

    public override void Exit()
    {
        enemy.isDodging = false;  // 解除锁定

        // 通知 EnemyController 追击惩罚计时 + 近距 Dodge 冷却同步
        enemy.consecutiveHits = 0;  // 成功 Dodge → 重置连续受击
        enemy.OnDodgeEnded();
        enemy.lastDodgeTime = Time.time;
        enemy.lastAttackEndTime = Time.time;  // 防止 Dodge 后立即再次触发近距 Dodge
        enemy.noDodgeUntil = Time.time + 2f;  // 硬性冷却：2秒内任何源不许再出 Dodge

        enemy.anim.applyRootMotion = previousRootMotion;

        int attackLayer = enemy.anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1)
            enemy.anim.SetLayerWeight(attackLayer, 1f);
        enemy.EnableWeaponHitBox(false, false);
    }
}
