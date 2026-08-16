using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 狂暴阶段（PhaseTwoBurst）派生链系统（队列式动态决策）
/// 1. 链 = 全部攻击招式（不含 Dodge）洗牌生成初始顺序
/// 2. 执行中每招结束：从剩余链随机捞 2 个候选 → 选"距离匹配度更高"的招式提前为下一招
/// 3. 攻击动画播完立即接下一招（零间隙）；链用完结束 → 回决策引擎抽奖
/// 4. 打断（受击 / 连续完美格挡 2 次）→ 清空链，重新决策
/// 5. 完美格挡打断奖励：立即打断 + Hit_Large_F 后摇 + 扣架势（下一招是 Combo 扣 20，否则扣 50）
/// </summary>
public class BossComboChain : MonoBehaviour
{
    [Header("距离阈值（招式定位）")]
    public float nearRange = 3f;       // 近：Normal/Kanpo/Charge（逼退）
    public float midNearRange = 5f;    // 中近：Quick
    public float midRange = 8f;        // 中：Combo（派生目标）
    public float midFarRange = 12f;    // 中远：Thrust（拉近）
    // 远：Iai（拉近）

    [Header("链参数")]
    public int perfectBlockBreakCount = 2;   // 连续完美格挡次数 → 打断
    public float linkRecoveryBuffer = 0.2f;  // 派生模式快速收刀缓冲（秒）
    public float linkTransitionNormalized = 0.9f;   // ★★ 连招衔接点（全局唯一入口）：攻击动画播到此比例即接下一招
                                                    //    0.2 = 20%（最快连绵）/ 0.8 = 80% / 1.0 = 完整播完
                                                    //    改这里，7 个攻击状态全部生效（AttackStateBase 从 combat 读取）

    [Header("打断奖励")]
    public float comboDrain = 20f;           // 下一招是 Combo → 扣 20 架势
    public float otherDrain = 50f;           // 否则 → 扣 50 架势
    public float breakRecoveryDuration = 1.2f; // 打断后摇时长

    [Header("狂暴打断·追击机制（攻击中任意阶段 → Thrust/Iai）")]
    public float interruptCooldown = 5f;                // 打断最小间隔（秒）
    public float interruptChance = 0.3f;                // 基础概率（aggression 满时 + aggressionChanceBonus）
    public float aggressionChanceBonus = 0.3f;          // 满狂暴度时的概率加成
    public float interruptThrustMinDist = 7f;           // 玩家距离 ≥ 此值 → 打断成 ThrustSlash（拉近）
    public float interruptIaiMinDist = 11f;             // 玩家距离 ≥ 此值 → 打断成 IaiSlash（拉近）
    public float interruptIaiPlayerStaminaDist = 5f;    // 玩家低体力(<30%)且距离 ≥ 此值 → 打断成 IaiSlash
    public float interruptMinProgress = 0.2f;           // 当前招至少播到该进度才允许打断（防起手抽搐）
    public float interruptStaminaCostPercent = 0.1f;    // 打断体力代价 = 最大体力 10%
    public float overdrawExhaustExtraPerPercent = 0.5f; // 体力透支时，每差 1%（向上取整）延长力竭 0.5 秒

    private EnemyController enemy;
    private EnemyCombat combat;
    private BossDecisionEngine decisionEngine;
    private BossPosture posture;

    private bool chainActive = false;
    private readonly List<EnemyStates> chainQueue = new List<EnemyStates>();   // 剩余链（有序）
    private int consecutivePerfectBlocks = 0;
    private bool inBreakRecovery = false;
    private Coroutine breakRoutine;

    // 狂暴打断·追击机制运行时状态
    private float nextInterruptTime = 0f;
    private bool interruptUsedThisChain = false;      // 每条链最多 1 次打断
    private EnemyStates? pendingInterruptMove = null; // 打断发起的招（OnAttackStarted 旁路识别：不消费链池）
    private int emptyAttackHash;
    private PlayerStamina playerStamina;
    private BossStamina bossStamina;

    // 招式记录（OnAttackFinished 每帧触发，用 lastHandledMove 防重）
    private EnemyStates currentMove;
    private EnemyStates lastHandledMove;
    private bool firstLinkPending = false;   // ★ 第一段衔接：固定出"初始决策的另一个候选"（不重选）

