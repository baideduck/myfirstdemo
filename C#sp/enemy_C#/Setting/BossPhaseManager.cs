using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using System;
using Random = UnityEngine.Random;

public class BossPhaseManager : MonoBehaviour
{
    [Header("Phase thresholds")]
    [SerializeField] private float phaseTwoThreshold = 0.8f;
    [SerializeField] private float phaseThreeThreshold = 0.45f;
    [SerializeField] private float phaseFinalThreshold = 0.05f;

    [Header("Phase two - burst")]
    [SerializeField] private float phaseTwoDecisionSpeed = 1.1f;
    [SerializeField] private float phaseTwoDamageMult = 1.3f;
    [SerializeField] private float phaseTwoExhaustShorten = 0.7f;

    [Header("Phase three - master")]
    [SerializeField] private float phaseThreeDecisionSpeed = 0.7f;
    [SerializeField] private float phaseThreeFrequencyLow = 0.6f;

    [Header("Clash related")]
    [SerializeField] private string playerBlockFailAnim = "Block_Fail";
    [SerializeField] private string playerBlockHitAnim = "Block_Hit";
    [SerializeField] private string bossThrustAnim = "Thrust";
    [SerializeField] private string bossHitAirAnim = "Hit_Air";
    [SerializeField] private string bossRollAnim = "Roll";

    [Header("Phase effects & lock pos")]
    public GameObject burstPhaseAuraEffect;
    public GameObject masterPhaseEyeGlowEffect;

    [Header("Boss prefab")]
    public GameObject bossPrefab;

    [Header("Clash Timeline")]
    public UnityEngine.Playables.PlayableDirector clashTimeline;

    private GameObject currentBurstAura;
    private GameObject currentEyeGlow;

    [Header("Clash params")]
    [SerializeField] private float struggleKnockbackDistance = 8f;
    [SerializeField] private float struggleTotalDuration = 2f;
    [SerializeField] private float iaiPauseDuration = 0.8f;

    [Header("Death animation")]
    [SerializeField] private string blockAnimName = "Block_Hit";
    [SerializeField] private string blockHitLargeAnimName = "Hit_Large_F";

    public enum BossPhase
    {
        PhaseOne_Test,
        PhaseTwoBurst,
        PhaseThreeMaster,
        PhaseFinalFlee,
        Dead
    }

    public BossPhase CurrentPhase { get; private set; } = BossPhase.PhaseOne_Test;
    public event Action<BossPhase> OnPhaseChanged;

    private EnemyHealth enemyHealth;
    private EnemyController enemyController;
    private BossStamina bossStamina;
    private BossPosture bossPosture;

    private bool phaseTwoTriggered = false;
    private bool isPhaseTransitioning = false;  // true only during retreat+respawn
    private bool phaseThreeTriggered = false;
    private bool phaseFinalTriggered = false;
    private Coroutine executionCoroutine;
    public bool IaiAwakenedUsed { get; set; } = false;
    private GameObject player;

    public bool StruggleTriggered => phaseThreeTriggered;
    public bool IsInPhaseTransition => isPhaseTransitioning;

    private void Start()
    {
        enemyHealth = GetComponent<EnemyHealth>();
        enemyController = GetComponent<EnemyController>();
        bossStamina = GetComponent<BossStamina>();
        bossPosture = GetComponent<BossPosture>();
        player = GameObject.FindGameObjectWithTag("Player");
        ApplyPhaseOneParams();
    }

    private void Update()
    {
        if (CurrentPhase == BossPhase.PhaseFinalFlee)
            return;
        if (enemyHealth == null) return;
    }

    public void OnTakeDamageAfterThreshold()
    {
        if (enemyHealth == null) return;
        float hpPercent = enemyHealth.currentHealth / enemyHealth.maxHealth;

        if (!phaseTwoTriggered && hpPercent <= phaseTwoThreshold)
        {
            TriggerPhaseTwo();
            return;
        }

        if (phaseTwoTriggered && !phaseThreeTriggered && hpPercent <= phaseThreeThreshold)
        {
            TriggerPhaseThree();
            return;
        }
    }

