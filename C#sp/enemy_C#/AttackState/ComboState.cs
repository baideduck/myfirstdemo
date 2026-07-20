using System.Collections;
using UnityEngine;

public class ComboState : State<EnemyController>
{
    private EnemyController enemy;
    private bool attackFinished = false;
    private Coroutine routine;

    [Header("��նʱ���ᣨ60fps����320֡��")]
    public float animTotalTime = 5.333f;
    public float hitWindowStart = 0f;              // 第0帧开始
    public float hitWindowDuration = 4.25f;         // 持续到第255帧
    public int damage = 30;                         // 单次伤害

    [Header("ǰ��֡����")]
    public float freezeDuration = 0.5f;

    [Header("ǰ�ö���")]
    [SerializeField] private string preAttackAnimName = "ToFight";  // ����ǰ�����������ŵĶ���

    // ������ʼ����������ն�ڼ䲻ת��
    private Vector3 attackDirection;

    // ========== Ԥ������Ч��� ==========
    [Header("��Ч (Ԥ��)")]
    public AudioClip comboStartSound;
    public AudioClip comboHitSound;

    // ========== Ԥ������Ч��� ==========
    [Header("��Ч (Ԥ��)")]
    public GameObject comboSlashEffect;

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

        // ��¼������ʼ����������ң����������ٸ���ת��
        Vector3 playerPos = enemy.GetPlayerPosition();
        Vector3 dirToPlayer = (playerPos - enemy.transform.position).normalized;
        dirToPlayer.y = 0;
        if (dirToPlayer.magnitude > 0.01f)
            attackDirection = dirToPlayer;
        else
            attackDirection = enemy.transform.forward;

        // ǿ������һ�γ���
        enemy.transform.rotation = Quaternion.LookRotation(attackDirection);

        // ����Э��
        if (routine != null) enemy.StopCoroutine(routine);
        routine = enemy.StartCoroutine(ComboRoutine());
        enemy.RegisterAttackRoutine(routine);
    }

    IEnumerator ComboRoutine()
    {
        // 第0帧：刀挂手上 + 开伤害窗口
        enemy.AttachWeaponToHand();
        enemy.anim.applyRootMotion = true;
        enemy.anim.SetBool("isCombo", true);
        enemy.currentAttackDamage = damage;
        enemy.EnableWeaponHitBox(true, false);

        float animStartTime = Time.time;

        // 等待伤害窗口结束（第255帧）
        yield return new WaitForSeconds(hitWindowDuration);
        enemy.EnableWeaponHitBox(false, false);

        if (enemy.shouldAbortAttack) yield break;

        // 等待尾段（第255帧到第320帧）
        float elapsed = Time.time - animStartTime;
        float remaining = Mathf.Max(0, animTotalTime - elapsed);

        var phaseMgr = enemy.GetComponent<BossPhaseManager>();
        bool isBurst = phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;
        if (isBurst)
            enemy.nextMoveAfterSheath = FindObjectOfType<BossDecisionEngine>()?.ForceDecide();

        if (remaining > 0)
            yield return new WaitForSeconds(remaining);

        // 收尾
        Vector3 finalPosition = enemy.transform.position;
        enemy.anim.SetBool("isCombo", false);
        enemy.anim.applyRootMotion = false;
        enemy.transform.position = finalPosition;
        enemy.anim.Play("Idle", 0, 0f);
        enemy.AttachWeaponToSheath();
        yield return null;
        attackFinished = true;
    }

    public override void Execute()
    {
        if (enemy == null) return;
        // ��ն�ڼ䲻ת��
        if (attackFinished) enemy.OnAttackFinished();
    }

    public override void Exit()
    {
        if (routine != null) enemy.StopCoroutine(routine);
        routine = null;
        enemy.RegisterAttackRoutine(null);
        enemy.EnableWeaponHitBox(false, false);
        enemy.ForceWeaponToSheath();
        enemy.anim.applyRootMotion = false;
        enemy.anim.Play("Idle", 0, 0f);
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && enemy != null)
            AudioSource.PlayClipAtPoint(clip, enemy.transform.position);
    }
}