    public bool IsChainActive => chainActive;
    public bool IsInBreakRecovery => inBreakRecovery;

    private void Awake()
    {
        enemy = GetComponent<EnemyController>();
        combat = GetComponent<EnemyCombat>();
        decisionEngine = GetComponent<BossDecisionEngine>();
        posture = GetComponent<BossPosture>();
        bossStamina = GetComponent<BossStamina>();

        emptyAttackHash = Animator.StringToHash("Empty_Attack");
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerStamina = playerObj.GetComponent<PlayerStamina>();

        BossEvents events = GetComponent<BossEvents>();
        if (events != null)
        {
            events.OnAttackStarted += OnAttackStartedHandler;
            // ★ OnAttackFinished 不再在此订阅：EnemyController 已直接调用 comboChain.OnAttackFinished() 并据返回值决定是否回 Idle。
            //   双重订阅会导致同一次攻击结束被处理两次——第一次链接下一招后 currentMove 已同步更新，第二次会再选一招（跳过衔接招式）。
            events.OnAttackInterrupted += OnAttackInterruptedHandler;
            events.OnHitTaken += OnHitTakenHandler;
        }
    }

    private void OnDestroy()
    {
        BossEvents events = GetComponent<BossEvents>();
        if (events != null)
        {
            events.OnAttackStarted -= OnAttackStartedHandler;
            events.OnAttackInterrupted -= OnAttackInterruptedHandler;
            events.OnHitTaken -= OnHitTakenHandler;
        }
    }

    // ═══════════════════ 事件 ═══════════════════

    // 攻击开始：记录招式；第一招（抽奖）时开链；链中后续招兜底去重
    private void OnAttackStartedHandler(EnemyStates move)
    {
        // ★ 狂暴打断发起的招：行为完全独立——不消费/不移除链池，不更新链队列状态
        if (pendingInterruptMove.HasValue)
        {
            if (pendingInterruptMove.Value == move)
            {
                pendingInterruptMove = null;
                currentMove = move;
                combat.isDerivedMove = IsPhaseTwo() && !inBreakRecovery;
                return;
            }
            pendingInterruptMove = null;   // 上次打断未生效（被守卫吞掉等），清残留
        }

        currentMove = move;
        combat.isDerivedMove = IsPhaseTwo() && !inBreakRecovery;

        if (!combat.isDerivedMove) return;

        if (!chainActive)
        {
            // ★ 新开链：链 = [初始决策的另一个候选] + 剩余 5 招随机排列一次
            chainActive = true;
            chainQueue.Clear();
            BuildChainQueue(move);
            firstLinkPending = true;   // ★ 第一段衔接固定打"另一个候选"（不重选）
        }
        else
        {
            // 链中后续招：从队列移除已打出（正常情况下 PickNext 已移除，这里兜底）
            chainQueue.Remove(move);
        }
    }

    /// <summary>
    /// ★ 新开链：决策引擎本次"抽2选1"选中的招（=current）正在打；
    ///   另一个候选固定为链头（第一段不重选），剩余 5 招随机排列一次放后面。
    ///   无候选对（首招保证/调试键等）→ 剩余 6 招全洗牌。
    /// </summary>
    private void BuildChainQueue(EnemyStates current)
    {
        List<EnemyStates> pool = new List<EnemyStates>
        {
            EnemyStates.NormalSlash, EnemyStates.QuickSlash, EnemyStates.Combo,
            EnemyStates.ChargeSlash, EnemyStates.KanPo, EnemyStates.IaiSlash,
            EnemyStates.ThrustSlash
        };
        pool.Remove(current);   // 当前招已在打，不入队

        EnemyStates? other = decisionEngine != null ? decisionEngine.GetOtherCandidate(current) : null;
        if (other.HasValue)
        {
            chainQueue.Add(other.Value);
            pool.Remove(other.Value);
        }

        Shuffle(pool);
        chainQueue.AddRange(pool);
    }

    // 攻击被中断（受击/打断）：重置派生链
    private void OnAttackInterruptedHandler() => ResetChain();

