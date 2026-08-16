using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 战斗层 —— 武器、受击、霸体、攻击管理
/// 从 EnemyController 中提取，与状态机/AI 完全解耦
/// </summary>
public class EnemyCombat : MonoBehaviour
{
    // ═══════════════════════ 引用 ═══════════════════════
    [HideInInspector] public Animator Anim;
    [HideInInspector] public EnemyController Controller;
    private BossEvents events;
    private EnemyHealth health;
    private BossPhaseManager phaseMgr;
    private BossDecisionEngine decisionEngine;
    private BossStamina stamina;
    private BossPosture posture;

    // ═══════════════════════ 武器 ═══════════════════════
    [Header("Weapon")]
    public GameObject weaponModel;
    public GameObject weaponHitBox;
    public GameObject sheathModel;
    public Transform sheathPoint;
    public Transform handBone;
    [HideInInspector] public bool lockWeaponInHand;
    // ★ 持刀位偏移（相对 handBone 的 local 值）：默认 0 = 原始 bind pose（即当前"位置对了"的状态）。
    //   手动调整刀姿：在场景里把刀摆到想要的持刀位置 → 右键 EnemyCombat 组件 → Capture Hand Offset 自动写入。
    [Header("Hand Offset (右键 Capture Hand Offset 写入)")]
    public Vector3 handOffsetPos = Vector3.zero;
    public Vector3 handOffsetRotEuler = Vector3.zero;
    private Vector3 weaponWorldScale = Vector3.one;   // 刀的原始世界缩放（挂手/跟随/收刀时保持）

    // 碰撞体膨胀记录
    private bool hitboxInflated;
    private bool originalHitboxSaved;        // 是否已保存原始碰撞体尺寸
    private Vector3 hitboxOriginalSize;
    private float hitboxOriginalRadius;
    private float hitboxOriginalHeight;

    // ═══════════════════════ 伤害 ═══════════════════════
    [Header("Damage")]
    public float damageCooldown = 0.2f;
    [HideInInspector] public int currentAttackDamage;

    /// <summary>
    /// ★ 设置攻击伤害（阶段4接线）：二阶段（PhaseTwoBurst）伤害 × phaseTwoDamageMult(1.3)
    /// </summary>
    public void SetAttackDamage(int baseDamage)
    {
        BossPhaseManager pm = GetComponent<BossPhaseManager>();
        float mult = (pm != null && pm.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst) ? pm.PhaseTwoDamageMult : 1f;
        currentAttackDamage = Mathf.RoundToInt(baseDamage * mult);
    }
    [HideInInspector] public float lastDamageTime = -1f;
    [HideInInspector] public bool canHitThisAttack = true;

    // ★ 当前判定窗口是否 ComboLike（开窗时由"出招的招式"决定，命中时不再看当前状态——
    //   修复派生链衔接瞬间状态已切换导致单次锁失效的竞态）
    [HideInInspector] public bool isComboLikeWindow = false;
    public bool IsComboLikeWindow => isComboLikeWindow;

    // ═══════════════════════ 攻击管理 ═══════════════════════
    [HideInInspector] public bool shouldAbortAttack;
    [HideInInspector] public bool HasSuperArmor;
    private Coroutine currentAttackRoutine;
    private Coroutine hitReactionCoroutine;
    private float lastHitStopDuration;

    // ═══════════════════════ 受击追踪 ═══════════════════════
    [HideInInspector] public int consecutiveHits;
    [HideInInspector] public int interruptedAttackCount;
    [HideInInspector] public bool isParryAnimating;
    [HideInInspector] public bool pendingExhaustion;
    [HideInInspector] public bool isPostureBreakExhaust;

    // ═══════════════════════ 连招 ═══════════════════════
    [HideInInspector] public int comboChainCount;
    [HideInInspector] public bool isInComboChain;
    [HideInInspector] public bool comboInterrupted;
    [HideInInspector] public int derivedMoveCount;
    [HideInInspector] public bool lastDerivedMoveHitPlayer;

    // ═══════════════════════ 居合 ═══════════════════════
    [HideInInspector] public bool iaiAwakened;
    [HideInInspector] public bool iaiUsed;

    // ═══════════════════════ AI 动态 ═══════════════════════
    [HideInInspector] public float decisionSpeedMultiplier = 1f;

    // ═══ 派生链衔接（BossComboChain 驱动）═══
    [HideInInspector] public bool isDerivedMove;      // 本招收刀是否走派生衔接（收刀慢放+决策）
    public bool derivedMoveLinked;                    // 收刀衔接已切换下一招（攻击状态据此提前结束）
    private BossComboChain chain;
    [HideInInspector] public float frequencyMultiplier = 1f;

    // 常量
    private const string SHEATH_OBJECT_NAME = "Katana_sheath";
    private const float BACK_ATTACK_THRESHOLD = -0.3f; // 背面攻击判定阈值

