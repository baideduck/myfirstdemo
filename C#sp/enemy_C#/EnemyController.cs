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
    [HideInInspector] public int currentAttackDamage;
    [HideInInspector] public bool pendingExhaustion = false;

    [Header("锟斤拷锟斤拷锟借定")]
    public float moveSpeed = 3.5f;
    public float rotationSpeed = 10f;

    [Header("锟斤拷锟斤拷锟揭碉拷")]
    public Transform sheathPoint;
    public Transform handBone;
    public GameObject weaponModel;

    [Header("锟斤拷锟斤拷锟借定")]
    public float idleDistance = 4f;
    public float chaseStopDistance = 3.5f;

    [Header("锟斤拷时")]
    public float idleDecisionInterval = 1.2f;
    public float lastDecisionTime { get; set; } = 0f;

    [Header("锟斤拷锟斤拷锟斤拷撞锟斤拷")]
    public GameObject weaponHitBox;

    // 锟斤拷锟斤拷锟斤拷锟?
    [HideInInspector] public Animator anim;
    private GameObject player;
    private Coroutine hitReactionCoroutine;
    private float lastHitStopDuration;
    private PlayerActionPredictor actionPredictor;   // 二元预测模块
    [HideInInspector] public int derivedMoveCount = 0;   // 派生计数（不计抽奖起手）
    [HideInInspector] public bool lastDerivedMoveHitPlayer = false; // 上一招是否命中   // 当前受击的顿帧时长
    public StateMachine<EnemyController> StateMachine { get; private set; }
    private Dictionary<EnemyStates, State<EnemyController>> stateDict;

    [Header("锟斤拷停锟斤拷锟斤拷")]
    [SerializeField] private float runStopAnimLength = 0.5f;
    private bool freezeMovement = false;
    private bool isPlayingRunStop = false;

    private Coroutine currentAttackRoutine;
    public float lockedY;
    private bool isDead = false;

    [Header("AI 锟介动锟斤拷(锟斤拷锟斤拷锟斤拷锟斤拷庸锟?")]
    [HideInInspector] public float decisionSpeedMultiplier = 1.0f;
    [HideInInspector] public float frequencyMultiplier = 1.0f;
    [HideInInspector] public bool shouldAbortAttack = false;

    // 锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷
    [HideInInspector] public int comboChainCount = 0;
    [HideInInspector] public bool isInComboChain = false;
    [HideInInspector] public bool comboInterrupted = false;

    private EnemyStates lastAttack;

    // 锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锥喂锟斤拷锟斤拷锟斤拷锟?
    private BossPhaseManager bossPhaseManager;
    private BossDecisionEngine decisionEngine;

    [Header("锟接合撅拷锟斤拷")]
    public bool iaiAwakened = false;
    public bool iaiUsed = false;

    [Header("锟斤拷锟斤拷")]
    public bool HasSuperArmor = false;

    [Header("锟斤拷锟斤拷锟斤拷锟斤拷")]
    [SerializeField] private float gravityForce = -15f;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private Vector3 groundCheckOffset;
    [SerializeField] private LayerMask groundLayer;

    [Header("锟斤拷锟斤拷/锟斤拷锟斤拷锟饺达拷")]
    public bool waitForPlayerEngage = true;
    public float engageDistance = 30f;
    private bool openingSequenceDone = false;

    [Header("锟斤拷锟斤拷模锟斤拷")]
    public GameObject sheathModel;

    private float verticalSpeed = 0f;
    private bool enableAutoGround = true;

    [Header("锟剿猴拷锟斤拷却")]
    public float damageCooldown = 0.2f;
    [HideInInspector] public float lastDamageTime = -1f;

    // 姣忔鏀诲嚮鐨勫崟娆′激瀹抽攣锛氬懡涓垨鏍兼尅鍚庣疆false锛屼笅娆℃敾鍑?涓嬩釜绐楀彛澶嶄綅
    [HideInInspector] public bool canHitThisAttack = true;

    [HideInInspector] public bool isParryAnimating = false;
    private float attackStateEnterTime = 0f;
    private const float attackStateMaxDuration = 5f;

    // 寮瑰垁鍐荤粨锛氶攣鏈熼棿Update椤跺眰鏆村姏鎷︽埅
    private float parryFreezeUntil = 0f;

    // 鏀跺垁闃舵鍐崇瓥缁撴灉锛氫笅涓€鎷涘簲璇ユ墦浠€涔?
    public EnemyStates? nextMoveAfterSheath = null;

    // 鏀跺垁鎱㈡斁鍊嶇巼锛屾毚闇插湪Inspector涓皟鑺?
    [Header("鏀跺垁鎱㈡斁鍊嶇巼")]
    public float sheathSlowMoRate = 0.3f;

    // 鍒€榛樿閿佸湪鎵嬩笂锛屽彧鏈夊姏绔?鎹㈠尯鎵嶅洖闉橈紙鎴樻枟鍓嶄负false锛岄鍒€鍚庡彉true锛?
    public bool weaponLockedInHand = false;

    // 距离检测：每0.5秒检查，太近则随机方向Dodge
    private float lastProximityCheck = 0f;
    private const float PROXIMITY_INTERVAL = 0.5f;
    private const float CLOSE_DISTANCE = 1.5f;    // 近距离阈值：1.5米
    private const float CLOSE_PERSIST_TIME = 0.5f;   // 保持0.5秒就触发

    // 统一 Dodge 冷却（供接近检测和决策池共用）
    [HideInInspector] public float lastDodgeTime = -10f;
    [Header("Dodge 统一冷却")]
    [SerializeField] private float dodgeGlobalCooldown = 4f;

    // 追击惩罚
    private float dodgeEndTime = -10f;
    [Header("追击惩罚")]
    [SerializeField] private float pursuitCheckWindow = 0.5f;
    [SerializeField][Range(0f, 1f)] private float pursuitCounterChance = 0.6f;

    [Header("锟斤拷锟斤拷锟斤拷锟斤拷")]
    [SerializeField] public float maxAggression = 100f;
    [SerializeField] public float aggressionRegenRate = 15f;
    [SerializeField] public float farAggressionRegenRate = 5f;
    [SerializeField] public float aggressionGainOnHit = 20f;
    [SerializeField] public float closeRangeForAggression = 4f;
    [HideInInspector] public float currentAggression = 0f;
    [HideInInspector] public bool isExecutionFrozen = false;
    [HideInInspector] public bool isDodging = false;  // DodgeState 控制，期间禁止决策
    [HideInInspector] public float lastAttackEndTime = -10f;  // 攻击结束时间，用于Dodge冷却
    public float noDodgeUntil = -10f;  // 硬性Dodge冷却截止时间（秒）
    private float closeStartTime = -1f;  // 玩家开始进入近距范围的时间
    private int interruptedAttackCount = 0;  // 条件2：连续被打断次数

    // 连续受击计数器：每次 PlayHitReaction 递增，达到阈值触发强制 Dodge
    [HideInInspector] public int consecutiveHits = 0;
    private const int HIT_THRESHOLD_FOR_DODGE = 3;

    // 武器碰撞体膨胀缓存
    private Vector3? hitboxOriginalSize = null;
    private float? hitboxOriginalRadius = null;
    private float? hitboxOriginalHeight = null;
    private bool hitboxInflated = false;

    // 锟街碉拷锟斤拷锟斤拷锟斤拷锟斤拷时锟斤拷锟斤拷锟斤拷锟杰癸拷锟斤拷
    [HideInInspector] public bool lockWeaponInHand = false;

    // 标识此次气绝是由架势清空触发的（而非体力耗尽）
    [HideInInspector] public bool isPostureBreakExhaust = false;

    private void Start()
    {
        anim = GetComponent<Animator>();
        if (anim == null) { }
        player = GameObject.FindGameObjectWithTag("Player");
        bossPhaseManager = GetComponent<BossPhaseManager>();
        decisionEngine = GetComponent<BossDecisionEngine>();
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

        // 寮瑰垁鍐荤粨锛氬己鍒嘔dle锛屼笉缁忚繃鐘舵€佹満锛屼笉纰版鍣ㄤ綅缃?
        if (Time.time < parryFreezeUntil)
        {
            if (anim != null)
            {
                anim.speed = 1f;
                anim.SetLayerWeight(anim.GetLayerIndex("Attack Layer"), 0f);
                anim.Play("Idle", 0, 0f);
            }
            EnableWeaponHitBox(false, false);
            return;
        }

        if (player == null || StateMachine == null) return;

        // 锟斤拷锟皆帮拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟矫ｏ拷
        if (Input.GetKeyDown(KeyCode.T)) { ChangeState(EnemyStates.NormalSlash); return; }
        if (Input.GetKeyDown(KeyCode.O)) { ChangeState(EnemyStates.QuickSlash); return; }
        if (Input.GetKeyDown(KeyCode.I)) { ChangeState(EnemyStates.Combo); return; }
        if (Input.GetKeyDown(KeyCode.P)) { ChangeState(EnemyStates.ChargeSlash); return; }
        if (Input.GetKeyDown(KeyCode.U)) { ChangeState(EnemyStates.KanPo); return; }
        if (Input.GetKeyDown(KeyCode.Y)) { ChangeState(EnemyStates.IaiSlash); return; }
        if (Input.GetKeyDown(KeyCode.E)) { ClearPostureForDebug(); return; }
        if (Input.GetKeyDown(KeyCode.H)) { ChangeState(EnemyStates.ThrustSlash); return; }

        // 锟斤拷锟斤拷锟斤拷撞锟斤拷每帧锟斤拷锟斤拷
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
                Debug.LogWarning($"锟角癸拷锟斤拷状态({StateMachine.CurrentState.GetType().Name})锟斤拷强锟狡关憋拷锟斤拷锟斤拷锟斤拷撞锟斤拷");
                weaponHitBox.SetActive(false);
            }
        }

        // 锟斤拷锟斤拷/锟斤拷锟斤拷锟饺达拷
        if (waitForPlayerEngage && player != null)
        {
            if (DistanceToPlayer() <= engageDistance)
            {
                waitForPlayerEngage = false;
                StartCoroutine(OpeningSequence());
            }
        }

        // 锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷然锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷 BossDecisionEngine 锟斤拷锟斤拷锟矫ｏ拷
        if (currentAggression < maxAggression)
        {
            float dist = DistanceToPlayer();
            float regenRate = dist <= closeRangeForAggression ? aggressionRegenRate : farAggressionRegenRate;
            currentAggression += regenRate * Time.deltaTime;
            if (currentAggression > maxAggression) currentAggression = maxAggression;
        }

        // 条件1：近距离自动Dodge（玩家<1.5米且保持0.5秒以上）
        bool canAct = !isDodging
            && !(StateMachine.CurrentState is DodgeState)
            && !IsAttackState(StateMachine.CurrentState)
            && Time.time >= noDodgeUntil
            && Time.time - lastDodgeTime >= dodgeGlobalCooldown
            && Time.time - lastAttackEndTime > 0.5f;  // 攻击结束后等0.5秒才能Dodge

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

        // 条件3：连续被击中 HIT_THRESHOLD_FOR_DODGE 次 → 强制 Dodge
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

        // P1 线性决策：Idle + 无Dodge → 让决策引擎抽奖（Tick 内部自己计时）
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

        // 锟斤拷锟斤拷锟斤拷锟斤拷
        if (enableAutoGround && !isDead)
        {
            bool shouldApplyGravity = StateMachine.CurrentState is IdleState ||
                                      StateMachine.CurrentState is ExhaustedState;
            if (shouldApplyGravity)
                ApplyGravityAndGround();
        }

        if (weaponModel != null && !weaponModel.activeSelf)
            weaponModel.SetActive(true);

        // 锟斤拷锟斤拷状态锟斤拷时锟斤拷锟斤拷
        if (IsAttackState(StateMachine.CurrentState))
        {
            if (Time.time - attackStateEnterTime > attackStateMaxDuration)
            {
                Debug.LogWarning($"锟斤拷锟斤拷状态锟斤拷时锟斤拷强锟斤拷锟叫伙拷Idle: {StateMachine.CurrentState.GetType().Name}");
                if (currentAttackRoutine != null)
                {
                    StopCoroutine(currentAttackRoutine);
                    currentAttackRoutine = null;
                }
                EnableWeaponHitBox(false, false);
                DisableAttackLayer();
                ChangeState(EnemyStates.Idle);
            }
        }
    }

    // 防止连续进入同一状态（如 ChargeSlash → ChargeSlash）
    private EnemyStates lastState = EnemyStates.Idle;

    private bool IsAttackStateByEnum(EnemyStates s)
    {
        return s == EnemyStates.NormalSlash || s == EnemyStates.QuickSlash || s == EnemyStates.Combo
            || s == EnemyStates.ChargeSlash || s == EnemyStates.KanPo || s == EnemyStates.IaiSlash
            || s == EnemyStates.ThrustSlash;
    }

    // ==================== 状态锟斤拷锟?====================
    public void ChangeState(EnemyStates state)
    {
        if (isExecutionFrozen && state != EnemyStates.Idle && state != EnemyStates.Exhausted)
        {
            return;
        }

        // 禁止立刻重复进入同一攻击/Dodge状态
        if (state == lastState && (IsAttackStateByEnum(state) || state == EnemyStates.Dodge))
        {
            return;
        }

        if (StateMachine == null || stateDict == null) return;
        if (!stateDict.ContainsKey(state)) return;

        State<EnemyController> newState = stateDict[state];
        if (newState == null) return;

        // 记录上一个状态（离开时）+ 当前进入的攻击状态
        if (IsAttackStateByEnum(state) || state == EnemyStates.Dodge)
        {
            lastState = state;
            lastAttack = state;  // 同步更新 lastAttack，用于防重复链
        }
        else if (state == EnemyStates.Idle)
            lastState = EnemyStates.Idle;  // 进 Idle 重置，防止阻挡下一轮决策

        // 鍒囨崲鍓嶈褰曚笂娆＄寮€鐨勬敾鍑荤姸鎬侊紙鐢ㄤ簬杩炴嫑姹犲垽鏂級
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
            EnableAttackLayer();  // 纭繚Attack Layer鏉冮噸涓?锛岄槻姝㈠墠娆′腑鏂鑷村姩鐢讳笉鎾?
        }
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

    // ==================== 锟斤拷锟斤拷锟斤拷撞锟斤拷业锟?====================
    public void EnableWeaponHitBox(bool enable, bool unblockable)
    {
        if (weaponHitBox == null) return;

        if (enable)
        {
            if (StateMachine == null || StateMachine.CurrentState == null)
            {
                Debug.LogError("状态锟斤拷为锟秸ｏ拷锟睫凤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷撞锟斤拷");
                return;
            }

            if (!IsAttackState(StateMachine.CurrentState))
            {
                Debug.LogWarning($"锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟杰撅拷锟斤拷锟斤拷前状态 {StateMachine.CurrentState.GetType().Name}");
                return;
            }

            if (weaponModel != null) weaponModel.SetActive(true);
            EnemyWeapon weapon = weaponHitBox.GetComponent<EnemyWeapon>();
            if (weapon != null) weapon.ResetHitState();

            // 只膨胀一次（防止每次开碰撞体都乘 1.5）
            if (!hitboxInflated) InflateHitboxCollider(1.5f);

            // 每次开启武器碰撞体 → 重置单次伤害锁 + 清空跨攻击的 lastDamageTime
            canHitThisAttack = true;
            lastDamageTime = -10f;

            weaponHitBox.SetActive(true);
        }
        else
        {
            lastDamageTime = -10f;  // 关碰撞体时清空，防止影响到下一个攻击
            if (weaponHitBox.activeSelf)
                weaponHitBox.SetActive(false);
        }
    }

    /// <summary>
    /// 加粗武器碰撞体（只执行一次），降低高速挥砍穿透概率
    /// </summary>
    private void InflateHitboxCollider(float scale)
    {
        if (weaponHitBox == null) return;
        Collider col = weaponHitBox.GetComponent<Collider>();
        if (col == null) return;

        hitboxInflated = true;

        if (col is BoxCollider box)
        {
            hitboxOriginalSize = box.size;
            box.size *= scale;
        }
        else if (col is SphereCollider sphere)
        {
            hitboxOriginalRadius = sphere.radius;
            sphere.radius *= scale;
        }
        else if (col is CapsuleCollider capsule)
        {
            hitboxOriginalRadius = capsule.radius;
            hitboxOriginalHeight = capsule.height;
            capsule.radius *= scale;
            capsule.height *= scale;
        }
    }

    public void AttachWeaponToHand()
    {
        if (weaponModel == null || handBone == null) return;
        weaponModel.transform.SetParent(handBone);
        weaponModel.transform.localPosition = Vector3.zero;
        weaponModel.transform.localRotation = Quaternion.identity;
    }

    public void AttachWeaponToSheath()
    {
        if (weaponLockedInHand) return;   // 鎴樻枟鐘舵€佷笉鍥為灅
        if (weaponModel == null || sheathPoint == null) return;
        weaponModel.transform.SetParent(sheathPoint);
        weaponModel.transform.localPosition = Vector3.zero;
        weaponModel.transform.localRotation = Quaternion.identity;
        weaponModel.SetActive(true);
    }

    public void ForceWeaponToSheath()
    {
        if (weaponLockedInHand) return;   // 棣栧垁鍚庝笉鍥為灅
        if (weaponModel == null) return;
        weaponModel.SetActive(true);
        if (sheathPoint != null)
        {
            weaponModel.transform.SetParent(sheathPoint);
            weaponModel.transform.localPosition = Vector3.zero;
            weaponModel.transform.localRotation = Quaternion.identity;
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

    // ==================== 锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷 ====================
    public void OnAttackFinished()
    {
        consecutiveHits = 0;  // 成功出招 → 重置连续受击
        interruptedAttackCount = 0;  // 攻击正常结束 → 清零连续打断计数
        lastAttackEndTime = Time.time;  // 记录本次攻击结束时间
        // 狂暴阶段：收刀时已决策，直接切下一招
        if (nextMoveAfterSheath != null)
        {
            // 防止链回同一招（如 ChargeSlash → ChargeSlash）
            if (nextMoveAfterSheath.Value == lastAttack)
            {
                nextMoveAfterSheath = null;
            }
            else
            {
                var phaseMgr = GetComponent<BossPhaseManager>();
                if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst)
                {
                    EnemyStates next = nextMoveAfterSheath.Value;
                    nextMoveAfterSheath = null;
                    ChangeState(next);
                    return;
                }
            }
        }
        nextMoveAfterSheath = null;

        // 连招链
        if (ShouldContinueChain())
        {
            StartChainNextAttack();
            return;
        }

        // 位锟狡达拷锟斤拷
        if (ShouldDodgeAfterAttack())
        {
            ChangeState(EnemyStates.Dodge);
            return;
        }

        // 原锟斤拷锟斤拷锟竭硷拷
        if (pendingExhaustion)
        {
            pendingExhaustion = false;
            ChangeState(EnemyStates.Exhausted);
        }
        else
        {
            ChangeState(EnemyStates.Idle);
        }
    }

    /// <summary>
    /// 标记本招是否命中玩家（由攻击状态或武器碰撞时调用）
    /// </summary>
    public void MarkAttackHit(bool hitPlayer)
    {
        lastDerivedMoveHitPlayer = hitPlayer;
        actionPredictor?.OnBossAttackHit(hitPlayer);
    }

    private bool ShouldContinueChain()
    {
        if (bossPhaseManager == null) return false;

        int maxChain;
        switch (bossPhaseManager.CurrentPhase)
        {
            case BossPhaseManager.BossPhase.PhaseOne_Test:
                return false;
            case BossPhaseManager.BossPhase.PhaseTwoBurst:
                // 狂暴：派生到第 4 招（含起手共 4 招，派生 3 招）
                maxChain = 3;
                break;
            case BossPhaseManager.BossPhase.PhaseThreeMaster:
                // 宗师：由预测决定是否继续，不受固定次数限制
                // 结束条件在 EndComboChain 中判断
                return derivedMoveCount < 6; // 最多派生 6 招防止无限循环
            default:
                return false;
        }

        if (comboChainCount >= maxChain) return false;
        return true;
    }

    private void StartChainNextAttack()
    {
        EnemyStates next = PickChainMove();
        if (next != EnemyStates.Idle)
        {
            comboChainCount++;
            derivedMoveCount++;
            isInComboChain = true;
            ChangeState(next);
        }
        else
        {
            EndComboChain();
        }
    }

    private EnemyStates PickChainMove()
    {
        if (bossPhaseManager == null) return EnemyStates.Idle;

        // Phase3 宗师：使用预测系统
        if (bossPhaseManager.CurrentPhase == BossPhaseManager.BossPhase.PhaseThreeMaster)
        {
            return PickPredictedMove();
        }

        // Phase2 狂暴：推拉式派生，以 Combo 为核心目标
        if (bossPhaseManager.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst)
        {
            return PickBerserkChainMove();
        }

        // Phase1：无派生
        return EnemyStates.Idle;
    }

    /// <summary>
    /// Phase2 狂暴：根据距离推拉，最终打出 Combo
    /// </summary>
    private EnemyStates PickBerserkChainMove()
    {
        float dist = DistanceToPlayer();

        // 中距离（核心目标区）：直接出 Combo
        if (dist > 3f && dist < 6f)
            return EnemyStates.Combo;

        // 近距离：推开
        if (dist <= 3f)
        {
            // 交替用 NormalSlash（推）和 Dodge（拉开），避免重复
            if (comboChainCount % 2 == 0)
                return EnemyStates.NormalSlash;
            else
            {
                // Dodge 会让派生提前结束，所以这里用 QuickSlash
                return EnemyStates.QuickSlash;
            }
        }

        // 远距离：拉近
        if (dist >= 6f)
            return EnemyStates.ThrustSlash;

        // 兜底
        return EnemyStates.QuickSlash;
    }

    /// <summary>
    /// Phase3 宗师：基于预测选择下一招
    /// </summary>
    private EnemyStates PickPredictedMove()
    {
        if (actionPredictor == null) return EnemyStates.QuickSlash;

        // 检测玩家当前动作
        actionPredictor.UpdatePlayerAction();
        PlayerAction currentAction = DetectPlayerCurrentAction();

        // 判断是否进入读指令模式
        bool readMode = actionPredictor.ShouldEnterReadMode(derivedMoveCount, lastDerivedMoveHitPlayer);

        if (readMode)
        {
            // 读指令模式：直接读取玩家当前输入，出克制招式
            return actionPredictor.GetCounterMove(currentAction);
        }
        else
        {
            // 预测模式：预测玩家下一步动作，出克制招式
            PlayerAction predicted = actionPredictor.PredictNextAction(currentAction);
            if (predicted == PlayerAction.None)
                return EnemyStates.QuickSlash; // 无数据时默认速斩
            return actionPredictor.GetCounterMove(predicted);
        }
    }

    /// <summary>
    /// 检测玩家的当前操作（给预测模块用）
    /// </summary>
    private PlayerAction DetectPlayerCurrentAction()
    {
        if (player == null) return PlayerAction.None;

        MeeleFighter mf = player.GetComponent<MeeleFighter>();
        PlayerController pc = player.GetComponent<PlayerController>();

        if (mf == null || pc == null) return PlayerAction.None;

        if (mf.IsBlocking) return PlayerAction.Block;
        if (pc != null && pc.IsInvincible) return PlayerAction.Dodge;
        if (mf.InAction) return PlayerAction.Attack;
        if (pc != null && pc.GetComponent<CharacterController>()?.velocity.magnitude > 0.1f) return PlayerAction.Move;

        return PlayerAction.None;
    }

    public void EndComboChain()
    {
        isInComboChain = false;
        comboChainCount = 0;

        if (bossPhaseManager != null && bossPhaseManager.CurrentPhase == BossPhaseManager.BossPhase.PhaseThreeMaster)
        {
            // 宗师：检测连续 3 招未命中 → 架势惩罚
            if (derivedMoveCount >= 3 && !lastDerivedMoveHitPlayer)
            {
                BossPosture posture = GetComponent<BossPosture>();
                if (posture != null)
                {
                    float reduceAmount = Mathf.Max(posture.MaxPosture * 0.5f, posture.CurrentPosture * 0.5f);
                    posture.CurrentPosture -= reduceAmount;
                }
            }
            derivedMoveCount = 0;
            StartCoroutine(ChainEndStagger());
        }
        else
        {
            derivedMoveCount = 0;
            ChangeState(EnemyStates.Idle);
        }
    }

    private IEnumerator ChainEndStagger()
    {
        HasSuperArmor = true;
        anim.Play("Hit_F", 0, 0f);
        yield return new WaitForSeconds(1f);
        if (shouldAbortAttack) yield break;
        HasSuperArmor = false;
        ChangeState(EnemyStates.Dodge);
    }

    // ==================== 追击惩罚 ====================
    /// <summary>
    /// 检测玩家是否在追击（Dodge/Shift 后接近 Boss）
    /// </summary>
    private bool IsPlayerChasing()
    {
        if (player == null) return false;
        PlayerController pc = player.GetComponent<PlayerController>();
        if (pc == null || pc.IsInvincible) return false;

        // 玩家正在向 Boss 移动
        Vector3 dirToBoss = (transform.position - player.transform.position).normalized;
        dirToBoss.y = 0;
        Vector3 playerVel = player.GetComponent<CharacterController>()?.velocity ?? Vector3.zero;
        playerVel.y = 0;

        // 玩家的移动方向朝向 Boss 且速度足够快
        if (playerVel.magnitude > 2f)
        {
            float dot = Vector3.Dot(playerVel.normalized, dirToBoss);
            return dot > 0.3f;
        }
        return false;
    }

    /// <summary>
    /// 获取追击惩罚的反击招式（按阶段）
    /// </summary>
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

    /// <summary>
    /// 标记 Dodge 结束（由 DodgeState.Exit 调用）
    /// </summary>
    public void OnDodgeEnded()
    {
        dodgeEndTime = Time.time;
    }

    protected virtual bool ShouldDodgeAfterAttack()
    {
        return false;
    }

    // ==================== 强锟斤拷停止锟斤拷锟斤拷锟斤拷锟斤拷帧锟斤拷希锟?====================
    /// <summary>
    /// 锟斤拷锟斤拷停止一锟叫癸拷锟斤拷锟斤拷锟教ｏ拷锟斤拷锟斤拷协锟斤拷锟皆硷拷
    /// </summary>
    public void ForceStopAllAttacks()
    {
        shouldAbortAttack = true;

        interruptedAttackCount++;  // 记录一次被打断

        // 停止锟斤拷前锟斤拷锟斤拷协锟斤拷
        if (currentAttackRoutine != null)
        {
            StopCoroutine(currentAttackRoutine);
            currentAttackRoutine = null;
        }

        // 锟截憋拷锟斤拷锟斤拷锟斤拷撞
        EnableWeaponHitBox(false, false);

        // 强制恢复动画速度，防止攻击协程遗留的 speed=0
        anim.speed = 1f;

        // 强锟狡诧拷锟脚憋拷锟斤拷隙锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷芑锟?锟斤拷锟斤拷锟斤拷锟斤拷锟结覆锟角ｏ拷
        anim.Play("Hit_F", 0, 0f);
        anim.Update(0f);  // 立即执行动画切换，跳过过渡

        // 锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷
        DisableAttackLayer();

        // 锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷
        isInComboChain = false;
        comboChainCount = 0;

        // 锟斤拷锟斤拷值锟斤拷锟斤拷锟斤拷煤锟斤拷锟斤拷锟斤拷炭锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟?
        lockWeaponInHand = false;

        // Keep current state (don't switch to Idle) - prevents decision engine
        // from firing during hit reaction. HitReactionRoutine will switch to Idle.
        int attackLayer = anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1)
        {
            anim.SetLayerWeight(attackLayer, 0f);
            if (anim.HasState(attackLayer, Animator.StringToHash("Empty")))
            {
                anim.Play("Empty", attackLayer, 0f);
                anim.Update(0f);  // 锟斤拷锟斤拷执锟斤拷
            }
        }

        // 澶嶄綅姝﹀櫒鍒版墜閮ㄩ浂鍋忕Щ锛岄槻姝㈡墜閮ㄩ楠煎洜鍔ㄧ敾寮傚父瀵艰嚧姝﹀櫒涔遍
        AttachWeaponToHand();
    }

    // ==================== 锟杰伙拷锟斤拷锟斤拷 ====================
    public void PlayHitReaction(Vector3 hitDirection, bool isHeavy = false, float hitStopDuration = 0.1f)
    {
        if (anim == null)
        {
            Debug.LogError("EnemyController: anim 为锟秸ｏ拷锟睫凤拷锟斤拷锟斤拷锟杰伙拷锟斤拷锟斤拷");
            return;
        }

        if (StateMachine == null || StateMachine.CurrentState is ExhaustedState) return;

        // 锟斤拷师锟阶讹拷锟斤拷锟斤拷锟斤拷锟斤拷希锟斤拷锟斤拷獯︼拷锟斤拷锟?
        if (bossPhaseManager != null &&
            bossPhaseManager.CurrentPhase == BossPhaseManager.BossPhase.PhaseThreeMaster &&
            isInComboChain)
        {
            comboInterrupted = true;
            shouldAbortAttack = true;
            ForceStopAllAttacks();
            EndComboChain();
            decisionEngine?.OnPlayerInterrupt();
            return;
        }

        // 锟狡筹拷锟斤拷锟藉保锟斤拷锟斤拷锟斤拷锟节硷拷使锟斤拷锟斤拷也锟结被锟斤拷锟?
        // if (HasSuperArmor) return;

        shouldAbortAttack = true;
        ForceStopAllAttacks();

        consecutiveHits++;  // 记录一次连续受击

        // 保险：停旧受击协程，同时立即清掉 Hit Layer 权重
        if (hitReactionCoroutine != null)
        {
            StopCoroutine(hitReactionCoroutine);
            int oldHitLayer = anim.GetLayerIndex("Hit Layer");
            if (oldHitLayer != -1)
                anim.SetLayerWeight(oldHitLayer, 0f);
        }

        string animName = GetHitAnimationName(hitDirection, isHeavy);
        if (string.IsNullOrEmpty(animName))
        {
            Debug.LogError("EnemyController: 锟斤拷锟斤拷锟斤拷为锟斤拷");
            return;
        }

        // 在 Hit Layer 上播放受击动画，权重最高，覆盖 Attack Layer
        int hitLayer = anim.GetLayerIndex("Hit Layer");
        if (hitLayer != -1)
        {
            anim.SetLayerWeight(hitLayer, 1f);
            anim.Play(animName, hitLayer, 0f);
        }
        else
        {
            // 兼容处理：没有 Hit Layer 时回退到 Base Layer
            anim.Play(animName, 0, 0f);
        }
        anim.Update(0f);  // 立即执行受击动画，跳过过渡

        // 🔒 保险：强制恢复动画速度，防止之前残留的 speed=0
        anim.speed = 1f;

        lastHitStopDuration = hitStopDuration;
        hitReactionCoroutine = StartCoroutine(HitReactionRoutine(animName));
    }

    public void AddAggression(float amount)
    {
        currentAggression = Mathf.Min(maxAggression, currentAggression + amount);
    }

    public void PlayParryReaction(Vector3 attackDirection)
    {
        if (StateMachine == null)
        {
            Debug.LogWarning("PlayParryReaction: StateMachine 为锟秸ｏ拷锟斤拷锟斤拷直锟接诧拷锟脚碉拷锟斤拷锟斤拷锟斤拷");
            if (anim != null && !isDead)
            {
                if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
                isParryAnimating = true;
                EnableWeaponHitBox(false, false);
                Vector3 localDir = transform.InverseTransformDirection(attackDirection.normalized);
                int blockDir = localDir.x > 0 ? 1 : 0;
                hitReactionCoroutine = StartCoroutine(BlockHitRoutine(blockDir));
            }
            return;
        }

        if (StateMachine.CurrentState is ExhaustedState) return;
        if (HasSuperArmor) return;

        bool isUninterruptible = StateMachine.CurrentState is QuickSlashState ||
                                 StateMachine.CurrentState is IaiSlashState ||
                                 StateMachine.CurrentState is SlashState;

        if (!isUninterruptible)
        {
            shouldAbortAttack = true;
            ForceStopAllAttacks();

            State<EnemyController> curState = StateMachine.CurrentState;
            if (curState is MonoBehaviour mb)
            {
                mb.enabled = false;
                mb.StopAllCoroutines();
            }

            StopAllCoroutines();
            ChangeState(EnemyStates.Idle);

            Vector3 localDir = transform.InverseTransformDirection(attackDirection.normalized);
            int blockDir = localDir.x > 0 ? 1 : 0;

            if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
            isParryAnimating = true;
            hitReactionCoroutine = StartCoroutine(BlockHitRoutine(blockDir));
        }
        else
        {
            shouldAbortAttack = true;
            ForceStopAllAttacks();

            EnableWeaponHitBox(false, false);
            if (anim != null)
            {
                int attackLayer = anim.GetLayerIndex("Attack Layer");
                if (attackLayer != -1) anim.SetLayerWeight(attackLayer, 0f);
            }

            Vector3 localDir = transform.InverseTransformDirection(attackDirection.normalized);
            int blockDir = localDir.x > 0 ? 1 : 0;
            if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
            isParryAnimating = true;
            hitReactionCoroutine = StartCoroutine(BlockHitRoutine(blockDir));
        }
    }

    private IEnumerator BlockHitRoutine(int blockDirection)
    {
        if (anim == null)
        {
            isParryAnimating = false;
            yield break;
        }

        AttachWeaponToHand();

        anim.speed = 0.9f;
        anim.SetInteger("BlockDirection", blockDirection);
        anim.SetTrigger("BlockHit");

        yield return null;
        if (shouldAbortAttack) yield break;

        AnimatorStateInfo blockState = anim.GetCurrentAnimatorStateInfo(0);
        float blockLength = (blockState.IsName("Block_L") || blockState.IsName("Block_R")) ? blockState.length : 0.5f;
        float actualBlockDuration = blockLength / anim.speed;
        yield return new WaitForSeconds(actualBlockDuration);

        if (isDead) yield break;
        if (shouldAbortAttack) yield break;

        anim.speed = 0.9f;
        anim.Play("Hit_Large_F", 0, 0f);
        yield return null;
        if (shouldAbortAttack) yield break;

        AnimatorStateInfo largeState = anim.GetCurrentAnimatorStateInfo(0);
        float largeLength = largeState.IsName("Hit_Large_F") ? largeState.length : 0.8f;
        float actualLargeDuration = largeLength / anim.speed;
        yield return new WaitForSeconds(actualLargeDuration);

        if (isDead) yield break;
        if (shouldAbortAttack) yield break;

        anim.speed = 1f;
        anim.applyRootMotion = false;

        // 强制清理 Attack Layer，防止残留攻击状态
        int attackLayer = anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1)
        {
            anim.SetLayerWeight(attackLayer, 0f);
            if (anim.HasState(attackLayer, Animator.StringToHash("Empty")))
                anim.Play("Empty", attackLayer, 0f);
        }
        anim.Update(0f);

        // 强制执行状态切换回 Idle
        ChangeState(EnemyStates.Idle);

        // 锁定决策 0.5 秒（在 ChangeState 之后，避免被 ResetTimer 覆盖）
        BossDecisionEngine de2 = GetComponent<BossDecisionEngine>();
        if (de2 != null) de2.LockDecision(0.5f);

        isParryAnimating = false;
        shouldAbortAttack = false;
    }

    public void PlayGuardBreakReaction(Vector3 attackDirection)
    {
        if (StateMachine == null || StateMachine.CurrentState is ExhaustedState) return;
        if (HasSuperArmor) return;
        if (anim == null) return;

        if (currentAttackRoutine != null)
        {
            StopCoroutine(currentAttackRoutine);
            currentAttackRoutine = null;
        }
        EnableWeaponHitBox(false, false);
        DisableAttackLayer();

        string animName = GetHitAnimationName(attackDirection, true);
        int hitLayer = anim.GetLayerIndex("Hit Layer");
        if (hitLayer != -1)
        {
            anim.SetLayerWeight(hitLayer, 1f);
            anim.Play(animName, hitLayer, 0f);
        }
        else
        {
            anim.Play(animName, 0, 0f);
        }
        if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
        hitReactionCoroutine = StartCoroutine(HitReactionRoutine(animName));
    }

    private IEnumerator HitReactionRoutine(string animName)
    {
        int hitLayer = anim.GetLayerIndex("Hit Layer");
        int checkLayer = (hitLayer != -1) ? hitLayer : 0;
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(checkLayer);
        float length = state.IsName(animName) ? state.length : 0.5f;

        // 顿帧：先播一帧受击再冻结，让玩家看到受击瞬间
        if (lastHitStopDuration > 0f)
        {
            yield return null;
            anim.speed = 0f;
            yield return new WaitForSecondsRealtime(lastHitStopDuration);
            anim.speed = 1f;
        }

        // 等待剩余受击动画播完
        yield return new WaitForSeconds(length);

        if (isDead) yield break;
        hitReactionCoroutine = null;

        // 🔒 保险：离开受击状态前强制恢复动画速度
        anim.speed = 1f;

        // 关闭 Hit Layer
        if (hitLayer != -1)
            anim.SetLayerWeight(hitLayer, 0f);

        if (!isDead) ChangeState(EnemyStates.Idle);

        // 锁定决策 0.5 秒（放在 ChangeState 之后，避免被 ResetTimer 覆盖）
        BossDecisionEngine de = GetComponent<BossDecisionEngine>();
        if (de != null) de.LockDecision(0.5f);
    }

    private string GetHitAnimationName(Vector3 worldDir, bool isHeavy)
    {
        int idx = GetDirectionIndex(worldDir);
        if (isHeavy)
        {
            return idx switch
            {
                0 => "Hit_Large_F",
                1 => "Hit_Large_B",
                2 => "Hit_Large_L",
                3 => "Hit_Large_R",
                _ => "Hit_Large_F"
            };
        }
        else
        {
            return idx switch
            {
                0 => "Hit_F",
                1 => "Hit_B",
                2 => "Hit_L",
                3 => "Hit_R",
                _ => "Hit_F"
            };
        }
    }

    private int GetDirectionIndex(Vector3 worldDir)
    {
        Vector3 localDir = transform.InverseTransformDirection(worldDir.normalized);
        float angle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
        if (angle > -45 && angle <= 45) return 0;
        if (angle > 45 && angle <= 135) return 2;
        if (angle < -45 && angle >= -135) return 3;
        return 1;
    }

    // ==================== 锟斤拷锟斤拷锟斤拷锟斤拷锟?====================
    public void DisableAttackLayer()
    {
        int attackLayer = anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1) anim.SetLayerWeight(attackLayer, 0f);
        anim.SetBool("isSlashing", false);
        anim.SetBool("isCombo", false);
        anim.SetBool("isQuick", false);
        anim.SetBool("isChargeSlash", false);
        anim.SetBool("isKanpo", false);
        anim.SetBool("isIai", false);
        anim.SetBool("isThrust", false);
        anim.Update(0f);  // 强制处理过渡，防止残留态在下次权重=1时重新触发
    }

    public void EnableAttackLayer()
    {
        int attackLayer = anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1) anim.SetLayerWeight(attackLayer, 1f);
    }

    // ==================== 锟斤拷锟斤拷锟斤拷锟斤拷锟?====================
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

    // ==================== 锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷 ====================
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

    public void InterruptCurrentAttack()
    {
        if (StateMachine.CurrentState is IaiSlashState || StateMachine.CurrentState is SlashState) return;

        if (currentAttackRoutine != null)
        {
            StopCoroutine(currentAttackRoutine);
            currentAttackRoutine = null;
        }
        EnableWeaponHitBox(false, false);
        ChangeState(EnemyStates.Idle);
    }

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
        // 锟斤拷锟斤拷锟斤拷要锟街讹拷锟斤拷锟斤拷 Idle 锟斤拷时锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟斤拷锟窖接癸拷
    }

    public void StopCurrentAttack()
    {
        if (currentAttackRoutine != null)
        {
            StopCoroutine(currentAttackRoutine);
            currentAttackRoutine = null;
        }
    }

    public void RegisterAttackRoutine(Coroutine routine) => currentAttackRoutine = routine;

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
        HasSuperArmor = true;
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
        HasSuperArmor = false;
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
        HasSuperArmor = false;
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

    // ==================== 锟接撅拷锟斤拷锟斤拷 ====================
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(groundCheckOffset), groundCheckRadius);
    }

    public void HideAllWeaponModels()
    {
        if (weaponModel != null)
        {
            Renderer[] renderers = weaponModel.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers) r.enabled = false;
        }
        GameObject sheath = sheathModel;
        if (sheath == null)
        {
            Transform found = transform.Find("Katana_sheath");
            if (found != null) sheath = found.gameObject;
        }
        if (sheath != null)
        {
            Renderer[] renderers = sheath.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers) r.enabled = false;
        }
    }

    public void ShowAllWeaponModels()
    {
        if (weaponModel != null)
        {
            AttachWeaponToHand();
            Renderer[] renderers = weaponModel.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers) r.enabled = true;
        }
        GameObject sheath = sheathModel;
        if (sheath == null)
        {
            Transform found = transform.Find("Katana_sheath");
            if (found != null) sheath = found.gameObject;
        }
        if (sheath != null)
        {
            Renderer[] renderers = sheath.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers) r.enabled = true;
        }
    }

    // ==================== distance dodge ====================
    private IEnumerator DodgeBackRoutine(string animName)
    {
        if (StateMachine.CurrentState is ExhaustedState || isExecutionFrozen)
            yield break;

        // 关武器碰撞体，防止后跳过程中误触
        EnableWeaponHitBox(false, false);

        // 用 root motion 驱动位移（动画自带后退运动）
        anim.applyRootMotion = true;
        anim.Play(animName, 0, 0f);
        anim.Update(0f);

        // 等动画播完
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        float animLength = state.IsName(animName) ? state.length : 0.4f;
        yield return new WaitForSeconds(animLength);

        anim.applyRootMotion = false;
    }

    // ==================== 锟节诧拷锟斤拷锟斤拷 ====================
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

    IEnumerator OpeningSequence()
    {
        openingSequenceDone = true;

        // 1. 拔刀前，刀在刀鞘上
        if (weaponModel != null && sheathPoint != null)
        {
            weaponModel.transform.SetParent(sheathPoint);
            weaponModel.transform.localPosition = Vector3.zero;
            weaponModel.transform.localRotation = Quaternion.identity;
            weaponModel.SetActive(true);
        }

        // 2. 播放拔刀动画
        anim.Play("ToFight", 0, 0f);
        anim.Update(0f);
        yield return null;

        // 3. 获取动画长度
        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        float animLength = stateInfo.IsName("ToFight") ? stateInfo.length : 1.5f;

        // 4. 等待到 90%（拔刀动作完成，刀从鞘切换到手上）
        float switchTime = animLength * 0.90f;
        yield return new WaitForSeconds(switchTime);
        AttachWeaponToHand();

        // 5. 再接突刺
        ChangeState(EnemyStates.ThrustSlash);
    }

    IEnumerator PhaseTwoEngageSequence()
    {
        ForceClearAndPlayBuff(null);
        yield return new WaitForSeconds(2.5f);
        ChangeState(EnemyStates.Idle);
    }
}