using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyStates
{
    Wait,
    Idle,
    NormalSlash,
    QuickSlash,
    Combo,
    Dodge,
    ChargeSlash,
    KanPo,
    IaiSlash,
    ThrustSlash,
    Exhausted
}

public class EnemyController : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    //  MIGRATION NOTICE: 战斗逻辑已迁移到 EnemyCombat
    //  属性/方法通过转发器代理到 combat 组件
    // ══════════════════════════════════════════════════════

    // ── 使用 EnemyCombat 替代 ──
    [HideInInspector] public EnemyCombat combat;

    // ═══════════════ 属性转发 → EnemyCombat ═══════════════
    public int currentAttackDamage   { get => combat.currentAttackDamage;   set => combat.currentAttackDamage = value; }
    public bool pendingExhaustion    { get => combat.pendingExhaustion;    set => combat.pendingExhaustion = value; }
    public GameObject weaponModel    { get => combat.weaponModel;          set => combat.weaponModel = value; }
    public GameObject weaponHitBox   { get => combat.weaponHitBox;         set => combat.weaponHitBox = value; }
    public GameObject sheathModel    { get => combat.sheathModel;          set => combat.sheathModel = value; }
    public Transform sheathPoint     { get => combat.sheathPoint;          set => combat.sheathPoint = value; }
    public Transform handBone        { get => combat.handBone;             set => combat.handBone = value; }
    public bool shouldAbortAttack    { get => combat.shouldAbortAttack;    set => combat.shouldAbortAttack = value; }
    public bool HasSuperArmor        { get => combat.HasSuperArmor;        set => combat.HasSuperArmor = value; }
    public bool canHitThisAttack     { get => combat.canHitThisAttack;     set => combat.canHitThisAttack = value; }
    public bool isParryAnimating     { get => combat.isParryAnimating;     set => combat.isParryAnimating = value; }
    public bool lockWeaponInHand     { get => combat.lockWeaponInHand;     set => combat.lockWeaponInHand = value; }
    public bool isPostureBreakExhaust{ get => combat.isPostureBreakExhaust;set => combat.isPostureBreakExhaust = value; }
    public bool iaiAwakened          { get => combat.iaiAwakened;          set => combat.iaiAwakened = value; }
    public bool iaiUsed              { get => combat.iaiUsed;              set => combat.iaiUsed = value; }
    public bool isInComboChain       { get => combat.isInComboChain;       set => combat.isInComboChain = value; }
    public bool comboInterrupted     { get => combat.comboInterrupted;     set => combat.comboInterrupted = value; }
    public bool lastDerivedMoveHitPlayer { get => combat.lastDerivedMoveHitPlayer; set => combat.lastDerivedMoveHitPlayer = value; }
    public int  comboChainCount      { get => combat.comboChainCount;      set => combat.comboChainCount = value; }
    public int  derivedMoveCount     { get => combat.derivedMoveCount;     set => combat.derivedMoveCount = value; }
    public int  consecutiveHits      { get => combat.consecutiveHits;      set => combat.consecutiveHits = value; }
    public int  interruptedAttackCount{get => combat.interruptedAttackCount;set=> combat.interruptedAttackCount = value; }
    public float damageCooldown      { get => combat.damageCooldown;       set => combat.damageCooldown = value; }
    public float lastDamageTime      { get => combat.lastDamageTime;       set => combat.lastDamageTime = value; }
    public float decisionSpeedMultiplier { get => combat.decisionSpeedMultiplier; set => combat.decisionSpeedMultiplier = value; }
    private BossComboChain comboChain;
    public float frequencyMultiplier { get => combat.frequencyMultiplier;  set => combat.frequencyMultiplier = value; }
    public float moveSpeed = 3.5f;
    public float rotationSpeed = 10f;

    public float idleDistance = 4f;
    public float chaseStopDistance = 3.5f;

    public float idleDecisionInterval = 1.2f;
    public float lastDecisionTime { get; set; } = 0f;

    [HideInInspector] public Animator anim;
    private GameObject player;
    private PlayerActionPredictor actionPredictor;
    public StateMachine<EnemyController> StateMachine { get; private set; }
    private Dictionary<EnemyStates, State<EnemyController>> stateDict;

    [Header("跑步停止")]
    [SerializeField] private float runStopAnimLength = 0.5f;
    private bool freezeMovement = false;
    private bool isPlayingRunStop = false;

    public float lockedY;
    private bool isDead = false;

    private BossPhaseManager bossPhaseManager;
    private BossDecisionEngine decisionEngine;

    [Header("重力设置")]
    [SerializeField] private float gravityForce = -15f;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private Vector3 groundCheckOffset;
    [SerializeField] private LayerMask groundLayer;

    [Header("开战/观战抵达")]
    public bool waitForPlayerEngage = true;
    public float engageDistance = 30f;
    private bool openingSequenceDone = false;
    private Coroutine openingSequenceCoroutine;

    [Header("二阶段出场待命")]
    public bool waitForPhaseTwoEngage = false;   // 出现后待命，发现玩家播放 Buff（类似一阶段 ToFight）
    public bool lockFacing = false;              // 锁定朝向（Roll 等位移动画期间防止 Idle FacePlayer 转动）
    private bool phaseTwoEngageStarted = false;  // 防止 Buff 出场协程重复触发
    private bool phaseTransitionInternal = false; // 转场协程内部放行标志（仅 TransitionChangeState 使用）

    private float verticalSpeed = 0f;
    private bool enableAutoGround = true;
    private float attackStateEnterTime = 0f;
    private const float attackStateMaxDuration = 5f;

    // 弹刀冻结：锁期间Update顶层暴力拦截
    private float parryFreezeUntil = 0f;

    // 收刀阶段决策结果：下一招应该打什么
    public EnemyStates? nextMoveAfterSheath = null;

    // 近距离Dodge: 每0.5秒检查，太近则随机方向Dodge
    private float lastProximityCheck = 0f;
    private const float PROXIMITY_INTERVAL = 0.5f;
    private const float CLOSE_DISTANCE = 1.5f;
    private const float CLOSE_PERSIST_TIME = 0.5f;

    // 统一 Dodge 冷却
    [HideInInspector] public float lastDodgeTime = -10f;
    [Header("Dodge 统一冷却")]
    [SerializeField] private float dodgeGlobalCooldown = 4f;

    // 追击惩罚
    private float dodgeEndTime = -10f;
    [Header("追击惩罚")]
    [SerializeField] private float pursuitCheckWindow = 0.5f;
    [SerializeField][Range(0f, 1f)] private float pursuitCounterChance = 0.6f;

    [Header("攻击性")]
    [SerializeField] public float maxAggression = 100f;
    [SerializeField] public float aggressionRegenRate = 15f;
    [SerializeField] public float farAggressionRegenRate = 5f;
    [SerializeField] public float aggressionGainOnHit = 20f;
    [SerializeField] public float closeRangeForAggression = 4f;
    [HideInInspector] public float currentAggression = 0f;
    [HideInInspector] public bool isExecutionFrozen = false;
    [HideInInspector] public bool isDodging = false;
    [HideInInspector] public float lastAttackEndTime = -10f;
    public float noDodgeUntil = -10f;
    private float closeStartTime = -1f;
    private const int HIT_THRESHOLD_FOR_DODGE = 3;

    // 防止连续进入同一攻击状态
    private EnemyStates lastState = EnemyStates.Idle;
    private EnemyStates lastAttack = EnemyStates.Idle;

    private void Awake()
    {
        combat = GetComponent<EnemyCombat>();
    }

    private void Start()
    {
        combat?.Init(this);
        anim = GetComponent<Animator>();
        if (anim == null) { }
        player = GameObject.FindGameObjectWithTag("Player");
        bossPhaseManager = GetComponent<BossPhaseManager>();
        decisionEngine = GetComponent<BossDecisionEngine>();
        comboChain = GetComponent<BossComboChain>();
        actionPredictor = GetComponent<PlayerActionPredictor>();

        stateDict = new Dictionary<EnemyStates, State<EnemyController>>();

        void TryAddState(EnemyStates key, State<EnemyController> state)
        {
            if (state != null)
                stateDict[key] = state;
        }

        TryAddState(EnemyStates.Wait, GetComponent<WaitState>());
        TryAddState(EnemyStates.Idle, GetComponent<IdleState>());
        TryAddState(EnemyStates.Dodge, GetComponent<DodgeState>());
        TryAddState(EnemyStates.NormalSlash, GetComponent<NormalSlashState>());
        TryAddState(EnemyStates.QuickSlash, GetComponent<QuickSlashState>());
        TryAddState(EnemyStates.Combo, GetComponent<ComboState>());
        TryAddState(EnemyStates.ChargeSlash, GetComponent<ChargeSlashState>());
        TryAddState(EnemyStates.KanPo, GetComponent<SlashState>());
        TryAddState(EnemyStates.IaiSlash, GetComponent<IaiSlashState>());
        TryAddState(EnemyStates.ThrustSlash, GetComponent<ThrustSlashState>());
        TryAddState(EnemyStates.Exhausted, GetComponent<ExhaustedState>());

        StateMachine = new StateMachine<EnemyController>(this);
        StateMachine.ChangeState(stateDict[EnemyStates.Wait]);

        // ── 订阅战斗事件 ──
        BossEvents events = GetComponent<BossEvents>();
        if (events != null)
        {
            events.OnAttackFinished += () =>
            {
                // 攻击中体力耗尽（pendingExhaustion）→ 攻击结束立即进入力竭，优先级高于链衔接
                if (pendingExhaustion)
                {
                    pendingExhaustion = false;
                    ChangeState(EnemyStates.Exhausted);
                    return;
                }
                if (comboChain != null && comboChain.OnAttackFinished()) return;   // 链队列已出下一招，不回 Idle
                ChangeState(EnemyStates.Idle);
            };
            events.OnAttackInterrupted += () =>
            {
                // 攻击被打断但体力已耗尽 → 同样进入力竭
                if (pendingExhaustion)
                {
                    pendingExhaustion = false;
                    ChangeState(EnemyStates.Exhausted);
                    return;
                }
                ChangeState(EnemyStates.Idle);
            };
            events.OnRecoveredFromHit += () =>
            {
                ChangeState(EnemyStates.Idle);
                decisionEngine?.LockDecision(0.5f);
            };
        }

        lockedY = transform.position.y;
        currentAttackDamage = 0;
        EnableWeaponHitBox(false, false);
        AttachWeaponToSheath();
    }

    private void Update()
    {
        if (isExecutionFrozen)
        {
            if (anim != null) anim.speed = 0f;
            return;
        }

        // 弹刀冻结
        if (Time.time < parryFreezeUntil)
        {
            if (anim != null)
            {
                anim.speed = 1f;
                int al = anim.GetLayerIndex("Attack Layer");
                if (al >= 0) anim.SetLayerWeight(al, 0f);
                anim.Play("Idle", 0, 0f);
            }
            EnableWeaponHitBox(false, false);
            return;
        }

        // 派生链打断后摇：锁定行动（力竭动画后摇中禁止出招/闪避）
        if (comboChain != null && comboChain.IsInBreakRecovery) return;

        if (player == null || StateMachine == null || combat == null) return;

        // ★ 转场期间（Block_Hit→Roll→隐藏→出现）屏蔽一切 AI（Dodge/反击/决策/调试键）与重力，
        //   确保转场动画完整播放、不被任何状态切换覆盖；仅保持状态机执行（Idle FacePlayer，Roll 期间 lockFacing 已锁）
        if (bossPhaseManager != null && bossPhaseManager.IsInPhaseTransition)
        {
            StateMachine.Execute();
            return;
        }

        // 调试快捷键
        if (Input.GetKeyDown(KeyCode.T)) { ChangeState(EnemyStates.NormalSlash); return; }
        if (Input.GetKeyDown(KeyCode.O)) { ChangeState(EnemyStates.QuickSlash); return; }
        if (Input.GetKeyDown(KeyCode.I)) { ChangeState(EnemyStates.Combo); return; }
        if (Input.GetKeyDown(KeyCode.P)) { ChangeState(EnemyStates.ChargeSlash); return; }
        if (Input.GetKeyDown(KeyCode.U)) { ChangeState(EnemyStates.KanPo); return; }
        if (Input.GetKeyDown(KeyCode.Y)) { ChangeState(EnemyStates.IaiSlash); return; }
        if (Input.GetKeyDown(KeyCode.E)) { ClearPostureForDebug(); return; }
        if (Input.GetKeyDown(KeyCode.H)) { ChangeState(EnemyStates.ThrustSlash); return; }

        // 武器碰撞安全检查
        if (weaponHitBox != null && weaponHitBox.activeSelf)
        {
            bool isAttackState = StateMachine.CurrentState is NormalSlashState ||
                                 StateMachine.CurrentState is QuickSlashState ||
                                 StateMachine.CurrentState is ComboState ||
                                 StateMachine.CurrentState is ChargeSlashState ||
                                 StateMachine.CurrentState is SlashState ||
                                 StateMachine.CurrentState is IaiSlashState ||
                                 StateMachine.CurrentState is ThrustSlashState;

            if (!isAttackState)
            {
                Debug.LogWarning($"非法状态({StateMachine.CurrentState.GetType().Name})强制关闭武器碰撞");
                weaponHitBox.SetActive(false);
            }
        }

        // 开战/观战抵达
        if (waitForPlayerEngage && player != null)
        {
            if (DistanceToPlayer() <= engageDistance)
            {
                waitForPlayerEngage = false;
                openingSequenceCoroutine = StartCoroutine(OpeningSequence());
            }
        }
        // 二阶段出场待命：发现玩家前与 Buff 播放期间禁止出招（决策/闪避/反击），但保持面向玩家
        if (waitForPhaseTwoEngage)
        {
            if (player != null && DistanceToPlayer() <= engageDistance && !phaseTwoEngageStarted)
            {
                phaseTwoEngageStarted = true;
                StartCoroutine(PhaseTwoEngageSequence());
            }
            StateMachine.Execute();
            return;
        }

        // 攻击性自然恢复
        if (currentAggression < maxAggression)
        {
            float dist = DistanceToPlayer();
            float regenRate = dist <= closeRangeForAggression ? aggressionRegenRate : farAggressionRegenRate;
            currentAggression += regenRate * Time.deltaTime;
            if (currentAggression > maxAggression) currentAggression = maxAggression;
        }

        // 条件1：近距离自动Dodge
        bool canAct = !isDodging
            && !(StateMachine.CurrentState is DodgeState)
            && !IsAttackState(StateMachine.CurrentState)
            && Time.time >= noDodgeUntil
            && Time.time - lastDodgeTime >= dodgeGlobalCooldown
            && Time.time - lastAttackEndTime > 0.5f;

        if (canAct && DistanceToPlayer() < CLOSE_DISTANCE)
        {
            if (closeStartTime < 0f)
                closeStartTime = Time.time;
            else if (Time.time - closeStartTime >= CLOSE_PERSIST_TIME)
            {
                closeStartTime = -1f;
                consecutiveHits = 0;
                lastDodgeTime = Time.time;
                ChangeState(EnemyStates.Dodge);
                return;
            }
        }
        else
        {
            closeStartTime = -1f;
        }

        // 条件2：连续3次攻击被打断 → 触发Dodge
        if (canAct && interruptedAttackCount >= 3)
        {
            interruptedAttackCount = 0;
            consecutiveHits = 0;
            lastDodgeTime = Time.time;
            ChangeState(EnemyStates.Dodge);
            return;
        }

        // 条件3：连续被击中N次 → 强制Dodge
        if (consecutiveHits >= HIT_THRESHOLD_FOR_DODGE
            && !isDodging
            && !(StateMachine.CurrentState is DodgeState)
            && !IsAttackState(StateMachine.CurrentState)
            && Time.time - lastAttackEndTime > 0.5f)
        {
            consecutiveHits = 0;
            lastDodgeTime = Time.time;
            ChangeState(EnemyStates.Dodge);
            return;
        }

        // 追击惩罚：Boss Dodge 后玩家追击则概率反击
        if (!isDodging
            && Time.time - dodgeEndTime < pursuitCheckWindow
            && Time.time - dodgeEndTime > 0.05f
            && StateMachine.CurrentState is IdleState
            && IsPlayerChasing())
        {
            dodgeEndTime = -10f;
            if (Random.value < pursuitCounterChance)
            {
                EnemyStates counterMove = GetPursuitCounterMove();
                ChangeState(counterMove);
                return;
            }
        }

        // 决策引擎
        if (StateMachine.CurrentState is IdleState && decisionEngine != null)
        {
            EnemyStates? decidedMove = decisionEngine.Tick(Time.deltaTime);
            if (decidedMove != null)
            {
                ChangeState(decidedMove.Value);
                return;
            }
        }

        StateMachine.Execute();

        // 重力
        if (enableAutoGround && !isDead)
        {
            bool shouldApplyGravity = StateMachine.CurrentState is IdleState ||
                                      StateMachine.CurrentState is ExhaustedState;
            if (shouldApplyGravity)
                ApplyGravityAndGround();
        }

        if (weaponModel != null && !weaponModel.activeSelf)
            weaponModel.SetActive(true);

        // 攻击状态超时保护
        if (IsAttackState(StateMachine.CurrentState))
        {
            if (Time.time - attackStateEnterTime > attackStateMaxDuration)
            {
                Debug.LogWarning($"攻击状态超时，强制切回Idle: {StateMachine.CurrentState.GetType().Name}");
                combat.StopCurrentAttack();
                EnableWeaponHitBox(false, false);
                DisableAttackLayer();
                ChangeState(EnemyStates.Idle);
            }
        }
    }

    // ==================== 状态辅助 ====================
    private bool IsAttackStateByEnum(EnemyStates s)
    {
        return s == EnemyStates.NormalSlash || s == EnemyStates.QuickSlash || s == EnemyStates.Combo
            || s == EnemyStates.ChargeSlash || s == EnemyStates.KanPo || s == EnemyStates.IaiSlash
            || s == EnemyStates.ThrustSlash;
    }

    private bool IsAttackState(State<EnemyController> state)
    {
        return state is NormalSlashState ||
               state is QuickSlashState ||
               state is ComboState ||
               state is ChargeSlashState ||
               state is SlashState ||
               state is IaiSlashState ||
               state is ThrustSlashState;
    }

    public void ChangeState(EnemyStates state)
    {
        // ★ 转场期间（PhaseTwoRetreat 全程）禁止任何外部状态切换（受击恢复/Dodge/力竭/反击等），
        //   确保 Block_Hit→Roll→隐藏→出现 流程完整播放；转场协程自身的切换必须走 TransitionChangeState
        if (bossPhaseManager != null && bossPhaseManager.IsInPhaseTransition && !phaseTransitionInternal)
            return;

        if (isExecutionFrozen && state != EnemyStates.Idle && state != EnemyStates.Exhausted)
            return;

        if (state == lastState && (IsAttackStateByEnum(state) || state == EnemyStates.Dodge))
            return;

        if (StateMachine == null || stateDict == null) return;
        if (!stateDict.ContainsKey(state)) return;

        State<EnemyController> newState = stateDict[state];
        if (newState == null) return;

        if (IsAttackStateByEnum(state) || state == EnemyStates.Dodge)
        {
            lastState = state;
            lastAttack = state;
        }
        else if (state == EnemyStates.Idle)
            lastState = EnemyStates.Idle;

        if (StateMachine.CurrentState != null)
        {
            foreach (var kvp in stateDict)
            {
                if (kvp.Value == StateMachine.CurrentState && IsAttackState(kvp.Value))
                {
                    lastAttack = kvp.Key;
                    break;
                }
            }
        }

        StateMachine.ChangeState(newState);

        if (IsAttackState(newState))
        {
            attackStateEnterTime = Time.time;
            EnableAttackLayer();
        }
    }

    /// <summary>
    /// 转场协程专用状态切换：临时放行"转场期间禁止外部切换"守卫。
    /// 仅供 BossPhaseManager 的转场/出场流程调用（PhaseTwoRetreat / PostSpawnSequence）。
    /// </summary>
    public void TransitionChangeState(EnemyStates state)
    {
        phaseTransitionInternal = true;
        ChangeState(state);
        phaseTransitionInternal = false;
    }

    public float DistanceToPlayer()
    {
        if (player == null) return 999f;
        return Vector3.Distance(transform.position, player.transform.position);
    }

    public void FacePlayer()
    {
        if (player == null) return;
        Vector3 dir = (player.transform.position - transform.position).normalized;
        dir.y = 0;
        if (dir.magnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    public Vector3 GetPlayerPosition()
    {
        if (player != null) return player.transform.position;
        return transform.position + transform.forward * 3f;
    }

    public Vector3 GetPlayerForward()
    {
        if (player != null)
        {
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null) return pc.transform.forward;
            return player.transform.forward;
        }
        return Vector3.forward;
    }

    public void FaceTarget(Vector3 targetPos)
    {
        Vector3 dir = (targetPos - transform.position).normalized;
        dir.y = 0;
        if (dir.magnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    // ==================== combat forwarders ====================
    public void EnableWeaponHitBox(bool enable, bool unblockable) => combat.EnableWeaponHitBox(enable, unblockable);
    public void AttachWeaponToHand() => combat.AttachWeaponToHand();
    public void AttachWeaponToSheath() => combat.AttachWeaponToSheath();
    public void ForceWeaponToSheath() => combat.ForceWeaponToSheath();
    public void PlayHitReaction(Vector3 hitDir, bool isHeavy = false, float hitStop = 0.1f) => combat.PlayHitReaction(hitDir, isHeavy, hitStop);
    public void PlayParryReaction(Vector3 dir) => combat.PlayParryReaction(dir);
    public void PlayGuardBreakReaction(Vector3 dir) => combat.PlayGuardBreakReaction(dir);
    public void ForceStopAllAttacks() => combat.ForceStopAllAttacks();
    public void RegisterAttackRoutine(Coroutine r) => combat.RegisterAttackRoutine(r);
    public void StopCurrentAttack() => combat.StopCurrentAttack();
    public void OnAttackFinished() => combat.OnAttackFinished();
    public void MarkAttackHit(bool hit) => combat.MarkAttackHit(hit);
    public void EndComboChain() => combat.EndComboChain();
    public bool ShouldContinueChain() => combat.ShouldContinueChain();
    public void DisableAttackLayer() => combat.DisableAttackLayer();
    public void EnableAttackLayer() => combat.EnableAttackLayer();
    public void HideAllWeaponModels() => combat.HideAllWeaponModels();
    public void ShowAllWeaponModels() => combat.ShowAllWeaponModels();

    // ==================== 追击惩罚 ====================
    private bool IsPlayerChasing()
    {
        if (player == null) return false;
        PlayerController pc = player.GetComponent<PlayerController>();
        if (pc == null || pc.IsInvincible) return false;

        Vector3 dirToBoss = (transform.position - player.transform.position).normalized;
        dirToBoss.y = 0;
        Vector3 playerVel = player.GetComponent<CharacterController>()?.velocity ?? Vector3.zero;
        playerVel.y = 0;

        if (playerVel.magnitude > 2f)
        {
            float dot = Vector3.Dot(playerVel.normalized, dirToBoss);
            return dot > 0.3f;
        }
        return false;
    }

    private EnemyStates GetPursuitCounterMove()
    {
        if (bossPhaseManager == null) return EnemyStates.NormalSlash;
        return bossPhaseManager.CurrentPhase switch
        {
            BossPhaseManager.BossPhase.PhaseTwoBurst => EnemyStates.QuickSlash,
            BossPhaseManager.BossPhase.PhaseThreeMaster => EnemyStates.ChargeSlash,
            _ => EnemyStates.NormalSlash
        };
    }

    public void OnDodgeEnded()
    {
        dodgeEndTime = Time.time;
    }

    public void AddAggression(float amount)
    {
        currentAggression = Mathf.Min(maxAggression, currentAggression + amount);
    }

    // ==================== 体力/架势 ====================
    public void OnStaminaDepleted()
    {
        if (IsAttackState(StateMachine.CurrentState))
            pendingExhaustion = true;
        else
            ChangeState(EnemyStates.Exhausted);
    }

    public void OnPostureBroken()
    {
        isPostureBreakExhaust = true;
        ChangeState(EnemyStates.Exhausted);
    }

    // ==================== 跑步停止动画 ====================
    public void PlayRunStopThenIdle()
    {
        if (isPlayingRunStop) return;
        isPlayingRunStop = true;
        anim.SetTrigger("StopRun");
        StartCoroutine(WaitRunStopEnd());
    }

    IEnumerator WaitRunStopEnd()
    {
        yield return new WaitForSeconds(runStopAnimLength);
        isPlayingRunStop = false;
        freezeMovement = false;
        ChangeState(EnemyStates.Idle);
    }

    // ==================== 调试 ====================
    public void ClearPostureForDebug()
    {
        BossPosture posture = GetComponent<BossPosture>();
        if (posture != null)
        {
            posture.CurrentPosture = 0f;
            posture.OnPerfectBlocked(0f);
        }
    }

    public void ForceIdleDecision()
    {
        // 仅占位
    }

    public void EnableDeath()
    {
        isDead = true;
        StopAllCoroutines();
        EnableWeaponHitBox(false, false);
        enabled = false;
    }

    public bool EnableAutoGround
    {
        get => enableAutoGround;
        set => enableAutoGround = value;
    }

    private void ApplyGravityAndGround()
    {
        if (!enableAutoGround) return;

        Vector3 checkPoint = transform.TransformPoint(groundCheckOffset);
        bool isGrounded = Physics.CheckSphere(checkPoint, groundCheckRadius, groundLayer);

        if (isGrounded && verticalSpeed < 0f)
            verticalSpeed = -0.5f;
        else
            verticalSpeed += gravityForce * Time.deltaTime;

        transform.position += new Vector3(0f, verticalSpeed, 0f) * Time.deltaTime;

        if (isGrounded && verticalSpeed < 0f)
        {
            verticalSpeed = 0f;
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 5f, groundLayer))
            {
                Vector3 pos = transform.position;
                pos.y = hit.point.y;
                transform.position = pos;
            }
        }
    }

    public void ForceClearAndPlayBuff(System.Action onComplete = null)
    {
        StopAllCoroutines();
        EnableWeaponHitBox(false, false);
        DisableAttackLayer();
        anim.SetBool("isExhausted", false);
        anim.ResetTrigger("Block_Hit");
        anim.ResetTrigger("buff");
        ChangeState(EnemyStates.Idle);

        anim.applyRootMotion = false;
        Vector3 lockedPos = transform.position;
        Quaternion lockedRot = transform.rotation;

        anim.Play("Buff", 0, 0f);
        StartCoroutine(WaitForBuffEndWithFreeze(lockedPos, lockedRot, onComplete));
    }

    private IEnumerator WaitForBuffEndWithFreeze(Vector3 lockedPos, Quaternion lockedRot, System.Action onComplete)
    {
        yield return null;
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        float length = state.IsName("Buff") ? state.length : 2.5f;
        float elapsed = 0f;

        while (elapsed < length)
        {
            elapsed += Time.deltaTime;
            transform.position = lockedPos;
            transform.rotation = lockedRot;
            yield return null;
        }

        anim.applyRootMotion = true;
        EnableAttackLayer();
        onComplete?.Invoke();
    }

    public void PlayBuffAnimationDirect(EnemyStates callbackState, System.Action onComplete = null)
    {
        StartCoroutine(BuffRoutineDirect(callbackState, onComplete));
    }

    private IEnumerator BuffRoutineDirect(EnemyStates callbackState, System.Action onComplete)
    {
        DisableAttackLayer();
        anim.SetBool("isIai", false);
        anim.Play("Buff", 0, 0f);

        yield return null;
        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        float length = stateInfo.IsName("Buff") ? stateInfo.length : 2.5f;
        yield return new WaitForSeconds(length);

        EnableAttackLayer();
        ChangeState(callbackState);
        onComplete?.Invoke();
    }

    public void TriggerAwakenedIai()
    {
        PlayBuffAnimationDirect(EnemyStates.IaiSlash);
    }

    public void OnHitDuringMikiri()
    {
        anim.SetBool("isHit", true);
        StartCoroutine(ExitMikiriAfterHit());
    }

    private IEnumerator ExitMikiriAfterHit()
    {
        yield return new WaitForSeconds(0.8f);
        anim.SetBool("isHit", false);
        ChangeState(EnemyStates.Idle);
    }

    // ==================== Gizmos ====================
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(groundCheckOffset), groundCheckRadius);
    }

    // ==================== distance dodge ====================
    private IEnumerator DodgeBackRoutine(string animName)
    {
        if (StateMachine.CurrentState is ExhaustedState || isExecutionFrozen)
            yield break;

        EnableWeaponHitBox(false, false);

        anim.applyRootMotion = true;
        anim.Play(animName, 0, 0f);
        anim.Update(0f);

        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        float animLength = state.IsName(animName) ? state.length : 0.4f;
        yield return new WaitForSeconds(animLength);

        anim.applyRootMotion = false;
    }

    // ==================== 开场 ====================

    /// <summary>
    /// 终止开场流程（转场/狂暴时必须调用）：
    /// 1. 停止 OpeningSequence 协程，防止 ToFight 跨转场播完并强行 ChangeState(ThrustSlash)
    /// 2. 兜底禁用决策引擎"首招保证"，防止协程被杀后 firstMoveGuaranteed 残留导致狂暴后强制突刺
    /// </summary>
    public void CancelOpeningSequence()
    {
        if (openingSequenceCoroutine != null)
        {
            StopCoroutine(openingSequenceCoroutine);
            openingSequenceCoroutine = null;
        }
        BossDecisionEngine de = GetComponent<BossDecisionEngine>();
        if (de != null) de.DisableFirstMoveGuarantee();
    }

    IEnumerator OpeningSequence()
    {
        openingSequenceDone = true;
        lockWeaponInHand = true;
        AttachWeaponToHand();

        // 恢复开场拔刀动画：先播 ToFight，播完后再进入突刺
        int attackLayer = anim.GetLayerIndex("Attack Layer");
        if (attackLayer == -1) attackLayer = 0;
        anim.SetLayerWeight(attackLayer, 1f);
        anim.Play("ToFight", attackLayer, 0f);
        anim.Update(0f);
        yield return null;

        while (anim.GetCurrentAnimatorStateInfo(attackLayer).normalizedTime < 1f)
            yield return null;

        ChangeState(EnemyStates.ThrustSlash);

        BossDecisionEngine de = GetComponent<BossDecisionEngine>();
        if (de != null) de.DisableFirstMoveGuarantee();
    }

    IEnumerator PhaseTwoEngageSequence()
    {
        // 发现玩家：播放 Buff 出场动画（内部含锁位），播完自动回 Idle 由决策引擎接管。
        // 注意：ForceClearAndPlayBuff 内部会 StopAllCoroutines，本协程会被自己终止，属正常。
        ForceClearAndPlayBuff(() =>
        {
            waitForPhaseTwoEngage = false;   // Buff 播完，解除出场待命
            phaseTwoEngageStarted = false;
        });
        yield break;
    }
}
