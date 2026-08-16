using System.Collections;
using UnityEngine;

public class PlayerDefense : MonoBehaviour
{
    [Header("完美格挡")]
    [SerializeField] private float perfectBlockWindow = 0.2f;

    [Header("格挡参数")]
    [SerializeField] private float blockStaminaCost = 8f;
    [SerializeField] private int blockSharpnessCost = 2;

    private PlayerController playerController;
    private MeeleFighter meleeFighter;
    private BossStamina bossStamina;
    private PlayerHealth playerHealth;
    private PlayerStamina playerStamina;
    private PlayerSharpness playerSharpness;
    private Animator animator;

    [Header("音效")]
    public AudioClip perfectBlockSound;
    public AudioClip normalBlockSound;
    public AudioClip guardBreakSound;
    public AudioClip playerHitSound;

    // Block_Fail 动画的预计时长（秒），用于恢复
    private const float BLOCK_FAIL_RECOVER_TIME = 0.8f;

    private void Start()
    {
        playerController = GetComponent<PlayerController>();
        meleeFighter = GetComponent<MeeleFighter>();
        animator = GetComponent<Animator>();
        bossStamina = FindObjectOfType<EnemyController>()?.GetComponent<BossStamina>();
        playerHealth = GetComponent<PlayerHealth>();
        playerStamina = GetComponent<PlayerStamina>();
        playerSharpness = GetComponent<PlayerSharpness>();
    }

    /// <summary>
    /// BlockFail 后封锁输入，播完后恢复
    /// </summary>
    private void OnBlockFailed()
    {
        meleeFighter.TriggerBlockFail();
        // InAction = true → 移动代码走 ApplyGravityOnly，拦截手动输入
        // 但 OnAnimatorMove 仍能运行 Root Motion 驱动击退
        meleeFighter.InAction = true;
        StartCoroutine(RecoverFromBlockFail());
    }

    private IEnumerator RecoverFromBlockFail()
    {
        yield return new WaitForSeconds(BLOCK_FAIL_RECOVER_TIME);

        // 关闭 Root Motion，防止持刀 idle 动画残留驱动滑步
        if (animator != null) animator.applyRootMotion = false;

        // ★ 锁定位置 0.2s，防止 Block_Fail → Idle 过渡期间滑步
        Vector3 lockedPos = transform.position;
        meleeFighter.InAction = false;
        float lockTime = 0.2f;
        float elapsed = 0f;
        while (elapsed < lockTime)
        {
            elapsed += Time.deltaTime;
            transform.position = lockedPos;
            yield return null;
        }

        // 锁定移动一小段时间，给 Animator 完整过渡
        if (playerController != null) playerController.LockMovement(0.2f);
    }

    /// <summary>
    /// 处理Boss攻击。返回 true 表示实际命中了（造成伤害/格挡），
    /// 返回 false 表示被闪避等无效命中，调用方不应消耗伤害锁。
    /// </summary>
    public bool ProcessEnemyAttack(int damage, bool isGuardBreak, Vector3 hitPoint)
    {
        EnemyController enemy = FindObjectOfType<EnemyController>();
        Vector3 attackDir = (transform.position - hitPoint).normalized;

        // 闪避无敌：不消耗伤害锁
        if (playerController.IsInvincible)
        {
            bossStamina?.ConsumeStamina(BossStamina.StaminaDrainType.Dodge);
            return false;
        }

        // 正在格挡
        if (meleeFighter.IsBlocking)
        {
            if (isGuardBreak)
            {
                playerHealth?.TakeDamage(damage);
                playerStamina?.Consume(blockStaminaCost * 1.5f);
                bossStamina?.ConsumeStamina(BossStamina.StaminaDrainType.DirectHit);
                OnBlockFailed();
                if (guardBreakSound != null)
                    AudioSource.PlayClipAtPoint(guardBreakSound, transform.position);

                if (!meleeFighter.IsHyperArmor)
                    meleeFighter?.PlayHitReaction(attackDir);
            }
            else
            {
                float elapsed = Time.time - meleeFighter.BlockStartTime;
                bool isPerfect = elapsed <= perfectBlockWindow;

                if (isPerfect)
                {
                    bossStamina?.ConsumeStamina(BossStamina.StaminaDrainType.PerfectBlock);
                    meleeFighter.OnBlockedAttack();
                    if (perfectBlockSound != null)
                        AudioSource.PlayClipAtPoint(perfectBlockSound, transform.position);
                    if (enemy != null && enemy.isActiveAndEnabled)
                    {
                        Vector3 attackDir2 = (transform.position - hitPoint).normalized;
                        enemy.combat.PlayGuardBreakReaction(attackDir2);

                        BossPosture posture = enemy.GetComponent<BossPosture>();
                        BossStamina stamina = enemy.GetComponent<BossStamina>();
                        if (posture != null && stamina != null)
                        {
                            float drain = stamina.GetCurrentAttackBaseDrain();
                            posture.OnPerfectBlocked(drain);
                            enemy?.GetComponent<BossComboChain>()?.OnPlayerBlocked(true);
                        }
                    }
                }
                else
                {
                    if (normalBlockSound != null)
                        AudioSource.PlayClipAtPoint(normalBlockSound, transform.position);
                    if (playerStamina != null && !playerStamina.Consume(blockStaminaCost))
                    {
                        OnBlockFailed();
                        bossStamina?.ConsumeStamina(BossStamina.StaminaDrainType.DirectHit);
                        return true;
                    }

                    bossStamina?.ConsumeStamina(BossStamina.StaminaDrainType.Block);
                    meleeFighter.OnBlockedAttack();
                    playerSharpness?.ConsumeBlock();
                    enemy?.GetComponent<BossComboChain>()?.OnPlayerBlocked(false);
                }
            }
            return true;
        }

        // 直接受伤
        playerHealth?.TakeDamage(damage);
        bossStamina?.ConsumeStamina(BossStamina.StaminaDrainType.DirectHit);

        if (playerHitSound != null)
            AudioSource.PlayClipAtPoint(playerHitSound, transform.position);
        if (!meleeFighter.IsHyperArmor)
            meleeFighter?.PlayHitReaction(attackDir);

        return true;
    }
}