    // Boss 受击：链被打断，重新决策
    private void OnHitTakenHandler()
    {
        ResetChain();
    }

    // Boss 受击：链被打断，重新决策
    private void ResetChain()
    {
        if (breakRoutine != null) { StopCoroutine(breakRoutine); breakRoutine = null; }
        inBreakRecovery = false;
        chainActive = false;
        chainQueue.Clear();
        firstLinkPending = false;
        interruptUsedThisChain = false;   // 链重置：狂暴打断次数复位
        lastHandledMove = EnemyStates.Wait;   // 防新链首招与旧链末招相同导致防重误判
        if (enemy != null && enemy.anim != null) enemy.anim.speed = 1f;
    }

    /// <summary>
    /// 攻击自然结束：从链中动态选出下一招（EnemyController 调用）。
    /// 返回 true = 已出下一招（不回 Idle）；false = 链结束（回 Idle 抽奖）。
    /// </summary>
    public bool OnAttackFinished()
    {
        if (!IsPhaseTwo() || inBreakRecovery)
        {
            return false;
        }
        if (currentMove == lastHandledMove)
        {
            return false;
        }
        lastHandledMove = currentMove;

        if (!chainActive)
        {
            return false;
        }

        EnemyStates? next;
        if (firstLinkPending)
        {
            // ★ 第一段衔接：固定打"初始决策的另一个候选"，不做距离重选
            firstLinkPending = false;
            next = chainQueue.Count > 0 ? chainQueue[0] : (EnemyStates?)null;
            if (next.HasValue) chainQueue.RemoveAt(0);
        }
        else
        {
            // 从剩余链动态选下一招（随机捞 2 个 → 选距离匹配度更高的提前）
            next = PickNextByDistance();
        }

        if (next == null)
        {
            EndChain();
            return false;
        }

        // ★ 零间隙衔接：先直接 Play 下一招动画（跳过 Empty_Attack 站姿穿插与过渡帧），
        //   再切换状态机（当前招 Exit 收刀 → 下一招 Enter 挂刀 + SetupAnimation），动画从第 0 帧无缝起手
        int atkLayer = enemy.anim.GetLayerIndex("Attack Layer");
        if (atkLayer == -1) atkLayer = 0;
        string nextState = GetAttackStateName(next.Value);
        if (!string.IsNullOrEmpty(nextState))
            enemy.anim.Play(nextState, atkLayer, 0f);

        enemy.ChangeState(next.Value);
        return true;
    }

    // EnemyStates → Animator 攻击层状态名（零间隙衔接直接 Play 用）
    private string GetAttackStateName(EnemyStates s)
    {
        return s switch
        {
            EnemyStates.NormalSlash => "Slash",
            EnemyStates.QuickSlash => "Quick",
            EnemyStates.Combo => "Combo",
            EnemyStates.ChargeSlash => "ChargeSlash",
            EnemyStates.KanPo => "Kanpo",
            EnemyStates.IaiSlash => "Iai",
            EnemyStates.ThrustSlash => "ThrustSlash",
            _ => null
        };
    }

    // ═══════════════════ 狂暴打断·追击机制（攻击中任意阶段 → Thrust/Iai） ═══════════════════