    // ═══════════════════════ 初始化 ═══════════════════════
    void Awake()
    {
        Controller = GetComponent<EnemyController>();
        Anim = GetComponent<Animator>();
        events = GetComponent<BossEvents>();
        health = GetComponent<EnemyHealth>();
        phaseMgr = GetComponent<BossPhaseManager>();
        decisionEngine = GetComponent<BossDecisionEngine>();
        stamina = GetComponent<BossStamina>();
        posture = GetComponent<BossPosture>();
        chain = GetComponent<BossComboChain>();
        EnsureWeaponRefs();   // ★ 引用兜底：Inspector 引用缺失时按骨骼名查找，确保挂刀/收刀链路生效
    }

    private void LateUpdate()
    {
        // ★ 武器强制跟随手部挂点：Animator 的 Write Defaults/骨骼动画会在状态切换时把刀的 Transform 重置回绑定姿势（刀鞘），
        //   这里在 Animator 更新后强制把刀锁到挂点世界位置，确保 Block_L/R 等动画播放时刀稳定在手上。
        if (lockWeaponInHand && weaponModel != null && handBone != null)
        {
            // ★ 应用持刀位偏移（默认 0 = bind pose；Capture Hand Offset 可写入手动调整的刀姿）
            weaponModel.transform.position = handBone.TransformPoint(handOffsetPos);
            weaponModel.transform.rotation = handBone.rotation * Quaternion.Euler(handOffsetRotEuler);
            // 保持刀的世界缩放，防止 Animator 重置 localScale 导致刀变形（SafeDivide 防骨骼缩放异常时除零/NaN 拉伸）
            Vector3 hs = handBone.lossyScale;
            Vector3 ls = new Vector3(
                SafeDivide(weaponWorldScale.x, hs.x),
                SafeDivide(weaponWorldScale.y, hs.y),
                SafeDivide(weaponWorldScale.z, hs.z));
            // 写入前校验：结果含 NaN/Infinity 则跳过本次锁刀（防止把坏 scale 写进刀 transform 造成拉伸/消失）
            if (!float.IsNaN(ls.x) && !float.IsInfinity(ls.x) &&
                !float.IsNaN(ls.y) && !float.IsInfinity(ls.y) &&
                !float.IsNaN(ls.z) && !float.IsInfinity(ls.z))
                weaponModel.transform.localScale = ls;
        }
    }

