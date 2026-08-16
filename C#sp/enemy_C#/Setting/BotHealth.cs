using System.Collections;
using UnityEngine;

/// <summary>
/// 木桩 / 训练假人血量 —— 用于验证攻击效果（顿帧、震屏、FOV）
/// 挂到任何想作为攻击目标的物体上即可，需要 Enemy 层 + Animator
/// </summary>
public class BotHealth : MonoBehaviour
{
    [Header("血量")]
    public float maxHealth = 9999f;
    [SerializeField] private float currentHealth;
    public bool infiniteHealth = true;           // 无限血量，永远不死

    [Header("受击反馈")]
    public bool useHitReaction = true;

    private Animator animator;
    private bool isDead = false;
    public bool IsDead => isDead;

    void Start()
    {
        currentHealth = maxHealth;
        animator = GetComponent<Animator>();
    }

    /// <summary>
    /// 与 EnemyHealth.TakeDamage 接口一致，PlayerWeaponHitbox 直接调用
    /// </summary>
    public void TakeDamage(float damage, Vector3 hitPoint, float hitStopDuration)
    {
        if (isDead) return;

        if (!infiniteHealth)
        {
            currentHealth -= damage;
            if (currentHealth <= 0)
            {
                currentHealth = 0;
                Die();
                return;
            }
        }

        // 播放受击动画 + 顿帧
        if (useHitReaction && animator != null)
            StartCoroutine(PlayHitReaction(hitPoint, hitStopDuration));
    }

    private IEnumerator PlayHitReaction(Vector3 hitPoint, float stopDuration)
    {
        Vector3 hitDir = (hitPoint - transform.position).normalized;
        string animName = GetHitAnimName(hitDir);

        // 播放受击
        animator.Play(animName, 0, 0f);

        // 顿帧（卡肉，与 EnemyController.HitReactionRoutine 一致）
        if (stopDuration > 0f)
        {
            yield return null;
            animator.speed = 0f;
            yield return new WaitForSecondsRealtime(stopDuration);
            animator.speed = 1f;
        }

        // 等待动画播完
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        float animLength = state.IsName(animName) ? state.length : 0.5f;
        yield return new WaitForSeconds(animLength);

        animator.speed = 1f;
    }

    private string GetHitAnimName(Vector3 worldDir)
    {
        Vector3 localDir = transform.InverseTransformDirection(worldDir.normalized);
        float angle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
        if (angle > -45 && angle <= 45) return "Hit_F";
        if (angle > 45 && angle <= 135) return "Hit_L";
        if (angle < -45 && angle >= -135) return "Hit_R";
        return "Hit_B";
    }

    private void Die()
    {
        isDead = true;
        if (animator != null)
            animator.speed = 1f;
        StopAllCoroutines();
    }

    [ContextMenu("Reset Health")]
    public void ResetHealth()
    {
        currentHealth = maxHealth;
        isDead = false;
        if (animator != null)
            animator.speed = 1f;
    }
}