    private void Update()
    {
        // 触发条件：狂暴阶段、非后摇、冷却结束、本链未用过打断、正在攻击中
        if (!IsPhaseTwo() || inBreakRecovery) return;
        if (Time.time < nextInterruptTime) return;
        if (interruptUsedThisChain) return;
        if (enemy == null || enemy.StateMachine == null || enemy.anim == null) return;
        if (enemy.isParryAnimating) return;                       // 弹刀演出中不断
        if (!(enemy.StateMachine.CurrentState is AttackStateBase)) return;   // 只在攻击中
        BossPhaseManager pm = GetComponent<BossPhaseManager>();
        if (pm != null && pm.IsInPhaseTransition) return;         // 转场期间不断

        // 进度闸：当前攻击动画至少播到 interruptMinProgress（排除 Empty_Attack 误判）
        int atkLayer = enemy.anim.GetLayerIndex("Attack Layer");
        if (atkLayer == -1) atkLayer = 0;
        AnimatorStateInfo st = enemy.anim.GetCurrentAnimatorStateInfo(atkLayer);
        if (st.shortNameHash == emptyAttackHash) return;
        if (st.normalizedTime < interruptMinProgress) return;

        // 目标选择：距离 + 玩家低体力
        float dist = enemy.DistanceToPlayer();
        bool playerLowStamina = playerStamina != null &&
            playerStamina.CurrentStamina < playerStamina.MaxStamina * 0.3f;

        EnemyStates? target = null;
        if (dist >= interruptIaiMinDist) target = EnemyStates.IaiSlash;
        else if (playerLowStamina && dist >= interruptIaiPlayerStaminaDist) target = EnemyStates.IaiSlash;
        else if (dist >= interruptThrustMinDist) target = EnemyStates.ThrustSlash;
        if (target == null) return;

        // 同招守卫预检：目标 == 当前攻击 → ChangeState 会被 lastState 守卫吞掉，直接跳过
        State<EnemyController> cur = enemy.StateMachine.CurrentState;
        if ((target.Value == EnemyStates.IaiSlash && cur is IaiSlashState) ||
            (target.Value == EnemyStates.ThrustSlash && cur is ThrustSlashState))
            return;

        // 概率闸（狂暴度加成：满狂暴时概率 + aggressionChanceBonus）
        float aggressionRatio = enemy.maxAggression > 0f ? enemy.currentAggression / enemy.maxAggression : 0f;
        float chance = interruptChance + aggressionRatio * aggressionChanceBonus;
        if (Random.value > chance) return;

        // ── 执行打断 ──
        nextInterruptTime = Time.time + interruptCooldown;
        interruptUsedThisChain = true;        // 每条链限 1 次
        pendingInterruptMove = target;        // 旁路：新招不消费/不移除链池

        // ★ 代价：扣最大体力 10%；不足 → 扣空 + 立即力竭 + 按差值延长力竭时间（每差 1% 向上取整 +0.5s）
        if (bossStamina != null)
        {
            float cost = bossStamina.MaxStamina * interruptStaminaCostPercent;
            if (bossStamina.CurrentStamina < cost)
            {
                float diffPercent = (cost - bossStamina.CurrentStamina) / bossStamina.MaxStamina * 100f;
                int diffCeil = Mathf.CeilToInt(diffPercent);   // 零头向上取整
                ExhaustedState es = GetComponent<ExhaustedState>();
                if (es != null) es.extraDownTime = diffCeil * overdrawExhaustExtraPerPercent;
            }
            // 扣空 → OnStaminaEmpty → 力竭；攻击中 → pendingExhaustion，新招收招后第一时间倒地（优先于链衔接）
            bossStamina.ConsumeStaminaFlat(cost);
        }

        // 不清链：直接状态切换。旧攻击 Exit() 停协程/关碰撞/复位速度；新攻击 Enter 硬切动画
        combat.shouldAbortAttack = false;     // 清残留标志，避免新招被误判打断
        enemy.ChangeState(target.Value);
    }

    // ═══════════════════ 动态选招（攻击过程中按距离调整） ═══════════════════

