using UnityEngine;

public class EnemyWeapon : MonoBehaviour
{
    private EnemyController enemy;
    private GameObject owner;
    private GameObject player;  // 缓存玩家引用
    private int lastHitFrame = -1;
    private GameObject lastHitTarget;

    // Combo 专用的 0.3s 冷却常量
    private const float COMBO_COOLDOWN = 0.3f;

    private void Awake()
    {
        enemy = GetComponentInParent<EnemyController>();
        owner = enemy ? enemy.gameObject : null;
        player = GameObject.FindGameObjectWithTag("Player");
    }

    public void ResetHitState()
    {
        lastHitFrame = -1;
        lastHitTarget = null;
    }

    private void OnEnable()
    {
        lastHitFrame = -1;
        lastHitTarget = null;
    }

    // ★ 由当前判定窗口的招式决定（开窗时记录，而非命中时状态）：
    //   修复派生链衔接瞬间状态已切到 Combo/Quick → 命中时误判为 Combo → 单次锁失效的竞态
    private bool IsCurrentStateComboLike()
    {
        return enemy != null && enemy.combat != null && enemy.combat.IsComboLikeWindow;
    }

    /// <summary>
    /// 每帧距离检测——作为物理碰撞的保险（纯数学计算，不依赖物理帧率）
    /// </summary>
    private void Update()
    {
        if (enemy == null || !enabled) return;
        if (!gameObject.activeSelf) return;  // 碰撞体没激活就不检测
        if (enemy.combat == null) return;    // EnemyCombat 未挂载，跳过
        if (!enemy.canHitThisAttack) return;  // 本招已经命中过了
        // ★ Combo 窗口：0.3s 冷却限速（物理碰撞路径已有该检查，Update 距离检测路径补充——
        //   否则 Combo/Quick 在 2m 内每帧判定伤害，远超"多次"预期）
        if (enemy.combat.IsComboLikeWindow && Time.time - enemy.lastDamageTime < COMBO_COOLDOWN)
            return;
        if (enemy.StateMachine == null) return;

        // 只在攻击状态下检测
        bool isAttack = enemy.StateMachine.CurrentState is NormalSlashState
                     || enemy.StateMachine.CurrentState is QuickSlashState
                     || enemy.StateMachine.CurrentState is ChargeSlashState
                     || enemy.StateMachine.CurrentState is SlashState
                     || enemy.StateMachine.CurrentState is IaiSlashState
                     || enemy.StateMachine.CurrentState is ThrustSlashState;
        if (!isAttack && !IsCurrentStateComboLike()) return;

        // 纯距离检测：武器碰撞体位置到玩家的距离 < 2m
        if (player == null) { Debug.LogError("[EnemyWeapon] player is null! Tag might be wrong."); return; }
        float dist = Vector3.Distance(transform.position, player.transform.position);
        if (dist > 2f)
        {
            // 武器原点超过 8m，用碰撞体表面最近点再查一次
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                Vector3 closestPoint = col.ClosestPoint(player.transform.position);
                float surfaceDist = Vector3.Distance(closestPoint, player.transform.position);
                if (surfaceDist > 2f) return;
            }
            else return;
        }

        PlayerDefense defense = player.GetComponent<PlayerDefense>();
        if (defense == null) return;

        int damage = enemy.currentAttackDamage;
        bool isGuardBreak = GetIsGuardBreak();

        bool hitLanded = defense.ProcessEnemyAttack(damage, isGuardBreak, player.transform.position);
        if (hitLanded)
        {
            if (IsCurrentStateComboLike())
                enemy.lastDamageTime = Time.time;
            else
            {
                // ★ 非 Combo：命中即锁 + 立即关窗——物理禁用碰撞体，杜绝同一招式窗口内任何二次命中
                enemy.canHitThisAttack = false;
                enemy.lastDamageTime = Time.time;
                enemy.combat.EnableWeaponHitBox(false, false);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (enemy == null || owner != null && other.transform.root == owner.transform)
            return;

        // 安全检查：必须处于攻击状态
        if (enemy.StateMachine == null) return;
        bool isAttackState = enemy.StateMachine.CurrentState is NormalSlashState ||
                             enemy.StateMachine.CurrentState is QuickSlashState ||
                             enemy.StateMachine.CurrentState is ComboState ||
                             enemy.StateMachine.CurrentState is ChargeSlashState ||
                             enemy.StateMachine.CurrentState is SlashState ||
                             enemy.StateMachine.CurrentState is IaiSlashState ||
                             enemy.StateMachine.CurrentState is ThrustSlashState;
        if (!isAttackState)
        {
            return;
        }

        bool isComboLike = IsCurrentStateComboLike();

        // ── 伤害锁判断 ──
        if (isComboLike)
        {
            // Combo: 0.3s 冷却，允许同一窗口内多次命中
            if (Time.time - enemy.lastDamageTime < COMBO_COOLDOWN)
                return;
        }
        else
        {
            // 非 Combo: 单次锁，命中或格挡后不再造成伤害/音效
            if (!enemy.canHitThisAttack)
                return;
        }
        int otherLayer = other.gameObject.layer;
        int playerLayer = LayerMask.NameToLayer("Player");
        int playerHitboxLayer = LayerMask.NameToLayer("PlayerHitbox");
        if (otherLayer != playerLayer && otherLayer != playerHitboxLayer) return;

        GameObject targetRoot = other.transform.root.gameObject;

        // 同一帧同目标去重
        if (lastHitFrame == Time.frameCount && lastHitTarget == targetRoot)
        {
            return;
        }

        PlayerDefense defense = other.GetComponentInParent<PlayerDefense>();
        if (defense == null) return;

        int damage = enemy.currentAttackDamage;
        bool isGuardBreak = GetIsGuardBreak();
        Vector3 hitPoint = other.ClosestPoint(transform.position);

        lastHitFrame = Time.frameCount;
        lastHitTarget = targetRoot;

        // 调用玩家防御处理，返回 true 表示实际造成了伤害/格挡
        bool hitLanded = defense.ProcessEnemyAttack(damage, isGuardBreak, hitPoint);

        if (hitLanded)
        {
            if (isComboLike)
            {
                // Combo/QuickSlash: 刷新 0.3s 冷却时间戳，允许连续命中
                enemy.lastDamageTime = Time.time;
            }
            else
            {
                // ★ 非 Combo：命中即锁 + 立即关窗——物理禁用碰撞体，杜绝同一招式窗口内任何二次命中
                enemy.canHitThisAttack = false;
                enemy.lastDamageTime = Time.time;
                enemy.combat.EnableWeaponHitBox(false, false);
            }
        }
    }

    private bool GetIsGuardBreak()
    {
        if (enemy != null && enemy.StateMachine != null && enemy.StateMachine.CurrentState is SlashState slash)
            return slash.isGuardBreak;
        return false;
    }
}