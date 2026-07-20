using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossDecisionEngine : MonoBehaviour
{
    [Header("阶段决策系数 k")]
    [SerializeField] private float kPhaseOne = 25f;
    [SerializeField] private float kPhaseTwo = 33.33f;
    [SerializeField] private float kPhaseThree = 25f;

    [Header("抽奖参数")]
    [SerializeField] private float tickInterval = 1f;
    [SerializeField] private float M = 100f;

    [Header("记忆长度")]
    [SerializeField] private int memPhaseOne = 1;
    [SerializeField] private int memPhaseTwo = 1;
    [SerializeField] private int memPhaseThree = 2;

    [Header("招式冷却(执行次数)")]
    [SerializeField] private int iaiCooldown = 3;
    [SerializeField] private int comboCooldown = 2;
    [SerializeField] private int dodgeCdTurns = 3;

    [Header("读指令成功率(宗师)")]
    [SerializeField] private float baseReadSuccess = 1.0f;
    [SerializeField] private float successDecayPerInterrupt = 0.2f;
    [SerializeField] private float successRecoveryPerSec = 0.05f;
    [SerializeField] private float minReadSuccess = 0.4f;

    private int currentRound = 0;
    private float timer = 0f;
    private EnemyStates lastMove;
    private List<EnemyStates> recentMoves = new List<EnemyStates>();
    private Dictionary<EnemyStates, int> moveCooldown = new Dictionary<EnemyStates, int>();
    private List<EnemyStates> availableMoves = new List<EnemyStates>();
    private int dodgeRemainingCd = 0;

    private EnemyController enemy;
    private BossPhaseManager phaseMgr;
    private float currentReadSuccess;

    // 受击锁：HitReactionRoutine 结束后设置，锁定期间决策引擎不工作
    private float decisionLockedUntil = 0f;

    // 首招保证：第一次决策强制出 ThrustSlash
    private bool firstMoveGuaranteed = true;

    private void Start()
    {
        enemy = GetComponent<EnemyController>();
        phaseMgr = GetComponent<BossPhaseManager>();
        currentRound = 0;
        currentReadSuccess = baseReadSuccess;
    }

    /// <summary>
    /// 每帧由 EnemyController 在 Idle 流程中调用，返回抽奖结果
    /// 不再自己跑 Update + ChangeState
    /// </summary>
    public EnemyStates? Tick(float deltaTime)
    {
        if (enemy == null || enemy.StateMachine == null) return null;
        if (!(enemy.StateMachine.CurrentState is IdleState)) return null;
        if (enemy.isDodging) return null;
        if (enemy.isParryAnimating) return null;

        // 受击锁：锁定期间不决策
        if (Time.time < decisionLockedUntil) return null;

        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseFinalFlee) return null;

        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseThreeMaster)
        {
            currentReadSuccess = Mathf.Min(baseReadSuccess, currentReadSuccess + successRecoveryPerSec * deltaTime);
        }

        // 每 1 秒 currentRound++，然后抽奖
        timer += deltaTime;
        if (timer < tickInterval) return null;
        timer = 0f;

        currentRound++;

        // 首招保证：第一次抽奖强制出 ThrustSlash
        if (firstMoveGuaranteed)
        {
            firstMoveGuaranteed = false;
            ResetTimer();
            return EnemyStates.ThrustSlash;
        }

        return PerformLottery();
    }

    /// <summary>
    /// 锁定决策一段时间（受击后调用）
    /// </summary>
    public void LockDecision(float seconds)
    {
        decisionLockedUntil = Time.time + seconds;
    }

    /// <summary>
    /// 重置决策计时器+回合（进入 Idle 时调用）
    /// </summary>
    public void ResetTimer()
    {
        timer = 0f;
        currentRound = 0;
        decisionLockedUntil = 0f;  // 解锁
    }

    /// <summary>
    /// 强制立即决策（收刀阶段调用）
    /// </summary>
    public EnemyStates? ForceDecide() { return PerformLottery(); }

    private EnemyStates? PerformLottery()
    {
        float k = GetCurrentK();
        k += enemy.currentAggression / enemy.maxAggression * 10f;

        BuildAvailableMoves();
        if (availableMoves.Count < 2) return null;

        Dictionary<EnemyStates, float> X = new Dictionary<EnemyStates, float>();
        foreach (var move in availableMoves)
            X[move] = Random.Range(0f, M);

        List<EnemyStates> candidates = PickTwoDistinct(availableMoves);
        int memLen = GetMemoryLength();

        // 记忆屏蔽改：优先出不在记忆中的招式
        bool aInMem = IsInRecentMoves(candidates[0], memLen);
        bool bInMem = IsInRecentMoves(candidates[1], memLen);
        if (!aInMem && bInMem)
            candidates[1] = candidates[0];  // 出 a
        else if (aInMem && !bInMem)
            candidates[0] = candidates[1];  // 出 b
        else if (aInMem && bInMem)
            return null;  // 都在记忆中 → 本轮作废

        float Z = (X[candidates[0]] + X[candidates[1]]) / 2f;
        float S = k * currentRound;

        if (Z < S)
        {
            EnemyStates finalMove = SceneAdaptation(candidates[0], candidates[1]);
            UpdateHistory(finalMove);
            UpdateCooldowns();
            ResetTimer();
            return finalMove;
        }
        return null;
    }

    private float GetCurrentK()
    {
        if (phaseMgr == null) return kPhaseOne;
        return phaseMgr.CurrentPhase switch
        {
            BossPhaseManager.BossPhase.PhaseOne_Test => kPhaseOne,
            BossPhaseManager.BossPhase.PhaseTwoBurst => kPhaseTwo,
            BossPhaseManager.BossPhase.PhaseThreeMaster => kPhaseThree,
            _ => kPhaseOne
        };
    }

    private int GetMemoryLength()
    {
        if (phaseMgr == null) return memPhaseOne;
        return phaseMgr.CurrentPhase switch
        {
            BossPhaseManager.BossPhase.PhaseOne_Test => memPhaseOne,
            BossPhaseManager.BossPhase.PhaseTwoBurst => memPhaseTwo,
            BossPhaseManager.BossPhase.PhaseThreeMaster => memPhaseThree,
            _ => memPhaseOne
        };
    }

    private void BuildAvailableMoves()
    {
        availableMoves.Clear();
        EnemyStates[] all = {
            EnemyStates.NormalSlash, EnemyStates.QuickSlash, EnemyStates.Combo,
            EnemyStates.ChargeSlash, EnemyStates.KanPo, EnemyStates.IaiSlash, EnemyStates.ThrustSlash,
            EnemyStates.Dodge
        };

        foreach (var move in all)
        {
            if (moveCooldown.ContainsKey(move) && moveCooldown[move] > 0) continue;
            availableMoves.Add(move);
        }
    }

    private List<EnemyStates> PickTwoDistinct(List<EnemyStates> pool)
    {
        List<EnemyStates> temp = new List<EnemyStates>(pool);
        List<EnemyStates> res = new List<EnemyStates>();
        int i1 = Random.Range(0, temp.Count);
        res.Add(temp[i1]);
        temp.RemoveAt(i1);
        int i2 = Random.Range(0, temp.Count);
        res.Add(temp[i2]);
        return res;
    }

    private bool IsInRecentMoves(EnemyStates move, int count)
    {
        int start = Mathf.Max(0, recentMoves.Count - count);
        for (int i = start; i < recentMoves.Count; i++)
            if (recentMoves[i] == move) return true;
        return false;
    }

    private int CountInHistory(EnemyStates move)
    {
        int c = 0;
        foreach (var m in recentMoves) if (m == move) c++;
        return c;
    }

    private EnemyStates SceneAdaptation(EnemyStates a, EnemyStates b)
    {
        EnemyStates selected = PickBetterMove(a, b);

        // P1: 决策引擎不出 Dodge，Dodge 只由 Update 里的条件触发
        // if (ShouldDodgeInstead(a, b, selected))
        // {
        //     dodgeRemainingCd = dodgeCdTurns;
        //     return EnemyStates.Dodge;
        // }

        if (selected == EnemyStates.Dodge)
            dodgeRemainingCd = dodgeCdTurns;

        return selected;
    }

    private EnemyStates PickBetterMove(EnemyStates a, EnemyStates b)
    {
        float scoreA = GetMoveScore(a);
        float scoreB = GetMoveScore(b);
        if (Mathf.Abs(scoreA - scoreB) > 0.5f)
            return scoreA > scoreB ? a : b;
        return GetThreatLevel(a) > GetThreatLevel(b) ? a : b;
    }

    private float GetMoveScore(EnemyStates move)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null || enemy == null) return 0f;
        MeeleFighter mf = player.GetComponent<MeeleFighter>();
        PlayerController pc = player.GetComponent<PlayerController>();
        if (mf == null || pc == null) return 0f;
        float dist = enemy.DistanceToPlayer();
        switch (move)
        {
            case EnemyStates.KanPo:
                if (dist < 3f && mf.IsBlocking) return 2f;
                if (dist < 3f) return 1f;
                return 0f;
            case EnemyStates.ChargeSlash:
                // 近距离大范围：玩家在近处且不在闪避（格挡时KanPo优先）
                if (dist < 4f && !pc.IsInvincible) return 1.9f;
                if (dist < 5f) return 1f;
                return 0f;
            case EnemyStates.NormalSlash:
                bool playerPressuring = mf.InAction;
                bool playerChasing = dist < 4f;
                if (dist < 4f && (playerPressuring || playerChasing)) return 2f;
                if (dist < 5f) return 1f;
                return 0f;
            case EnemyStates.IaiSlash:
                BossStamina bossStamina = GetComponent<BossStamina>();
                PlayerStamina playerStamina = player.GetComponent<PlayerStamina>();
                bool bossHighStamina = bossStamina == null || bossStamina.CurrentStamina > bossStamina.MaxStamina * 0.6f;
                bool playerLowStamina = playerStamina != null && playerStamina.CurrentStamina < playerStamina.MaxStamina * 0.3f;
                if (dist > 8f && bossHighStamina && playerLowStamina) return 2f;
                if (dist > 6f) return 1f;
                return 0f;
            case EnemyStates.ThrustSlash:
                if (dist > 5f && !mf.IsBlocking) return 2f;
                if (dist > 4f) return 1f;
                return 0f;
            case EnemyStates.QuickSlash:
                PlayerStamina ps = player.GetComponent<PlayerStamina>();
                BossStamina bs = GetComponent<BossStamina>();
                bool anyLowStamina = (ps != null && ps.CurrentStamina < ps.MaxStamina * 0.3f)
                                  || (bs != null && bs.CurrentStamina < bs.MaxStamina * 0.3f);
                if (dist > 2f && dist < 7f && anyLowStamina) return 2f;
                if (dist > 2f && dist < 7f) return 1f;
                return 0f;
            case EnemyStates.Combo:
                if (dist > 3f && dist < 6f) return 2f;
                if (dist > 2f && dist < 7f) return 1f;
                return 0f;
            default:
                return 0f;
        }
    }

    private bool ShouldDodgeInstead(EnemyStates a, EnemyStates b, EnemyStates selected)
    {
        if (enemy == null) return false;

        // 条件3：不受内置CD限制，但命中后CD重置
        bool selectedIsSafe = (selected == EnemyStates.NormalSlash ||
                               selected == EnemyStates.KanPo ||
                               selected == EnemyStates.ChargeSlash);
        bool selectedJustUsed = (selected == lastMove);

        // 安全招式且没用过 → 不替换
        if (selectedIsSafe && !selectedJustUsed) return false;

        // 距离不够近 → 不替换
        float dist = enemy.DistanceToPlayer();
        if (dist >= 1.5f) return false;

        return true;
    }

    private float GetThreatLevel(EnemyStates move)
    {
        return move switch
        {
            EnemyStates.IaiSlash => 6f,
            EnemyStates.KanPo => 5f,
            EnemyStates.ChargeSlash => 4.5f,
            EnemyStates.Combo => 4f,
            EnemyStates.QuickSlash => 3f,
            EnemyStates.ThrustSlash => 2f,
            EnemyStates.NormalSlash => 1f,
            EnemyStates.Dodge => 0.1f,
            _ => 1f
        };
    }

    private EnemyStates RandomChoice(EnemyStates a, EnemyStates b) => Random.value > 0.5f ? a : b;

    private void UpdateHistory(EnemyStates move)
    {
        recentMoves.Add(move);
        lastMove = move;
        int max = GetMemoryLength() + 2;
        while (recentMoves.Count > max) recentMoves.RemoveAt(0);
    }

    private void UpdateCooldowns()
    {
        if (dodgeRemainingCd > 0) dodgeRemainingCd--;

        List<EnemyStates> keys = new List<EnemyStates>(moveCooldown.Keys);
        foreach (var k in keys)
        {
            moveCooldown[k]--;
            if (moveCooldown[k] <= 0) moveCooldown.Remove(k);
        }
        if (lastMove == EnemyStates.IaiSlash) moveCooldown[EnemyStates.IaiSlash] = iaiCooldown;
        if (lastMove == EnemyStates.Combo) moveCooldown[EnemyStates.Combo] = comboCooldown;
        if (lastMove == EnemyStates.Dodge) dodgeRemainingCd = dodgeCdTurns;
    }

    public void ResetEngine()
    {
        currentRound = 0;
        timer = 0f;
        recentMoves.Clear();
        moveCooldown.Clear();
        dodgeRemainingCd = 0;
        currentReadSuccess = baseReadSuccess;
    }

    public void OnPlayerInterrupt()
    {
        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseThreeMaster)
        {
            currentReadSuccess = Mathf.Max(minReadSuccess, currentReadSuccess - successDecayPerInterrupt);
        }
    }
}