    private EnemyStates? PickNextByDistance()
    {
        if (chainQueue.Count == 0) return null;

        float dist = enemy.DistanceToPlayer();

        // 随机捞 2 个候选（不足则全部）
        List<EnemyStates> pool = new List<EnemyStates>(chainQueue);
        List<EnemyStates> two = new List<EnemyStates>();
        int pick = Mathf.Min(2, pool.Count);
        for (int i = 0; i < pick; i++)
        {
            int idx = Random.Range(0, pool.Count);
            two.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        // 选距离匹配度最高的（更符合当前距离定位的招式）
        EnemyStates best = two[0];
        float bestScore = GetDistanceScore(best, dist);
        for (int i = 1; i < two.Count; i++)
        {
            float s = GetDistanceScore(two[i], dist);
            if (s > bestScore) { best = two[i]; bestScore = s; }
        }

        chainQueue.Remove(best);
        return best;
    }

    // 距离匹配分：招式定位距离与当前距离的契合度（越高越适合现在打出）
    private float GetDistanceScore(EnemyStates move, float dist)
    {
        switch (move)
        {
            case EnemyStates.NormalSlash:
            case EnemyStates.KanPo:
            case EnemyStates.ChargeSlash:
                // 近身逼退招：贴脸时最适合
                return dist < nearRange ? 3f : (dist < midNearRange ? 1f : 0f);
            case EnemyStates.QuickSlash:
                // 中近
                return dist >= nearRange && dist < midNearRange ? 3f : 1f;
            case EnemyStates.Combo:
                // 中距离目标
                return dist >= midNearRange && dist < midRange ? 3f : 1f;
            case EnemyStates.ThrustSlash:
                // 中远（拉近）
                return dist >= midRange && dist < midFarRange ? 3f : 1f;
            case EnemyStates.IaiSlash:
                // 远（拉近）
                return dist >= midFarRange ? 3f : 1f;
            default:
                return 0f;
        }
    }

    // ═══════════════════ 链结束 ═══════════════════

    private void EndChain()
    {
        chainActive = false;
        chainQueue.Clear();
        firstLinkPending = false;
        interruptUsedThisChain = false;   // 链结束：狂暴打断次数复位
        lastHandledMove = EnemyStates.Wait;   // 防新链首招与旧链末招相同导致防重误判
        if (enemy != null && enemy.anim != null) enemy.anim.speed = 1f;
        decisionEngine?.ResetTimer();
    }

    // ═══════════════════ 完美格挡打断（奖励机制） ═══════════════════

    /// <summary>
    /// 玩家格挡回调：isPerfect = 本次格挡是否完美（PlayerDefense 调用）
    /// </summary>
    public void OnPlayerBlocked(bool isPerfect)
    {
        if (isPerfect) consecutivePerfectBlocks++;
        else consecutivePerfectBlocks = 0;

        if (IsPhaseTwo() && consecutivePerfectBlocks >= perfectBlockBreakCount)
            BreakChain();
    }

    private void BreakChain()
    {
        consecutivePerfectBlocks = 0;

        // 先预测"下一招是否 Combo"（按当前距离 + 剩余链，清空前）→ 决定扣 20 / 50
        bool nextIsCombo = IsNextMoveCombo();
        float drain = nextIsCombo ? comboDrain : otherDrain;

        chainActive = false;
        chainQueue.Clear();
        firstLinkPending = false;
        lastHandledMove = EnemyStates.Wait;
        inBreakRecovery = true;
        if (breakRoutine != null) { StopCoroutine(breakRoutine); breakRoutine = null; }

        // 立即打断当前攻击
        combat.ForceStopAllAttacks();
        enemy.ChangeState(EnemyStates.Idle);

        // 扣架势（归零自然触发力竭）
        if (posture != null) posture.OnPerfectBlocked(drain);

        breakRoutine = StartCoroutine(BreakRecoveryRoutine());
    }

    private IEnumerator BreakRecoveryRoutine()
    {
        // 后摇：大受击动画 + 位置锁定（玩家可偷刀）
        enemy.anim.Play("Hit_Large_F", 0, 0f);   // 打断后摇：大受击动画
        Vector3 pos = enemy.transform.position;
        Quaternion rot = enemy.transform.rotation;
        float t = 0f;
        while (t < breakRecoveryDuration)
        {
            enemy.transform.position = pos;
            enemy.transform.rotation = rot;
            t += Time.deltaTime;
            yield return null;
        }
        enemy.anim.speed = 1f;
        inBreakRecovery = false;
        breakRoutine = null;
        decisionEngine?.ResetTimer();
    }

    // 预测下一招是否 Combo：当前距离在 Combo 范围（5~8m）且剩余链中还有 Combo
    private bool IsNextMoveCombo()
    {
        float dist = enemy.DistanceToPlayer();
        if (dist < midNearRange || dist >= midRange) return false;
        return chainQueue.Contains(EnemyStates.Combo);
    }

    private bool IsPhaseTwo()
    {
        BossPhaseManager pm = GetComponent<BossPhaseManager>();
        return pm != null && pm.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst;
    }

    // 洗牌
    private void Shuffle(List<EnemyStates> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
