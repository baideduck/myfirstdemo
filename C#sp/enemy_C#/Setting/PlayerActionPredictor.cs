using System.Collections.Generic;
using UnityEngine;

public enum PlayerAction { Attack, Block, Dodge, Move, None }

/// <summary>
/// 二元预测模块：记录 Phase1/2 中玩家对 Boss 出招的应对习惯，
/// Phase3 中用统计结果预测玩家下一步行为
/// </summary>
public class PlayerActionPredictor : MonoBehaviour
{
    [Header("精度递进")]
    [SerializeField] private float[] precisionThresholds = { 0.5f, 0.3f, 0.1f, 0.05f };

    // transitionCounts[response][next] = 次数
    private Dictionary<PlayerAction, Dictionary<PlayerAction, int>> transitionCounts = new();
    private int totalPairs = 0;

    private PlayerAction lastResponseAction = PlayerAction.None;
    private float lastResponseTime = -10f;
    private bool waitingForNextAction = false;

    private PlayerController playerCtrl;
    private MeeleFighter meleeFighter;
    private PlayerHealth playerHealth;
    private BossPhaseManager phaseMgr;

    // 玩家连续无伤计数（用于触发精度提升）
    private int playerNoHitStreak = 0;

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerCtrl = player.GetComponent<PlayerController>();
            meleeFighter = player.GetComponent<MeeleFighter>();
            playerHealth = player.GetComponent<PlayerHealth>();
        }
        phaseMgr = GetComponent<BossPhaseManager>();
    }

    /// <summary>
    /// 当 Boss 出招时调用，开始记录玩家的应对动作
    /// </summary>
    public void OnBossAttackStart()
    {
        waitingForNextAction = true;
        lastResponseAction = PlayerAction.None;
    }

    /// <summary>
    /// 每帧检测玩家当前动作（由 EnemyController 的 Update 调用）
    /// </summary>
    public void UpdatePlayerAction()
    {
        if (!waitingForNextAction || phaseMgr == null) return;

        PlayerAction currentAction = DetectPlayerAction();

        if (currentAction == PlayerAction.None) return;

        // 第一次检测到动作：记录为"应对动作"
        if (lastResponseAction == PlayerAction.None)
        {
            lastResponseAction = currentAction;
            lastResponseTime = Time.time;
            return;
        }

        // 当动作切换时：记录 transition[lastResponseAction → currentAction]
        if (currentAction != lastResponseAction)
        {
            RecordTransition(lastResponseAction, currentAction);
            lastResponseAction = currentAction;
            lastResponseTime = Time.time;
        }
    }

    /// <summary>
    /// Boss 攻击结束时停止记录
    /// </summary>
    public void OnBossAttackEnd()
    {
        waitingForNextAction = false;
        lastResponseAction = PlayerAction.None;
    }

    private PlayerAction DetectPlayerAction()
    {
        if (playerCtrl == null || meleeFighter == null) return PlayerAction.None;

        if (meleeFighter.InAction)
            return PlayerAction.Attack;

        if (meleeFighter.IsBlocking)
            return PlayerAction.Block;

        if (playerCtrl.IsInvincible)
            return PlayerAction.Dodge;

        if (playerCtrl.GetComponent<CharacterController>()?.velocity.magnitude > 0.1f)
            return PlayerAction.Move;

        return PlayerAction.None;
    }

    private void RecordTransition(PlayerAction from, PlayerAction to)
    {
        if (!transitionCounts.ContainsKey(from))
            transitionCounts[from] = new Dictionary<PlayerAction, int>();

        if (!transitionCounts[from].ContainsKey(to))
            transitionCounts[from][to] = 0;

        transitionCounts[from][to]++;
        totalPairs++;
    }

    // ==================== Phase3 预判接口 ====================

    /// <summary>
    /// 根据玩家当前应对动作，预测下一步行为
    /// </summary>
    public PlayerAction PredictNextAction(PlayerAction currentResponse)
    {
        if (!transitionCounts.ContainsKey(currentResponse))
            return PlayerAction.None;

        var nextActions = transitionCounts[currentResponse];

        // 带权重的预测：越近数据权重越高
        float weightFactor = 2f * (100f - GetBossHealthPercent()) / 100f;
        float threshold = precisionThresholds[Mathf.Min(playerNoHitStreak, precisionThresholds.Length - 1)];

        // 选概率最高的
        PlayerAction bestAction = PlayerAction.None;
        int highestCount = 0;
        foreach (var kvp in nextActions)
        {
            int weightedCount = kvp.Value;
            // 加上时间权重（简化：最近的数据用 weightFactor 加权）
            bestAction = (weightedCount > highestCount) ? kvp.Key : bestAction;
            highestCount = (weightedCount > highestCount) ? weightedCount : highestCount;
        }

        return bestAction;
    }

    /// <summary>
    /// 获取克制某玩家动作的招式（映射关系）
    /// </summary>
    public EnemyStates GetCounterMove(PlayerAction predictedAction)
    {
        return predictedAction switch
        {
            PlayerAction.Attack => EnemyStates.QuickSlash,   // 攻击 → 快速反击
            PlayerAction.Block => EnemyStates.KanPo,         // 格挡 → 破防
            PlayerAction.Dodge => EnemyStates.ThrustSlash,   // 闪避 → 追击
            PlayerAction.Move => EnemyStates.NormalSlash,    // 移动 → 追砍
            _ => EnemyStates.NormalSlash
        };
    }

    /// <summary>
    /// Phase3 中调用：记录这招是否打中玩家
    /// </summary>
    public void OnBossAttackHit(bool hit)
    {
        if (!hit)
        {
            playerNoHitStreak++;
        }
        else
        {
            playerNoHitStreak = 0;
        }
    }

    private float GetBossHealthPercent()
    {
        EnemyHealth health = GetComponent<EnemyHealth>();
        if (health == null) return 100f;
        return (health.currentHealth / health.maxHealth) * 100f;
    }

    /// <summary>
    /// 获取精度阈值
    /// </summary>
    public float GetCurrentPrecision()
    {
        return precisionThresholds[Mathf.Min(playerNoHitStreak, precisionThresholds.Length - 1)];
    }

    /// <summary>
    /// 检测是否应进入读指令模式（派生前 2 招都没打中玩家）
    /// </summary>
    public bool ShouldEnterReadMode(int derivedMoveCount, bool lastMoveHitPlayer)
    {
        // derivedMoveCount: 当前派生已出的招数（不计起手）
        // 前两招都没打中 → 读指令
        return derivedMoveCount <= 2 && !lastMoveHitPlayer;
    }
}