    // ===================== Phase two - retreat =====================
    private void TriggerPhaseTwo()
    {
        phaseTwoTriggered = true;
        isPhaseTransitioning = true;
        CurrentPhase = BossPhase.PhaseTwoBurst;

        float thresholdHP = enemyHealth.maxHealth * 0.8f;
        enemyHealth.currentHealth = thresholdHP;

        enemyController.HasSuperArmor = true;
        enemyController.shouldAbortAttack = true;
        StartCoroutine(PhaseTwoRetreat());
    }

    private IEnumerator PhaseTwoRetreat()
    {
        enemyController.StopCurrentAttack();
        enemyController.EnableWeaponHitBox(false, false);
        enemyController.DisableAttackLayer();
        enemyController.ChangeState(EnemyStates.Idle);

        enemyController.anim.Play("Block_Hit", 0, 0f);
        enemyController.anim.Update(0f);
        enemyController.HasSuperArmor = true;

        // 等 Block_Hit 播完（50帧 = 0.833s），然后消失瞬移
        yield return new WaitForSeconds(50f / 60f);

        Vector3 spawnPos = Vector3.zero;
        if (player != null)
        {
            float randomAngle = Random.Range(0f, 360f);
            float randomDistance = Random.Range(20f, 30f);
            Vector3 offset = new Vector3(
                Mathf.Cos(randomAngle * Mathf.Deg2Rad) * randomDistance,
                0f,
                Mathf.Sin(randomAngle * Mathf.Deg2Rad) * randomDistance
            );
            spawnPos = player.transform.position + offset;
            spawnPos.y = enemyController.transform.position.y;
            if (Physics.Raycast(spawnPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, LayerMask.GetMask("Default")))
                spawnPos.y = hit.point.y;
        }

        GameObject currentBoss = enemyController.gameObject;

        // 原地消失 → 瞬移到远处 → 重新出现
        currentBoss.SetActive(false);
        currentBoss.transform.position = spawnPos;
        currentBoss.transform.rotation = Quaternion.identity;
        currentBoss.SetActive(true);

        // 继承属性
        enemyController = currentBoss.GetComponent<EnemyController>();
        enemyHealth = currentBoss.GetComponent<EnemyHealth>();
        bossPosture = currentBoss.GetComponent<BossPosture>();
        bossStamina = currentBoss.GetComponent<BossStamina>();

        // 开始 Phase 2 出场（后续逻辑沿用 PostSpawnSequence）
        StartCoroutine(PostSpawnSequence(spawnPos));
    }

    public IEnumerator PostSpawnSequence(Vector3 spawnPos)
    {
        if (enemyController == null) enemyController = GetComponent<EnemyController>();
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");

        var allBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in allBehaviours) mb.enabled = true;
        if (enemyController != null) enemyController.enabled = true;
        if (enemyController.anim != null) enemyController.anim.enabled = true;

        if (player != null)
        {
            Vector3 dir = (player.transform.position - enemyController.transform.position).normalized;
            dir.y = 0;
            if (dir.magnitude > 0.01f)
                enemyController.transform.rotation = Quaternion.LookRotation(dir);
        }

        enemyController.AttachWeaponToHand();
        enemyController.anim.Rebind();
        enemyController.anim.speed = 1f;
        enemyController.anim.Play("Buff", 0, 0f);
        yield return null;  // wait one frame for Buff to take effect

        Vector3 lockPos = enemyController.transform.position;
        Quaternion lockRot = enemyController.transform.rotation;
        float buffLength = 2.5f;
        RuntimeAnimatorController ac = enemyController.anim.runtimeAnimatorController;
        if (ac != null)
            foreach (var clip in ac.animationClips)
                if (clip != null && clip.name == "Buff") { buffLength = clip.length; break; }

        float elapsed = 0f;
        while (elapsed < buffLength)
        {
            enemyController.transform.position = lockPos;
            enemyController.transform.rotation = lockRot;
            elapsed += Time.deltaTime;
            yield return null;
        }

        enemyController.anim.speed = 1f;
        enemyController.HasSuperArmor = false;