    /// <summary>
    /// 武器/骨骼引用兜底：无条件按名强制查找正确挂点（不依赖 Inspector 序列化引用）。
    /// 手部挂点 = hand_r（用户确认：攻击动画时刀挂在 hand_r 下，没有更具体的挂点）；
    /// 刀模型 = Katana_sword；刀鞘挂点 = Katana_sheath_01（用户确认）。
    /// </summary>
    private void EnsureWeaponRefs()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);

        // ★（临时回退版本）手部挂点用 Katana_weapon_01 —— 用于复制该状态下刀的组件（Transform）位置
        foreach (Transform t in all)
            if (t.name == "Katana_weapon_01") { handBone = t; break; }
        if (handBone == null)
            foreach (Transform t in all)
                if (t.name == "hand_r") { handBone = t; break; }

        foreach (Transform t in all)
            if (t.name == "Katana_sword") { weaponModel = t.gameObject; break; }
        foreach (Transform t in all)
            if (t.name == "Katana_sheath_01") { sheathPoint = t; break; }

        // 兜底
        if (sheathPoint == null)
            foreach (Transform t in all)
                if (t.name == "Katana_sheath") { sheathPoint = t; break; }

        if (weaponModel == null || handBone == null)
        { /* 挂点缺失：不挂刀（日志已清理） */ }

        // 记录刀的原始世界缩放（挂手/跟随/收刀时保持，防止父级 scale 变化导致刀变形）
        if (weaponModel != null)
        {
            Vector3 ws = weaponModel.transform.lossyScale;
            // 兜底：记录到异常值（0 / NaN，如动画未就绪）时用 1，避免后续挂刀 localScale 变 0 或 Infinity
            weaponWorldScale = (ws.x == 0f || float.IsNaN(ws.x)) ? Vector3.one : ws;
        }
    }

    public void Init(EnemyController controller)
    {
        Controller = controller;
        Anim = controller.anim;
    }

    // ═══ 派生链衔接转发（收刀阶段由攻击状态调用）═══
    public float DerivedRecoveryBuffer => chain != null ? chain.linkRecoveryBuffer : 0.9f;
    // ★ 连招衔接点（攻击动画播到此比例即接下一招）：全局唯一入口在 BossComboChain.linkTransitionNormalized
    public float LinkTransitionNormalized => chain != null ? chain.linkTransitionNormalized : 0.9f;

    // ══════════════════════════════════════
    //  武器挂载
    // ══════════════════════════════════════
    #region Weapon Mounting

    public void AttachWeaponToHand()
    {
        if (weaponModel == null || handBone == null)
        {
            return;
        }
        // ★ 保持刀的世界缩放：SetParent 后若父级（hand_r）scale≠1，刀会被继承放大/缩小（"scale 变抽象"）
        Vector3 worldScale = weaponWorldScale == Vector3.one ? weaponModel.transform.lossyScale : weaponWorldScale;
        Vector3 parentScale = handBone.lossyScale;   // SetParent 前记录父级缩放
        weaponModel.transform.SetParent(handBone);
        weaponModel.transform.localPosition = handOffsetPos;                 // ★ 持刀位偏移（默认 0 = bind pose）
        weaponModel.transform.localRotation = Quaternion.Euler(handOffsetRotEuler);
        weaponModel.transform.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z));
    }

    // 安全除法：父级缩放为 0 / NaN / Infinity（骨骼动画未就绪、缩放曲线为 0、IK 骨骼）时回退 1，
    // 防止除零/NaN 传播产生 Infinity → 刀模型被拉伸到无限大（"游戏开始刀拉伸"根因）
    private float SafeDivide(float world, float parent)
    {
        if (parent == 0f || float.IsNaN(parent) || float.IsInfinity(parent)) return 1f;
        return world / parent;
    }

    /// <summary>
    /// 手动调整持刀姿态后调用：把刀相对 handBone 的偏移写入 handOffsetPos / handOffsetRotEuler。
    /// 编辑模式和运行模式都可用（引用缺失时自动按名查找）。此后 AttachWeaponToHand 与 LateUpdate 锁刀都会应用该偏移。
    /// </summary>
    [ContextMenu("Capture Hand Offset")]
    private void CaptureHandOffset()
    {
        Transform wm = weaponModel != null ? weaponModel.transform : FindWeaponChild("Katana_sword");
        Transform hb = handBone != null ? handBone : FindWeaponChild("Katana_weapon_01");
        if (wm == null || hb == null)
        {
            return;
        }
        handOffsetPos = hb.InverseTransformPoint(wm.position);
        handOffsetRotEuler = (Quaternion.Inverse(hb.rotation) * wm.rotation).eulerAngles;
    }

    private Transform FindWeaponChild(string name)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    public void AttachWeaponToSheath()
    {
        if (lockWeaponInHand) return;
        if (weaponModel == null || sheathPoint == null) return;
        // ★ 保持刀的世界缩放（同挂手逻辑，防止收刀后 scale 变形）
        Vector3 worldScale = weaponWorldScale == Vector3.one ? weaponModel.transform.lossyScale : weaponWorldScale;
        Vector3 parentScale = sheathPoint.lossyScale;
        weaponModel.transform.SetParent(sheathPoint);
        weaponModel.transform.localPosition = Vector3.zero;
        weaponModel.transform.localRotation = Quaternion.identity;
        weaponModel.transform.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z));
        weaponModel.SetActive(true);
    }

    public void ForceWeaponToSheath()
    {
        if (lockWeaponInHand) return;
        if (weaponModel == null) return;
        weaponModel.SetActive(true);
        if (sheathPoint != null)
        {
            Vector3 worldScale = weaponWorldScale == Vector3.one ? weaponModel.transform.lossyScale : weaponWorldScale;
            Vector3 parentScale = sheathPoint.lossyScale;
            weaponModel.transform.SetParent(sheathPoint);
            weaponModel.transform.localPosition = Vector3.zero;
            weaponModel.transform.localRotation = Quaternion.identity;
            weaponModel.transform.localScale = new Vector3(
                SafeDivide(worldScale.x, parentScale.x),
                SafeDivide(worldScale.y, parentScale.y),
                SafeDivide(worldScale.z, parentScale.z));
        }
    }

    public void HideAllWeaponModels()
    {
        if (weaponModel != null)
        {
            foreach (Renderer r in weaponModel.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }
        GameObject s = sheathModel;
        if (s == null)
        {
            Transform found = transform.Find(SHEATH_OBJECT_NAME);
            if (found != null) s = found.gameObject;
        }
        if (s != null)
        {
            foreach (Renderer r in s.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }
    }

    public void ShowAllWeaponModels()
    {
        if (weaponModel != null)
        {
            AttachWeaponToHand();
            foreach (Renderer r in weaponModel.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
        }
        GameObject s = sheathModel;
        if (s == null)
        {
            Transform found = transform.Find(SHEATH_OBJECT_NAME);
            if (found != null) s = found.gameObject;
        }
        if (s != null)
        {
            foreach (Renderer r in s.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
        }
    }

    #endregion

    // ══════════════════════════════════════
    //  武器碰撞体
    // ══════════════════════════════════════
    #region Weapon Hitbox

    public void EnableWeaponHitBox(bool enable, bool unblockable)
    {
        if (weaponHitBox == null)
        {
            Debug.LogError("[EnemyCombat] weaponHitBox is null! Cannot enable hitbox.");
            return;
        }

        if (enable)
        {
            if (Controller == null || Controller.StateMachine == null || Controller.StateMachine.CurrentState == null)
            {
                Debug.LogError("[EnemyCombat] State null when enabling hitbox");
                return;
            }
            if (!IsAttackState(Controller.StateMachine.CurrentState))
            {
                Debug.LogWarning($"[EnemyCombat] Not attack state: {Controller.StateMachine.CurrentState.GetType().Name} - hitbox NOT enabled");
                return;
            }

            if (weaponModel != null) weaponModel.SetActive(true);
            EnemyWeapon weapon = weaponHitBox.GetComponent<EnemyWeapon>();
            if (weapon != null)
            {
                weapon.ResetHitState();
                weapon.enabled = true;
            }

            // 先恢复原始尺寸，再基于原始值膨胀，避免累积
            RestoreHitboxCollider();
            InflateHitboxCollider(1.5f);

            // ★ 记录本次判定窗口的招式类型：Combo/Quick 允许多次命中（0.3s 冷却），其余单次锁
            isComboLikeWindow = Controller.StateMachine.CurrentState is ComboState ||
                                Controller.StateMachine.CurrentState is QuickSlashState;
            canHitThisAttack = true;
            lastDamageTime = -10f;
            weaponHitBox.SetActive(true);
        }
        else
        {
            RestoreHitboxCollider();   // 关闭前恢复原始尺寸
            lastDamageTime = -10f;
            if (weaponHitBox.activeSelf)
                weaponHitBox.SetActive(false);
        }
    }

    private void InflateHitboxCollider(float scale)
    {
        if (weaponHitBox == null) return;
        Collider col = weaponHitBox.GetComponent<Collider>();
        if (col == null) return;

        // 仅在第一次膨胀时保存原始尺寸
        if (!originalHitboxSaved)
        {
            if (col is BoxCollider box) hitboxOriginalSize = box.size;
            else if (col is SphereCollider sphere) hitboxOriginalRadius = sphere.radius;
            else if (col is CapsuleCollider capsule) { hitboxOriginalRadius = capsule.radius; hitboxOriginalHeight = capsule.height; }
            originalHitboxSaved = true;
        }

        // 执行膨胀
        if (col is BoxCollider box2) box2.size *= scale;
        else if (col is SphereCollider sphere2) sphere2.radius *= scale;
        else if (col is CapsuleCollider capsule2) { capsule2.radius *= scale; capsule2.height *= scale; }

        hitboxInflated = true;
    }

    private void RestoreHitboxCollider()
    {
        if (!hitboxInflated || weaponHitBox == null) return;
        Collider col = weaponHitBox.GetComponent<Collider>();
        if (col == null) return;

        if (col is BoxCollider box) box.size = hitboxOriginalSize;
        else if (col is SphereCollider sphere) sphere.radius = hitboxOriginalRadius;
        else if (col is CapsuleCollider capsule) { capsule.radius = hitboxOriginalRadius; capsule.height = hitboxOriginalHeight; }

        hitboxInflated = false;
        // 注意：originalHitboxSaved 保持 true，确保下次膨胀不会丢失真正的原始值
    }

    #endregion

    // ══════════════════════════════════════
    //  受击反应
    // ══════════════════════════════════════
    #region Hit Reaction

    public void PlayHitReaction(Vector3 hitDirection, bool isHeavy = false, float hitStopDuration = 0.1f)
    {
        if (Anim == null) return;
        if (Controller.StateMachine == null || Controller.StateMachine.CurrentState is ExhaustedState)
        {
            return;
        }

        // Phase3 master: combo interrupted
        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseThreeMaster && isInComboChain)
        {
            comboInterrupted = true;
            shouldAbortAttack = true;
            ForceStopAllAttacks();
            EndComboChain();
            decisionEngine?.OnPlayerInterrupt();
            return;
        }

        // ⚠️ 霸体已彻底移除（AttackStateBase 不再设置 HasSuperArmor），受击不再被拦截

        consecutiveHits++;

        if (!isHeavy)
        {
            // ★ 轻击：不打断攻击、不播受击动画——只做同步顿帧（卡肉感），Boss 顶着刀继续出招。
            //   （链保留、攻击协程保留、shouldAbortAttack 不动；Hit Layer 不参与，避免受击动画造成"被打停"观感）
            if (hitReactionCoroutine != null)
            {
                StopCoroutine(hitReactionCoroutine);
                hitReactionCoroutine = null;
            }
            lastHitStopDuration = hitStopDuration;
            hitReactionCoroutine = StartCoroutine(LightHitRoutine());
            return;
        }

        // ★ 重击/蓄力命中：完整打断——停攻击、清链、受击大硬直（玩家用重击赢取回合）
        shouldAbortAttack = true;
        ForceStopAllAttacks();
        events?.FireHitTaken();

        if (hitReactionCoroutine != null)
        {
            StopCoroutine(hitReactionCoroutine);
            int oldHitLayer = Anim.GetLayerIndex("Hit Layer");
            if (oldHitLayer != -1) Anim.SetLayerWeight(oldHitLayer, 0f);
        }

        string animName = GetHitAnimationName(hitDirection, true);
        if (string.IsNullOrEmpty(animName)) return;

        int hitLayer = Anim.GetLayerIndex("Hit Layer");
        if (hitLayer != -1)
        {
            Anim.SetLayerWeight(hitLayer, 1f);
            if (Anim.HasState(hitLayer, Animator.StringToHash("Idle")))
            {
                Anim.Play("Idle", hitLayer, 0f);
                Anim.Update(0f);
            }
            Anim.Play(animName, hitLayer, 0f);
        }
        else
        {
            if (Anim.HasState(0, Animator.StringToHash("Idle")))
            {
                Anim.Play("Idle", 0, 0f);
                Anim.Update(0f);
            }
            Anim.Play(animName, 0, 0f);
        }
        Anim.Update(0f);

        Anim.speed = 1f;
        lastHitStopDuration = hitStopDuration;
        hitReactionCoroutine = StartCoroutine(HitReactionRoutine(animName));
    }

    /// <summary>
    /// 轻击顿帧：动画短冻结（同步卡肉），不播受击动画、不打断当前攻击。
    /// 播完不触发 FireRecoveredFromHit / 不 LockDecision（Boss 仍在出招流程中）。
    /// </summary>
    private IEnumerator LightHitRoutine()
    {
        yield return null;
        Anim.speed = 0f;
        yield return new WaitForSecondsRealtime(lastHitStopDuration);
        Anim.speed = 1f;
        hitReactionCoroutine = null;
        lastHitStopDuration = 0f;
    }

    /// <summary>
    /// 硬直节拍（BossPosture 跨档触发）：完整打断当前攻击（清链），播短受击动画 + 顿帧 + 短停顿。
    /// 玩家持续输出赢取的小窗口；Boss 回 Idle 后重新布局。
    /// </summary>
    public void PlayFlinch(float hitStopDuration = 0.12f)
    {
        if (Anim == null) return;
        if (Controller.StateMachine == null || Controller.StateMachine.CurrentState is ExhaustedState) return;

        shouldAbortAttack = true;
        ForceStopAllAttacks();     // 停攻击 + 断链（FireAttackInterrupted → ChangeState(Idle)）
        consecutiveHits++;
        events?.FireHitTaken();

        if (hitReactionCoroutine != null)
        {
            StopCoroutine(hitReactionCoroutine);
            int oldHitLayer = Anim.GetLayerIndex("Hit Layer");
            if (oldHitLayer != -1) Anim.SetLayerWeight(oldHitLayer, 0f);
        }

        // 受击方向：面向玩家（短受击动画 Hit_F/B/L/R）
        Vector3 toPlayer = Controller.GetPlayerPosition() - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) toPlayer = transform.forward;
        string animName = GetHitAnimationName(toPlayer, false);
        if (string.IsNullOrEmpty(animName)) return;

        int hitLayer = Anim.GetLayerIndex("Hit Layer");
        if (hitLayer != -1)
        {
            Anim.SetLayerWeight(hitLayer, 1f);
            if (Anim.HasState(hitLayer, Animator.StringToHash("Idle")))
            {
                Anim.Play("Idle", hitLayer, 0f);
                Anim.Update(0f);
            }
            Anim.Play(animName, hitLayer, 0f);
        }
        else
        {
            Anim.Play(animName, 0, 0f);
        }
        Anim.Update(0f);

        lastHitStopDuration = hitStopDuration;
        hitReactionCoroutine = StartCoroutine(HitReactionRoutine(animName));
    }

    public void PlayParryReaction(Vector3 attackDirection)
    {
        if (Controller.StateMachine == null) return;
        if (Controller.StateMachine.CurrentState is ExhaustedState) return;

        Vector3 localDir = transform.InverseTransformDirection(attackDirection.normalized);
        // 背面攻击无法格挡，转为普通受击
        if (localDir.z < BACK_ATTACK_THRESHOLD)
        {
            PlayHitReaction(attackDirection, false, 0.1f);
            return;
        }

        bool isUninterruptible = Controller.StateMachine.CurrentState is QuickSlashState
                              || Controller.StateMachine.CurrentState is IaiSlashState
                              || Controller.StateMachine.CurrentState is SlashState;

        if (!isUninterruptible)
        {
            shouldAbortAttack = true;
            ForceStopAllAttacks();
            State<EnemyController> curState = Controller.StateMachine.CurrentState;
            if (curState is MonoBehaviour mb) { mb.enabled = false; mb.StopAllCoroutines(); }
            StopAllCoroutines();
            Controller.ChangeState(EnemyStates.Idle);
        }
        else
        {
            shouldAbortAttack = true;
            ForceStopAllAttacks();
            EnableWeaponHitBox(false, false);
            if (Anim != null)
            {
                int al = Anim.GetLayerIndex("Attack Layer");
                if (al != -1) Anim.SetLayerWeight(al, 0f);
            }
            // ★ 状态机归位兜底：isUninterruptible 分支不切状态，攻击状态残留会在下一帧被 Execute 检测到并强切 Idle（anim.Play("Idle") 掐断 Block_L/R）
            if (!(Controller.StateMachine.CurrentState is IdleState) && !(Controller.StateMachine.CurrentState is ExhaustedState))
                Controller.ChangeState(EnemyStates.Idle);
        }

        // ★ 复位中断标志：ForceStopAllAttacks 已把攻击协程全部停掉，标志消费完毕；
        //   不清除的话 BlockHitRoutine 第 1 帧的 shouldAbortAttack 检查会把刚触发的格挡演出误判为"被打断"而提前掐断
        shouldAbortAttack = false;

        int blockDir = localDir.x > 0 ? 1 : 0;
        if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
        isParryAnimating = true;
        hitReactionCoroutine = StartCoroutine(BlockHitRoutine(blockDir));
    }

    public void PlayGuardBreakReaction(Vector3 attackDirection)
    {
        if (Controller.StateMachine == null || Controller.StateMachine.CurrentState is ExhaustedState) return;
        if (Anim == null) return;

        // ★ 完整攻击中断链路：设 shouldAbortAttack + 停攻击协程 + 关武器碰撞 + 清攻击层 + 触发中断事件
        shouldAbortAttack = true;
        ForceStopAllAttacks();

        // ★ 复位中断标志：ForceStopAllAttacks 已把攻击协程全部停掉，标志消费完毕。
        //   若不清除，PerfectBlockRoutine 第 1 帧会把刚触发的 Block_L/R 误判为"被打断"→ 提前 FinishPerfectBlock → 解锁收刀，
        //   弹刀动画几乎不播、刀也不在手。复位后该标志只对"格挡期间新到来的中断"生效。
        shouldAbortAttack = false;

        // ★ 状态机归位兜底：正常路径 ForceStopAllAttacks → FireAttackInterrupted → 已同步 ChangeState(Idle)；
        //   若事件链缺失，攻击状态残留会在下一帧 Execute 检测到协程被杀 → 强切 Idle 掐断 Block_L/R。力竭除外（力竭优先）。
        if (!(Controller.StateMachine.CurrentState is IdleState) && !(Controller.StateMachine.CurrentState is ExhaustedState))
            Controller.ChangeState(EnemyStates.Idle);

        // ★ 完美格挡弹刀动画：播 Block_L / Block_R（按攻击方向分左右），播完自动回 Idle（PerfectBlockRoutine）
        Vector3 localDir = transform.InverseTransformDirection(attackDirection.normalized);
        int blockDir = localDir.x > 0 ? 1 : 0;
        if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
        isParryAnimating = true;
        hitReactionCoroutine = StartCoroutine(PerfectBlockRoutine(blockDir));
    }

    private IEnumerator HitReactionRoutine(string animName)
    {
        int hitLayer = Anim.GetLayerIndex("Hit Layer");
        int checkLayer = hitLayer != -1 ? hitLayer : 0;
        AnimatorStateInfo state = Anim.GetCurrentAnimatorStateInfo(checkLayer);
        float length = state.IsName(animName) ? state.length : 0.5f;

        if (lastHitStopDuration > 0f)
        {
            yield return null;
            Anim.speed = 0f;
            yield return new WaitForSecondsRealtime(lastHitStopDuration);
            Anim.speed = 1f;
        }

        yield return new WaitForSeconds(length);

        hitReactionCoroutine = null;
        Anim.speed = 1f;
        if (hitLayer != -1) Anim.SetLayerWeight(hitLayer, 0f);

        events?.FireRecoveredFromHit();

        BossDecisionEngine de = GetComponent<BossDecisionEngine>();
        if (de != null) de.LockDecision(0.5f);

        lastHitStopDuration = 0f;   // 重置，防止下次误用
    }

    private IEnumerator BlockHitRoutine(int blockDirection)
    {
        if (Anim == null) { isParryAnimating = false; yield break; }
        AttachWeaponToHand();
        lockWeaponInHand = true;   // ★ Block 动画播放期间锁定武器在手，防止被收刀回鞘

        Anim.speed = 0.9f;
        Anim.SetInteger("BlockDirection", blockDirection);
        Anim.SetTrigger("BlockHit");
        yield return null;
        if (shouldAbortAttack) yield break;

        AnimatorStateInfo blockState = Anim.GetCurrentAnimatorStateInfo(0);
        float bl = (blockState.IsName("Block_L") || blockState.IsName("Block_R")) ? blockState.length : 0.5f;
        yield return new WaitForSeconds(bl / Anim.speed);
        if (shouldAbortAttack) yield break;

        Anim.speed = 0.9f;
        Anim.Play("Hit_Large_F", 0, 0f);
        yield return null;
        if (shouldAbortAttack) yield break;

        AnimatorStateInfo largeState = Anim.GetCurrentAnimatorStateInfo(0);
        float ll = largeState.IsName("Hit_Large_F") ? largeState.length : 0.8f;
        yield return new WaitForSeconds(ll / Anim.speed);
        if (shouldAbortAttack) yield break;

        Anim.speed = 1f;
        Anim.applyRootMotion = false;
        lockWeaponInHand = false;   // ★ 解锁，回 Idle 后正常收刀
        int al = Anim.GetLayerIndex("Attack Layer");
        if (al != -1) { Anim.SetLayerWeight(al, 0f); if (Anim.HasState(al, Animator.StringToHash("Empty_Attack"))) Anim.Play("Empty_Attack", al, 0f); }
        Anim.Update(0f);

        Controller.ChangeState(EnemyStates.Idle);
        BossDecisionEngine de2 = GetComponent<BossDecisionEngine>();
        if (de2 != null) de2.LockDecision(1.0f);   // 弹刀后 1s 内不出招

        isParryAnimating = false;
        shouldAbortAttack = false;
    }

    /// <summary>
    /// 完美格挡弹刀演出：播 Block_L/Block_R → 直接回 Idle（与普通弹刀 BlockHitRoutine 的区别：不接 Hit_Large_F 大硬直）
    /// </summary>
    private IEnumerator PerfectBlockRoutine(int blockDirection)
    {
        if (Anim == null) { isParryAnimating = false; yield break; }
        AttachWeaponToHand();
        lockWeaponInHand = true;   // ★ Block 动画播放期间锁定武器在手，防止被收刀回鞘

        Anim.speed = 1f;
        Anim.ResetTrigger("BlockHit");
        Anim.SetInteger("BlockDirection", blockDirection);
        Anim.SetTrigger("BlockHit");   // Animator AnyState→Block_L / Block_R
        yield return null;
        if (shouldAbortAttack) { FinishPerfectBlock(); yield break; }

        AnimatorStateInfo blockState = Anim.GetCurrentAnimatorStateInfo(0);
        float bl = (blockState.IsName("Block_L") || blockState.IsName("Block_R")) ? blockState.length : 0.5f;
        yield return new WaitForSeconds(bl);
        if (shouldAbortAttack) { FinishPerfectBlock(); yield break; }

        FinishPerfectBlock();
    }

    // 完美格挡结束：清攻击层 → 回 Idle → 短暂决策锁，防止立即出招造成"攻击继续"观感
    private void FinishPerfectBlock()
    {
        Anim.speed = 1f;
        Anim.applyRootMotion = false;
        lockWeaponInHand = false;   // ★ 解锁，回 Idle 后正常收刀
        int al = Anim.GetLayerIndex("Attack Layer");
        if (al != -1) { Anim.SetLayerWeight(al, 0f); if (Anim.HasState(al, Animator.StringToHash("Empty_Attack"))) Anim.Play("Empty_Attack", al, 0f); }
        Anim.Update(0f);

        Controller.ChangeState(EnemyStates.Idle);
        BossDecisionEngine de2 = GetComponent<BossDecisionEngine>();
        if (de2 != null) de2.LockDecision(2.0f);   // ★ 完美格挡是玩家奖励：2s 内决策引擎不出招，防止"弹完立刻又打"

        isParryAnimating = false;
        shouldAbortAttack = false;
    }

    /// <summary>
    /// 立即终止受击/格挡动画协程并清空 Hit Layer（转场开始时调用）。
    /// 防止：受击动画叠加在转场动画上、HitReactionRoutine 播完触发 FireRecoveredFromHit 强切 Idle。
    /// </summary>
    public void CancelHitReaction()
    {
        if (hitReactionCoroutine != null)
        {
            StopCoroutine(hitReactionCoroutine);
            hitReactionCoroutine = null;
        }
        isParryAnimating = false;
        lastHitStopDuration = 0f;
        if (Anim != null)
        {
            Anim.speed = 1f;
            int hitLayer = Anim.GetLayerIndex("Hit Layer");
            if (hitLayer != -1)
            {
                Anim.SetLayerWeight(hitLayer, 0f);
                if (Anim.HasState(hitLayer, Animator.StringToHash("Idle")))
                    Anim.Play("Idle", hitLayer, 0f);
                Anim.Update(0f);
            }
        }
    }

    public void MarkAttackHit(bool hitPlayer)
    {
        lastDerivedMoveHitPlayer = hitPlayer;
    }

    #endregion

    // ══════════════════════════════════════
    //  攻击管理
    // ══════════════════════════════════════
    #region Attack Management

    public void RegisterAttackRoutine(Coroutine routine) => currentAttackRoutine = routine;

    /// <summary>攻击协程是否仍在运行（被外部 StopCoroutine 后会变为 false）</summary>
    public bool IsAttackRoutineActive => currentAttackRoutine != null;

    public void StopCurrentAttack()
    {
        if (currentAttackRoutine != null) { StopCoroutine(currentAttackRoutine); currentAttackRoutine = null; }
    }

    public void ForceStopAllAttacks()
    {
        shouldAbortAttack = true;
        interruptedAttackCount++;

        if (currentAttackRoutine != null) { StopCoroutine(currentAttackRoutine); currentAttackRoutine = null; }
        EnableWeaponHitBox(false, false);
        Anim.speed = 1f;

        // 统一清理攻击层（包含权重、Bool 及 Empty 状态播放）
        DisableAttackLayer();

        isInComboChain = false;
        comboChainCount = 0;
        lockWeaponInHand = false;
        AttachWeaponToHand();

        events?.FireAttackInterrupted();
    }

    public void OnAttackFinished()
    {
        consecutiveHits = 0;
        interruptedAttackCount = 0;
        events?.FireAttackFinished();
    }

    #endregion

    // ══════════════════════════════════════
    //  连招
    // ══════════════════════════════════════
    #region Combo

    public void EndComboChain()
    {
        isInComboChain = false;
        comboChainCount = 0;

        if (phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseThreeMaster)
        {
            if (derivedMoveCount >= 3 && !lastDerivedMoveHitPlayer)
            {
                if (posture != null)
                {
                    float r = Mathf.Max(posture.MaxPosture * 0.5f, posture.CurrentPosture * 0.5f);
                    posture.CurrentPosture -= r;
                }
            }
            derivedMoveCount = 0;
            StartCoroutine(ChainEndStagger());
        }
        else
        {
            derivedMoveCount = 0;
            Controller.ChangeState(EnemyStates.Idle);
        }
    }

    private IEnumerator ChainEndStagger()
    {
        Anim.Play("Hit_F", 0, 0f);
        yield return new WaitForSeconds(1f);
        if (shouldAbortAttack) yield break;
        Controller.ChangeState(EnemyStates.Dodge);
    }

    public bool ShouldContinueChain()
    {
        if (phaseMgr == null) return false;
        int maxChain = phaseMgr.CurrentPhase switch
        {
            BossPhaseManager.BossPhase.PhaseOne_Test => 0,
            BossPhaseManager.BossPhase.PhaseTwoBurst => 3,
            BossPhaseManager.BossPhase.PhaseThreeMaster => 6,
            _ => 0
        };
        if (comboChainCount >= maxChain) return false;
        return true;
    }

    #endregion

    // ══════════════════════════════════════
    //  辅助
    // ══════════════════════════════════════
    #region Helpers

    private bool IsAttackState(State<EnemyController> state)
    {
        return state is NormalSlashState || state is QuickSlashState || state is ComboState
            || state is ChargeSlashState || state is SlashState || state is IaiSlashState
            || state is ThrustSlashState;
    }

    private string GetHitAnimationName(Vector3 worldDir, bool isHeavy)
    {
        int idx = GetDirectionIndex(worldDir);
        return isHeavy
            ? idx switch { 0 => "Hit_Large_F", 1 => "Hit_Large_B", 2 => "Hit_Large_L", 3 => "Hit_Large_R", _ => "Hit_Large_F" }
            : idx switch { 0 => "Hit_F", 1 => "Hit_B", 2 => "Hit_L", 3 => "Hit_R", _ => "Hit_F" };
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

    // ══════════════════════════════════════
    //  Animation Layer Management
    // ══════════════════════════════════════

    public void DisableAttackLayer()
    {
        if (Anim == null) return;
        int attackLayer = Anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1)
        {
            Anim.SetLayerWeight(attackLayer, 0f);
            // ★ 播放空状态，确保攻击层不会残留任何动画。
            //   注意：Attack Layer 的真实空状态名是 "Empty_Attack"（不是 "Empty"）！
            //   之前用 "Empty" 导致 HasState 失败 → 攻击层卡在攻击状态 → 权重恢复时"攻击动画继续"。
            if (Anim.HasState(attackLayer, Animator.StringToHash("Empty_Attack")))
            {
                Anim.Play("Empty_Attack", attackLayer, 0f);
                Anim.Update(0f);
            }
        }

        // 清除所有攻击相关的动画 Bool
        Anim.SetBool("isSlashing", false);
        Anim.SetBool("isCombo", false);
        Anim.SetBool("isQuick", false);
        Anim.SetBool("isChargeSlash", false);
        Anim.SetBool("isKanpo", false);
        Anim.SetBool("isIai", false);
        Anim.SetBool("isThrust", false);
        Anim.Update(0f);
    }

    public void EnableAttackLayer()
    {
        if (Anim == null) return;
        int attackLayer = Anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1) Anim.SetLayerWeight(attackLayer, 1f);
    }

    #endregion
}