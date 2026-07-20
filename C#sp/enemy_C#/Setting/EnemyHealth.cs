using System.Collections;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("受击阈值")]
    public float heavyHitThreshold = 25f;
    [SerializeField] private float executionThresholdPercent = 0.05f;

    public float maxHealth = 100f;
    public float currentHealth;
    public Animator animator;
    public HitStopManager hitStopManager;
    private EnemyController enemyController;
    private bool isDead = false;
    private bool phaseFinalTriggered = false;

    // 从换区生成时外部注入血量，标记后 Start 不再覆盖
    [HideInInspector] public bool initialized = false;

    void Start()
    {
        if (!initialized)
            currentHealth = maxHealth;
        initialized = true;
        animator = GetComponent<Animator>();
        hitStopManager = GetComponent<HitStopManager>();
        enemyController = GetComponent<EnemyController>();
    }

    public void TakeDamage(float damage, Vector3 hitPoint, float hitStopDuration)
    {
        if (isDead) return;

        BossPhaseManager phaseMgr = GetComponent<BossPhaseManager>();
        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseFinalFlee)
            return;

        // 架势倒地期间伤害 ×1.5
        if (enemyController != null && enemyController.isPostureBreakExhaust)
            damage *= 1.5f;

        currentHealth -= damage;
        float hpPercent = currentHealth / maxHealth;

        // ══════ 阶段二检查（80%）：先执行，确保不被阶段三跳过 ══════
        phaseMgr?.OnTakeDamageAfterThreshold();

        // Skip hit reactions during phase transition (retreat + respawn)
        if (phaseMgr != null && phaseMgr.IsInPhaseTransition)
            return;

        // ══════ 阶段三检查（45%） ══════
        if (phaseMgr != null && !phaseMgr.StruggleTriggered)
        {
            if (hpPercent <= 0.45f)
            {
                phaseMgr.TriggerPhaseThree();
                return;   // 角力流程接管，跳过普通受击处理
            }
        }

        // ����Ƿ�Ӧ�ô�������
        bool shouldTriggerExecution = false;
        if (currentHealth <= 0)
        {
            shouldTriggerExecution = true;
        }
        else if (hpPercent <= executionThresholdPercent)
        {
            shouldTriggerExecution = true;
        }

        if (shouldTriggerExecution && !phaseFinalTriggered)
        {
            currentHealth = 1f;
            phaseFinalTriggered = true;
            phaseMgr?.TriggerPhaseFinal();
            return;
        }

        // �ӺϾ����ж��������������������󲻻��ߵ����
        if (enemyController != null)
        {
            if (hpPercent <= 0.45f && !enemyController.iaiAwakened)
            {
                enemyController.iaiAwakened = true;
            }
            if (enemyController.iaiAwakened && !enemyController.iaiUsed)
            {
                enemyController.iaiUsed = true;
                if (phaseMgr != null) phaseMgr.IaiAwakenedUsed = true;
                enemyController.TriggerAwakenedIai();
                return;
            }
        }

        bool isHeavy = damage >= heavyHitThreshold;
        if (enemyController != null)
        {
            Vector3 hitDir = (hitPoint - transform.position).normalized;
            enemyController.PlayHitReaction(hitDir, isHeavy, hitStopDuration);
        }

        if (currentHealth <= 0)
        {
            ForceDeath();
        }
    }

    public void ForceDeath()
    {
        if (isDead) return;
        currentHealth = 0;
        Die();
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        if (enemyController != null)
            enemyController.EnableDeath();

        if (animator != null)
        {
            int attackLayer = animator.GetLayerIndex("Attack Layer");
            if (attackLayer != -1)
                animator.SetLayerWeight(attackLayer, 0f);

            animator.SetBool("isSlashing", false);
            animator.SetBool("isCombo", false);
            animator.SetBool("isQuick", false);
            animator.SetBool("isChargeSlash", false);
            animator.SetBool("isKanpo", false);
            animator.SetBool("isIai", false);
            animator.SetBool("isExhausted", false);
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
            col.enabled = false;

        StartCoroutine(DestroyAfterDelay(3f));
    }

    IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }
}