        phaseTwoTriggered = true;
        CurrentPhase = BossPhase.PhaseTwoBurst;
        ApplyPhaseTwoParams();
        OnPhaseChanged?.Invoke(CurrentPhase);

        if (currentBurstAura != null) Destroy(currentBurstAura);
        if (burstPhaseAuraEffect != null)
            currentBurstAura = Instantiate(burstPhaseAuraEffect, transform);

        enemyController.shouldAbortAttack = false;
        enemyController.ChangeState(EnemyStates.Idle);
        if (player != null)
            enemyController.waitForPlayerEngage = true;
    }

    // ===================== Phase three - clash =====================
    public void TriggerPhaseThree()
    {
        if (phaseThreeTriggered) return;
        phaseThreeTriggered = true;
        CurrentPhase = BossPhase.PhaseThreeMaster;

        if (enemyController == null) enemyController = GetComponent<EnemyController>();
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");

        enemyController.StopCurrentAttack();
        enemyController.EnableWeaponHitBox(false, false);
        enemyController.HasSuperArmor = true;
        enemyController.DisableAttackLayer();
        enemyController.ChangeState(EnemyStates.Idle);

        MeeleFighter mf = player?.GetComponent<MeeleFighter>();
        PlayerController pc = player?.GetComponent<PlayerController>();
        if (mf != null) { mf.InAction = true; mf.IsHyperArmor = true; }
        if (pc != null) pc.LockMovement(5f);

        // 把 Boss 拉到玩家面前 3m 处，确保模型位置对齐
        Vector3 dirToBossFromPlayer = (enemyController.transform.position - player.transform.position).normalized;
        dirToBossFromPlayer.y = 0;
        if (dirToBossFromPlayer.magnitude > 0.01f)
        {
            enemyController.transform.position = player.transform.position + dirToBossFromPlayer * 3f;
            enemyController.transform.rotation = Quaternion.LookRotation(-dirToBossFromPlayer);
            player.transform.rotation = Quaternion.LookRotation(dirToBossFromPlayer);
        }

        BossDecisionEngine decisionEngine = GetComponent<BossDecisionEngine>();
        if (decisionEngine != null) decisionEngine.enabled = false;

        if (clashTimeline != null)
        {
            clashTimeline.Play();
            // 临时 0.1 倍速，用来观察角力节奏
            var rootPlayable = clashTimeline.playableGraph.GetRootPlayable(0);
            rootPlayable.SetSpeed(0.1f);
        }
    }

    public void OnStruggleFinished()
    {
        BossDecisionEngine decisionEngine = GetComponent<BossDecisionEngine>();
        if (decisionEngine != null) decisionEngine.enabled = true;

        ApplyPhaseThreeParams();

        if (currentEyeGlow != null) Destroy(currentEyeGlow);
        if (masterPhaseEyeGlowEffect != null)
            currentEyeGlow = Instantiate(masterPhaseEyeGlowEffect, transform);

        OnPhaseChanged?.Invoke(CurrentPhase);

        enemyController.waitForPlayerEngage = false;
        enemyController.ChangeState(EnemyStates.Idle);
    }

    // ===================== Phase four - execution =====================
    public void TriggerPhaseFinal()
    {
        phaseFinalTriggered = true;
        CurrentPhase = BossPhase.PhaseFinalFlee;
        OnPhaseChanged?.Invoke(CurrentPhase);
        executionCoroutine = StartCoroutine(ExecutionSequence());
    }

    public void StopExecutionSequence()
    {
        if (executionCoroutine != null)
        {
            StopCoroutine(executionCoroutine);
            executionCoroutine = null;
        }
    }

    private IEnumerator ExecutionSequence()
    {
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player");

        enemyController.StopCurrentAttack();
        enemyController.EnableWeaponHitBox(false, false);
        enemyController.HasSuperArmor = true;
        enemyController.DisableAttackLayer();

        if (enemyController.anim == null)
            enemyController.anim = enemyController.GetComponent<Animator>();
        enemyController.anim.applyRootMotion = false;

        enemyController.anim.speed = 1f;
        enemyController.anim.Play(blockAnimName, 0, 0f);
        yield return null;

        AnimatorStateInfo blockState = enemyController.anim.GetCurrentAnimatorStateInfo(0);
        float blockLength = blockState.IsName(blockAnimName) ? blockState.length : 0.5f;
        yield return new WaitForSeconds(blockLength);

        enemyController.anim.Play(blockHitLargeAnimName, 0, 0f);

        if (enemyController.weaponModel != null)
        {
            GameObject droppedWeapon = Instantiate(enemyController.weaponModel,
                enemyController.weaponModel.transform.position,
                enemyController.weaponModel.transform.rotation);
            droppedWeapon.transform.SetParent(null);
            Rigidbody rb = droppedWeapon.AddComponent<Rigidbody>();
            rb.velocity = Vector3.down * 2f + enemyController.transform.forward * 0.5f;
            Destroy(droppedWeapon, 5f);
        }

        enemyController.HideAllWeaponModels();
        enemyController.ForceWeaponToSheath();

        yield return null;
        AnimatorStateInfo largeState = enemyController.anim.GetCurrentAnimatorStateInfo(0);
        float largeLength = largeState.IsName(blockHitLargeAnimName) ? largeState.length : 0.8f;
        yield return new WaitForSeconds(largeLength);

        Vector3 frozenPos;
        if (player != null)
        {
            Vector3 playerPos = player.transform.position;
            Vector3 playerForward = player.transform.forward;
            playerForward.y = 0;
            if (playerForward.magnitude < 0.01f)
                playerForward = Vector3.forward;

            frozenPos = playerPos + playerForward * 2f;
            frozenPos.y = enemyController.transform.position.y;
            enemyController.transform.position = frozenPos;
        }
        else
        {
            frozenPos = enemyController.transform.position;
        }
        Quaternion frozenRot = enemyController.transform.rotation;

        enemyController.isExecutionFrozen = true;
        enemyController.anim.speed = 0f;
        enemyController.anim.applyRootMotion = false;

        CharacterController cc = enemyController.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        enemyController.isExecutionFrozen = true;

        // Wait for left mouse click to execute
        while (!Input.GetMouseButtonDown(0))
        {
            enemyController.transform.position = frozenPos;
            enemyController.transform.rotation = frozenRot;
            if (enemyController.anim == null)
                enemyController.anim = enemyController.GetComponent<Animator>();
            yield return null;
        }

        // Unfreeze and play death
        enemyController.isExecutionFrozen = false;
        enemyController.anim.speed = 1f;
        enemyController.anim.applyRootMotion = false;

        if (cc != null) cc.enabled = true;

        enemyController.anim.Play("Hit_Large_F", 0, 0f);
        enemyController.anim.Update(0f);

        // Unlock player
        MeeleFighter mf = player.GetComponent<MeeleFighter>();
        PlayerController pc = player.GetComponent<PlayerController>();
        if (mf != null) { mf.InAction = false; mf.IsHyperArmor = false; }
        if (pc != null) pc.LockMovement(0f);

        yield return new WaitForSeconds(1.5f);

        // Cleanup
        enemyController.HasSuperArmor = false;
        EnemyHealth health = enemyController.GetComponent<EnemyHealth>();
        if (health != null)
            health.ForceDeath();
    }

    // ===================== Phase param application =====================
    private void ApplyPhaseOneParams()
    {
        if (enemyController != null)
        {
            enemyController.decisionSpeedMultiplier = 1.0f;
            enemyController.frequencyMultiplier = 1.0f;
        }
    }

    private void ApplyPhaseTwoParams()
    {
        if (enemyController != null)
        {
            enemyController.decisionSpeedMultiplier = phaseTwoDecisionSpeed;
            enemyController.frequencyMultiplier = 1.0f;
        }
    }

    private void ApplyPhaseThreeParams()
    {
        if (enemyController != null)
        {
            enemyController.decisionSpeedMultiplier = phaseThreeDecisionSpeed;
            enemyController.frequencyMultiplier = phaseThreeFrequencyLow;
        }
    }
}
