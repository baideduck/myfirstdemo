using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeeleFighter : MonoBehaviour
{
    [Header("所有招式")]
    [SerializeField] List<AttackData> allMoves;

    [Header("武器模型")]
    [SerializeField] public GameObject weaponRight;
    [SerializeField] public GameObject weaponLeft;
    [SerializeField] public GameObject weaponBack;

    [Header("拔刀/收刀动画")]
    [SerializeField] string equipAnimName = "GreatSword_Equip_Inplace";
    [SerializeField] string sheatheAnimName = "GreatSword_Unarm_Inplace";

    [Header("格挡")]
    [SerializeField] bool enableBlocking = true;
    [SerializeField] float blockStaminaCostPerSecond = 0f;
    [SerializeField] float blockSharpnessCostPerHit = 0f;
    [SerializeField] float blockEndTransitionDelay = 0.3f;
    [SerializeField] float blockCooldown = 0.35f;

    [Header("磨刀")]
    [SerializeField] float sharpenDuration = 2.5f;
    [SerializeField] string sharpenAnimName = "Sharpen";

    [Header("连击缓冲")]
    [SerializeField] private float minComboWindow = 0.15f;   // 连击开始输入缓冲的最小窗口，太小会导致无法连招
    private bool isCommittingToAttack = false;   // 攻击提交中，禁止任何攻击

    [Header("处决")]
    [SerializeField] private string executionAnimName = "Execution";     // 玩家处决动画
    [SerializeField] private float executionAnimLength = 2f;             // 处决动画时间
    [SerializeField] private string bossBeExecutionAnimName = "BeExecution"; // Boss 被处决动画
    private bool isExecuting = false;

    [Header("蓄力特效")]
    [SerializeField] private GameObject chargeLevel2Effect;   // 蓄力Lv2特效（>1.4s）

    public bool IsBlocking { get; private set; } = false;
    public System.Action OnBlockFailed;
    public System.Action OnBlockHit;
    public float BlockStartTime { get; private set; } = -1f;
    public int CurrentChargeLevel { get; private set; } = 0;

    private GameObject activeWeapon;
    private Collider activeWeaponCollider;
    private Animator animator;
    private CharacterController characterController;
    private int attackLayerIndex;

    private Coroutine hitReactionCoroutine;
    private Coroutine bounceCoroutine;

    public enum CombatState { Sheathed, Drawn }
    public CombatState CurrentCombatState { get; private set; } = CombatState.Sheathed;
    public bool InAction { get; set; } = false;
    public bool IsInBlockRecovery { get; private set; } = false;
    public bool IsCharging { get; private set; } = false;

    private AttackData currentMove;
    private AttackData bufferedMove;
    private float bufferTimer = 0f;
    private float bufferExpireTime = 0.6f;
    private float comboIdleTimer = 0f;
    private float comboResetTime = 3f;
    private Vector2 currentMoveInput;
    private Vector2 lastNonZeroInput;
    private float lastChargeEndTime = -10f;   // 蓄力结束时间，用于冷却
    public bool CanBeInterrupted { get; private set; } = false;
    private Coroutine blockCoroutine;
    private bool isBlockCoroutineActive = false;
    private float lastBlockEndTime = -10f;
    private bool isSharpening = false;
    private Coroutine currentMoveCoroutine = null;
    private Coroutine exitSmoothCoroutine = null;     // SmoothExitAttackLayer

    public bool IsStaggering { get; private set; } = false;
    public bool IsCurrentAttackHeavy { get; private set; } = false;
    public bool IsHyperArmor { get; set; } = false;
    private PlayerController playerController;
    private string lastMoveID = "";

    // 单次攻击伤害锁：鼠标按下时置true，造成伤害后即刻置false
    [HideInInspector] public bool canDamageThisAttack = false;
    private void Awake()
    {
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
        attackLayerIndex = animator.GetLayerIndex("Attack Layer");
        if (attackLayerIndex == -1) attackLayerIndex = 0;

    }

    private void Start()
    {
        if (weaponRight) weaponRight.SetActive(false);
        if (weaponLeft) weaponLeft.SetActive(false);
        if (weaponBack) weaponBack.SetActive(true);

        StopAllCoroutines();
        InAction = false;
        currentMove = null;
        bufferedMove = null;
        CanBeInterrupted = false;
        IsBlocking = false;
        CurrentCombatState = CombatState.Sheathed;
        animator.SetLayerWeight(attackLayerIndex, 0f);
        if (animator.HasState(attackLayerIndex, Animator.StringToHash("Empty")))
            animator.Play("Empty", attackLayerIndex);
    }

    void Update()
    {
        if (bufferedMove != null)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0f) bufferedMove = null;
        }

        if (!InAction)
        {
            comboIdleTimer += Time.deltaTime;
            if (comboIdleTimer >= comboResetTime && currentMove != null)
            {
                currentMove = null;
                bufferedMove = null;
            }
        }
        else comboIdleTimer = 0f;
    }

    public void SetMoveInput(Vector2 input)
    {
        currentMoveInput = input;
        if (input.magnitude > 0.1f) lastNonZeroInput = input;
    }

    public void TryAttack(AttackData.AttackInputType inputType)
    {
        if (IsBlocking) return;
        if (isCommittingToAttack) return;   // 提交中，禁止任何攻击

        if (inputType == AttackData.AttackInputType.Heavy && Time.time < lastChargeEndTime + 0.2f)
            return;

        if (InAction && currentMove != null && currentMove.IsChargeable)
            return;

        if (CurrentCombatState == CombatState.Sheathed)
        {
            if (InAction) return;
            AttackData drawAttack = FindDrawAttack(inputType);
            if (drawAttack != null)
            {
                canDamageThisAttack = true;
                StartCoroutine(EquipAndAttack(drawAttack));
            }
            else StartCoroutine(EquipWeapon());
            return;
        }

        AttackData nextMove = FindNextMove(inputType);
        if (nextMove == null) return;

        if (!InAction)
        {
            // 提交新攻击
            isCommittingToAttack = true;
            canDamageThisAttack = true;  // 鼠标按下，本次攻击可以造成一次伤害
            if (currentMoveCoroutine != null) StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = StartCoroutine(PerformMove(nextMove));
        }
        else
        {
            BufferMove(nextMove);
        }
    }
    public void SheatheWeapon()
    {
        if (CurrentCombatState != CombatState.Drawn || InAction || IsBlocking) return;
        StartCoroutine(Sheathe());
    }
    public void TriggerBlockFail()
    {
        if (!IsBlocking) return;

        // ★ 重置攻击伤害锁，防止切换武器时误伤 Boss
        canDamageThisAttack = false;
        isCommittingToAttack = false;

        // 终止格挡协程和状态
        if (blockCoroutine != null)
        {
            StopCoroutine(blockCoroutine);
            blockCoroutine = null;
        }
        isBlockCoroutineActive = false;
        IsBlocking = false;
        IsHyperArmor = false;
        BlockStartTime = -1f;
        IsInBlockRecovery = true;

        // 请确保动画控制器包含 "BlockFail" 或 "Block_Fail" 触发器

        // 关闭 Root Motion，防止 Block_Fail 动画残留驱动角色滑步
        animator.applyRootMotion = false;

        // 禁用当前武器碰撞箱（BlockRoutine 切换到左手武器的那把）
        if (activeWeaponCollider)
            activeWeaponCollider.enabled = false;

        animator.SetBool("isBlocking", false);
        animator.ResetTrigger("BlockStart");
        animator.ResetTrigger("BlockEnd");

        // 请确保动画控制器包含 "BlockFail" 或 "Block_Fail" 触发器
        animator.SetTrigger("BlockFail");

        // 切换回右手武器，确保格挡失败时用右手武器显示
        if (CurrentCombatState == CombatState.Drawn)
            SwitchToRightWeapon();

        // 禁用右手武器碰撞箱（SwitchToRightWeapon 刚激活的）
        if (activeWeaponCollider)
            activeWeaponCollider.enabled = false;

        // InAction 不由 TriggerBlockFail 管理，由调用方（PlayerDefense.OnBlockFailed）决定
        lastBlockEndTime = Time.time;

        // 延迟解锁硬直
        StartCoroutine(DelayedRecoveryUnlock(0.5f)); // 普通格挡失败摇晃
    }
    public void TryStartBlock()
    {
        if (Time.time < lastBlockEndTime + blockCooldown) return;
        if (!enableBlocking || InAction || IsBlocking || isBlockCoroutineActive) return;
        if (CurrentCombatState != CombatState.Drawn) return;

        blockCoroutine = StartCoroutine(BlockRoutine());
    }

    public void StopBlock()
    {
        if (!isBlockCoroutineActive) return;
        if (blockCoroutine != null) StopCoroutine(blockCoroutine);
        isBlockCoroutineActive = false;

        if (IsBlocking)
            StartCoroutine(EndBlockRoutine());
        else
        {
            IsBlocking = false;
            BlockStartTime = -1f;
            animator.SetBool("isBlocking", false);
            animator.ResetTrigger("BlockStart");
            animator.ResetTrigger("BlockEnd");
            SwitchToRightWeapon();
            InAction = false;
            lastBlockEndTime = Time.time;
        }
    }

    public void ForceStopBlock()
    {
        isCommittingToAttack = false;
        if (!isBlockCoroutineActive) return;
        if (blockCoroutine != null) StopCoroutine(blockCoroutine);
        isBlockCoroutineActive = false;
        IsBlocking = false;
        BlockStartTime = -1f;
        IsInBlockRecovery = true;

        animator.SetBool("isBlocking", false);
        animator.ResetTrigger("BlockStart");
        animator.ResetTrigger("BlockEnd");
        InAction = false;
        lastBlockEndTime = Time.time;
        StartCoroutine(DelayedRecoveryUnlock(0.15f));
        SwitchToRightWeapon();
    }

    private IEnumerator DelayedRecoveryUnlock(float delay)
    {
        yield return new WaitForSeconds(delay);
        IsInBlockRecovery = false;
    }

    public void OnBlockedAttack()
    {
        if (!IsBlocking) return;
        animator.SetTrigger("BlockHit");
        OnBlockHit?.Invoke();
    }


    public bool TryInterruptActionOnDodge()
    {
        if (IsBlocking) { ForceStopBlock(); return true; }
        if (!InAction) return true;

        // 蓄力释放阶段不能翻滚取消
        if (currentMove != null && currentMove.IsChargeable)
            return false;

        // ★ 翻滚打断时机：当前招的伤害窗口（ImpactEndTime）结束后即可打断
        //   （替换"动画后 20%"——窗口结束即判定结束，收招期可翻滚取消）
        if (animator != null && attackLayerIndex >= 0 && currentMove != null)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
            // Clamp01 而非 %1f：非循环 clip 播完时 normalizedTime=1.0，1.0%1f=0 → progress 跳变回 0
            float progress = state.length > 0.001f ? Mathf.Clamp01(state.normalizedTime) : 0f;
            if (progress < currentMove.ImpactEndTime)
                return false;   // 伤害窗口内禁止翻滚打断
        }

        // 立即取消
        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }

        InAction = false;
        CanBeInterrupted = false;
        animator.applyRootMotion = false;

        if (activeWeaponCollider)
            activeWeaponCollider.enabled = false;

        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);

        bufferedMove = null;
        currentMove = null;
        return true;
    }
    public void TrySharpen()
    {
        if (InAction || IsBlocking || isSharpening || isBlockCoroutineActive) return;

        if (CurrentCombatState == CombatState.Sheathed)
        {
            // 收刀状态：拔刀 → 磨刀 → 收刀
            StartCoroutine(SharpenFromSheathedRoutine());
        }
        else
        {
            // 拔刀状态：磨刀 → 收刀
            StartCoroutine(SharpenFromDrawnRoutine());
        }
    }
    private IEnumerator SharpenFromSheathedRoutine()
    {
        isSharpening = true;
        InAction = true;

        // --- 第一阶段：拔刀 ---
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.CrossFade(equipAnimName, 0.1f, attackLayerIndex);
        yield return null;
        var equipState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float equipLength = equipState.length;
        float equipAttachTime = equipLength * (26f / 90f);
        yield return new WaitForSeconds(equipAttachTime);
        SwitchToRightWeapon();
        yield return new WaitForSeconds(equipLength - equipAttachTime);

        CurrentCombatState = CombatState.Drawn;

        // --- 第二阶段：磨刀 ---
        animator.CrossFade(sharpenAnimName, 0.1f, attackLayerIndex);
        yield return new WaitForSeconds(sharpenDuration);
        PlayerSharpness sharpness = GetComponent<PlayerSharpness>();
        sharpness?.Sharpen();

        // --- 第三阶段：收刀 ---
        animator.CrossFade(sheatheAnimName, 0.1f, attackLayerIndex);
        yield return null;
        var sheatheState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float sheatheLength = sheatheState.length;
        float sheatheAttachTime = sheatheLength * (60f / 90f);
        yield return new WaitForSeconds(sheatheAttachTime);

        // 切换到背武器
        SwitchToBackWeapon();
        // 强制把武器模型挂到背上，防止错位或穿模
        if (weaponBack != null)
        {
            weaponBack.transform.localPosition = Vector3.zero;
            weaponBack.transform.localRotation = Quaternion.identity;
        }

        yield return new WaitForSeconds(sheatheLength - sheatheAttachTime);

        CurrentCombatState = CombatState.Sheathed;

        InAction = false;
        isSharpening = false;
        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);
    }

    private IEnumerator SharpenFromDrawnRoutine()
    {
        isSharpening = true;
        InAction = true;

        // --- 第一阶段：磨刀 ---
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.CrossFade(sharpenAnimName, 0.1f, attackLayerIndex);
        yield return new WaitForSeconds(sharpenDuration);
        PlayerSharpness sharpness = GetComponent<PlayerSharpness>();
        sharpness?.Sharpen();

        // --- 第二阶段：收刀 ---
        animator.CrossFade(sheatheAnimName, 0.1f, attackLayerIndex);
        yield return null;
        var sheatheState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float sheatheLength = sheatheState.length;
        float sheatheAttachTime = sheatheLength * (60f / 90f);
        yield return new WaitForSeconds(sheatheAttachTime);

        // 切换到背武器
        SwitchToBackWeapon();
        // 强制把武器模型挂到背上
        if (weaponBack != null)
        {
            weaponBack.transform.localPosition = Vector3.zero;
            weaponBack.transform.localRotation = Quaternion.identity;
        }

        yield return new WaitForSeconds(sheatheLength - sheatheAttachTime);

        CurrentCombatState = CombatState.Sheathed;

        InAction = false;
        isSharpening = false;
        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);
    }

    private void SwitchToRightWeapon()
    {
        if (weaponRight) weaponRight.SetActive(true);
        if (weaponLeft) weaponLeft.SetActive(false);
        if (weaponBack) weaponBack.SetActive(false);
        activeWeapon = weaponRight;
        activeWeaponCollider = activeWeapon ? activeWeapon.GetComponentInChildren<Collider>() : null;
    }

    private void SwitchToLeftWeapon()
    {
        if (weaponRight) weaponRight.SetActive(false);
        if (weaponLeft) weaponLeft.SetActive(true);
        if (weaponBack) weaponBack.SetActive(false);
        activeWeapon = weaponLeft;
        activeWeaponCollider = activeWeapon ? activeWeapon.GetComponentInChildren<Collider>() : null;
    }

    private void SwitchToBackWeapon()
    {
        if (weaponRight) weaponRight.SetActive(false);
        if (weaponLeft) weaponLeft.SetActive(false);
        if (weaponBack) weaponBack.SetActive(true);
        activeWeapon = null;
        activeWeaponCollider = null;
    }

    private AttackData FindDrawAttack(AttackData.AttackInputType inputType)
    {
        foreach (var move in allMoves)
        {
            if (move == null) continue;
            if (!move.CanUseFromSheathed || move.RequiredInput != inputType) continue;
            if (move.RequiresForwardInput && !IsForwardInput(false)) continue;
            return move;
        }
        return null;
    }
    private AttackData GetAttackDataByAnimName(string animName)
    {
        foreach (var data in allMoves)
            if (data != null && data.AnimName == animName)
                return data;
        return null;
    }

    private AttackData GetAttackDataByMoveID(string moveID)
    {
        foreach (var data in allMoves)
            if (data != null && data.MoveID == moveID)
                return data;
        return null;
    }

    private AttackData FindNextMove(AttackData.AttackInputType inputType)
    {
        if (currentMove == null)
        {
            foreach (var move in allMoves)
            {
                if (move == null) continue;
                if (move.RequiresDrawn && CurrentCombatState != CombatState.Drawn) continue;
                if (move.RequiredInput != inputType) continue;
                if (move.RequiresForwardInput && !IsForwardInput(false)) continue;
                if (move.AllowedPreviousMoves == null || move.AllowedPreviousMoves.Length == 0)
                    return move;
            }
            return null;
        }

        string prevID = currentMove.MoveID;
        foreach (var move in allMoves)
        {
            if (move == null) continue;
            if (move.RequiresDrawn && CurrentCombatState != CombatState.Drawn) continue;
            if (move.RequiredInput != inputType) continue;
            if (move.RequiresForwardInput && !IsForwardInput(false)) continue;
            if (move.AllowedPreviousMoves != null && System.Array.Exists(move.AllowedPreviousMoves, id => id == prevID))
                return move;
        }
        return null;
    }

    private bool HasAnyNextMove(AttackData move)
    {
        if (move == null) return false;
        foreach (var m in allMoves)
        {
            if (m == move) continue;
            if (m.AllowedPreviousMoves != null && System.Array.Exists(m.AllowedPreviousMoves, id => id == move.MoveID))
                return true;
        }
        return false;
    }

    private bool IsForwardInput(bool useHistory = true)
    {
        Vector2 checkInput = currentMoveInput;
        if (useHistory && checkInput.magnitude < 0.1f)
            checkInput = lastNonZeroInput;
        if (checkInput.magnitude < 0.1f) return false;
        Vector3 inputDir = new Vector3(checkInput.x, 0, checkInput.y).normalized;
        return Vector3.Dot(transform.forward, inputDir) > 0.85f;
    }

    private void BufferMove(AttackData move)
    {
        bufferedMove = move;
        bufferTimer = bufferExpireTime;
    }

    private IEnumerator EquipWeapon()
    {
        InAction = true;
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.Play(equipAnimName, attackLayerIndex, 0f);
        yield return null;
        yield return null;

        var state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float length = state.length;
        float attachTime = length * (26f / 90f);
        yield return new WaitForSeconds(attachTime);
        SwitchToRightWeapon();
        yield return new WaitForSeconds(length - attachTime);

        CurrentCombatState = CombatState.Drawn;
        EndAction();

        if (bufferedMove != null)
        {
            AttackData move = bufferedMove;
            bufferedMove = null;
            if (move.RequiresDrawn)
            {
                canDamageThisAttack = true;
                StartCoroutine(PerformMove(move));
            }
        }
    }

    private IEnumerator EquipAndAttack(AttackData drawAttack)
    {
        InAction = true;
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.Play(equipAnimName, attackLayerIndex, 0f);
        yield return null;
        yield return null;

        var state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float length = state.length;
        float attachTime = length * (26f / 90f);
        yield return new WaitForSeconds(attachTime);
        SwitchToRightWeapon();
        yield return new WaitForSeconds(length - attachTime);

        CurrentCombatState = CombatState.Drawn;
        yield return StartCoroutine(PerformMove(drawAttack));
        EndAction();
    }

    private IEnumerator Sheathe()
    {
        InAction = true;
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.CrossFade(sheatheAnimName, 0.1f, attackLayerIndex);
        yield return null;

        var state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float length = state.length;
        float attachTime = length * (60f / 90f);
        yield return new WaitForSeconds(attachTime);
        SwitchToBackWeapon();
        yield return new WaitForSeconds(length - attachTime);

        CurrentCombatState = CombatState.Sheathed;
        currentMove = null;
        bufferedMove = null;
        EndAction();
    }

    private IEnumerator ChargeAttack(AttackData move)
    {
        bool hasPlayedEnhance = false;
        IsCurrentAttackHeavy = true;

        currentMove = move;
        lastMoveID = move.MoveID;
        InAction = true;
        CanBeInterrupted = false;
        animator.applyRootMotion = false;
        animator.SetLayerWeight(attackLayerIndex, 1f);

        if (move.HasChargeStartAnim)
        {
            animator.CrossFade(move.ChargeStartAnim, 0.1f, attackLayerIndex);
            yield return new WaitForSeconds(0.15f);
            var startState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
            yield return new WaitForSeconds(startState.length);
        }

        if (!string.IsNullOrEmpty(move.ChargeHoldAnim))
            animator.CrossFade(move.ChargeHoldAnim, 0.1f, attackLayerIndex);
        else
            animator.CrossFade(move.AnimName, 0.1f, attackLayerIndex);
        yield return new WaitForSeconds(0.15f);

        float chargeTimer = 0f;
        int chargeLevel = 1;
        float chargeThreshold = 1.4f;      // 两段蓄力分界线
        isCommittingToAttack = false;
        IsCharging = true;
        while (Input.GetKey(KeyCode.Mouse1))
        {
            if (!IsCharging) yield break;
            chargeTimer += Time.unscaledDeltaTime;

            if (chargeTimer >= chargeThreshold)
                chargeLevel = 2;
            else
                chargeLevel = 1;

            // 蓄力 Lv2 特效（>1.4s）
            if (chargeLevel >= 2 && chargeLevel2Effect != null && !chargeLevel2Effect.activeSelf)
                chargeLevel2Effect.SetActive(true);

            CurrentChargeLevel = chargeLevel;
            if (move.HasEnhanceAnim && chargeTimer >= move.EnhanceTriggerTime && !hasPlayedEnhance)
            {
                hasPlayedEnhance = true;
                animator.CrossFade(move.EnhanceAnimName, 0.1f, attackLayerIndex);
            }

            Vector3 inputDir = new Vector3(currentMoveInput.x, 0, currentMoveInput.y).normalized;
            if (inputDir.magnitude > 0.1f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(inputDir), 10f * Time.deltaTime);
            yield return null;
        }
        IsCharging = false;

        // 关闭蓄力特效
        if (chargeLevel2Effect != null) chargeLevel2Effect.SetActive(false);

        string releaseAnim = move.AnimName;
        if (move.ChargeReleaseAnims != null && move.ChargeReleaseAnims.Length > 0)
        {
            int index = Mathf.Clamp(chargeLevel - 1, 0, move.ChargeReleaseAnims.Length - 1);
            if (!string.IsNullOrEmpty(move.ChargeReleaseAnims[index]))
                releaseAnim = move.ChargeReleaseAnims[index];
        }

        animator.applyRootMotion = true;

        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.Play(releaseAnim, attackLayerIndex, 0f);
        animator.Update(0);
        yield return null;

        // 刀光拖尾：蓄力释放挥砍开始
        StartWeaponTrail();

        var releaseState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float animLength = releaseState.length;
        if (animLength < 0.02f) animLength = 1f;

        AttackData releaseData = GetAttackDataByAnimName(releaseAnim);
        float impactStart = (releaseData != null) ? releaseData.ImpactStartTime : move.ImpactStartTime;
        float impactEnd = (releaseData != null) ? releaseData.ImpactEndTime : move.ImpactEndTime;
        if (impactEnd - impactStart < 0.01f) { impactStart = 0f; impactEnd = animLength; }
        float damageForThisHit = (releaseData != null) ? releaseData.Damage : 25;

        if (activeWeapon != null)
        {
            PlayerWeaponHitbox hitbox = activeWeapon.GetComponent<PlayerWeaponHitbox>();
            hitbox?.PlaySwingSound();
        }

        if (impactStart <= 0.001f && impactEnd <= 0.001f) { impactStart = animLength * 0.2f; impactEnd = animLength * 0.8f; }

        EnemyController enemyCtrl = FindObjectOfType<EnemyController>();
        Vector3 enemyPos = enemyCtrl ? enemyCtrl.transform.position : Vector3.zero;

        float timer = 0f;
        bool impactActive = false;

        while (timer < animLength)
        {
            // 顿帧期间计时器暂停
            if (!isInHitStop)
            {
                timer += Time.deltaTime;
            }
            float normTime = Mathf.Clamp01(timer / animLength);

            // 连招检查：超过 minComboWindow 后消费缓存的下一招
            if (timer >= minComboWindow && bufferedMove != null && HasAnyNextMove(currentMove))
            {
                AttackData next = bufferedMove;
                bufferedMove = null;
                if (activeWeaponCollider) activeWeaponCollider.enabled = false;
                if (currentMoveCoroutine != null) StopCoroutine(currentMoveCoroutine);
                canDamageThisAttack = true;  // 连招缓存也要复位伤害锁
                currentMoveCoroutine = StartCoroutine(PerformMove(next));
                yield break;
            }

            if (!impactActive && normTime >= impactStart / animLength && normTime <= impactEnd / animLength)
            {
                impactActive = true;
                IsHyperArmor = true;

                CameraController camCtrl = Camera.main?.GetComponent<CameraController>();
                if (chargeLevel >= 2) camCtrl?.TriggerHeavySlashImpact(enemyPos);
                else camCtrl?.TriggerTier2ChargeShake();

                if (activeWeapon != null) activeWeaponCollider = activeWeapon.GetComponentInChildren<Collider>(true);

                if (activeWeaponCollider != null)
                {
                    activeWeaponCollider.enabled = false;
                    activeWeaponCollider.enabled = true;

                    var hitbox = activeWeaponCollider.GetComponent<PlayerWeaponHitbox>();
                    if (hitbox != null)
                    {
                        hitbox.damage = damageForThisHit;
                        hitbox.ResetHitState();
                        hitbox.ForceClearHitRecord();
                    }
                }
            }
            else if (impactActive && normTime > impactEnd / animLength)
            {
                impactActive = false;
                if (activeWeaponCollider) activeWeaponCollider.enabled = false;
            }
            yield return null;
        }

        IsHyperArmor = false;
        EndAction();
        lastChargeEndTime = Time.time;   // 记录冷却
        IsCurrentAttackHeavy = false;
        currentMove = null;

        if (bufferedMove != null)
        {
            AttackData next = bufferedMove;
            bufferedMove = null;
            if (currentMoveCoroutine != null) StopCoroutine(currentMoveCoroutine);
            canDamageThisAttack = true;
            currentMoveCoroutine = StartCoroutine(PerformMove(next));
        }
    }
    /// <summary>
    /// 取消当前攻击或蓄力释放攻击
    /// </summary>
    public void CancelCharge()
    {
        isCommittingToAttack = false;
        IsCharging = false;

        if (chargeLevel2Effect != null) chargeLevel2Effect.SetActive(false);
        // 停止所有攻击协程
        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }

        // 停止 MeeleFighter 的其他协程，确保 ChargeAttack 的 while 循环也被杀死
        StopAllCoroutines();

        // 重置状态
        IsCurrentAttackHeavy = false;
        InAction = false;
        currentMove = null;
        bufferedMove = null;

        if (activeWeaponCollider != null)
            activeWeaponCollider.enabled = false;

        // 刀光拖尾：蓄力取消
        StopWeaponTrail();

        // 恢复动画
        animator.speed = 1f;
        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);
    }

    /// <summary>
    /// 重置连招，下次攻击从第一击开始
    /// </summary>
    public void ResetAttackCombo()
    {
        currentMove = null;
        bufferedMove = null;
    }

    private IEnumerator PerformMove(AttackData move)
    {
        if (move.IsChargeable && move.RequiredInput == AttackData.AttackInputType.Heavy)
        {
            yield return StartCoroutine(ChargeAttack(move));
            yield break;
        }
        CurrentChargeLevel = 0;

        IsCurrentAttackHeavy = move.IsHeavyAttack;
        currentMove = move;
        lastMoveID = move.MoveID;
        InAction = true;
        CanBeInterrupted = false;

        // 停掉旧攻击的退出协程，防止它抢 Attack Layer 权重
        if (exitSmoothCoroutine != null)
        {
            StopCoroutine(exitSmoothCoroutine);
            exitSmoothCoroutine = null;
        }

        animator.applyRootMotion = true;
        animator.SetLayerWeight(attackLayerIndex, 1f);
        // ★ 过渡 0.1→0.05：连招隐式过渡时长 = 0.05×源长度（更短），
        //   减少"IsName 等待吃掉目标窗口起点"（2-1 窗口跳过）与收尾卡顿（④）
        animator.CrossFade(move.AnimName, 0.05f, attackLayerIndex);

        // ★ 等目标动画真正成为当前状态（替代固定 0.15s 等待 + 单点采样）
        // 修复：顿帧/过渡未完成时采到 Empty(无clip,length=0) → yield break 攻击静默作废
        float enterWait = 0f;
        while (!animator.GetCurrentAnimatorStateInfo(attackLayerIndex).IsName(move.AnimName))
        {
            enterWait += Time.unscaledDeltaTime;
            if (enterWait > 0.5f) break;   // 超时兜底：状态名不匹配时不再死等
            yield return null;
        }
        isCommittingToAttack = false;

        // 刀光拖尾：挥砍开始
        StartWeaponTrail();

        // 状态就绪后采样 clip 长度（IsName 成立后取到的必是目标状态）
        var state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float stateLength = state.length;
        if (stateLength < 0.02f) stateLength = 1f;   // 兜底：超时未进入时按 1s 走完流程
        float timer = 0f;
        bool impactActive = false;

        if (activeWeapon != null)
        {
            PlayerWeaponHitbox hitbox = activeWeapon.GetComponent<PlayerWeaponHitbox>();
            hitbox?.PlaySwingSound();
        }

        while (timer < stateLength)
        {
            // 顿帧期间计时器暂停，动画时间自然延长（卡肉感）
            if (!isInHitStop)
            {
                timer += Time.deltaTime;
            }

            // ★ 伤害窗口用动画真实进度驱动（与刀锋对齐；顿帧时 normalizedTime 冻结=碰撞体保持开启）
            AnimatorStateInfo curState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
            float animNorm;
            if (curState.IsName(move.AnimName))
                animNorm = Mathf.Min(curState.normalizedTime, 1f);   // 播完不回绕，避免窗口重开
            else
                animNorm = Mathf.Clamp01(timer / stateLength);       // 超时兜底：退回本地计时

            // 连招窗口，第一招使用 minComboWindow，后续招数乘以 1.5 倍
            float effectiveWindow = minComboWindow;
            if (currentMove != null && !string.IsNullOrEmpty(currentMove.MoveID) && currentMove.MoveID != "Attack1")
                effectiveWindow = minComboWindow * 1.5f;

            if ((move.MoveID == "Combo2" || move.MoveID == "Slash3") && animNorm >= 0.95f)
            {
                animator.speed = 0.5f;
            }

            if (timer >= effectiveWindow && bufferedMove != null && HasAnyNextMove(currentMove))
            {
                AttackData nextMove = bufferedMove;
                bufferedMove = null;
                if (activeWeaponCollider) activeWeaponCollider.enabled = false;
                if (currentMoveCoroutine != null) StopCoroutine(currentMoveCoroutine);
                canDamageThisAttack = true;
                currentMoveCoroutine = StartCoroutine(PerformMove(nextMove));
                yield break;
            }

            if (!impactActive && animNorm >= move.ImpactStartTime && animNorm <= move.ImpactEndTime)
            {
                impactActive = true;

                if (activeWeapon != null)
                    activeWeaponCollider = activeWeapon.GetComponentInChildren<Collider>(true);

                if (activeWeaponCollider)
                {
                    activeWeaponCollider.enabled = false;   // 先关再开，强制触发 OnTriggerEnter
                    activeWeaponCollider.enabled = true;
                    var weaponHitbox = activeWeaponCollider.GetComponent<PlayerWeaponHitbox>();
                    if (weaponHitbox != null)
                    {
                        weaponHitbox.damage = move.Damage;
                        weaponHitbox.ResetHitState();          // 清命中记录
                        weaponHitbox.ForceClearHitRecord();    // 清帧缓存
                    }
                }
            }
            else if (impactActive && animNorm > move.ImpactEndTime)
            {
                impactActive = false;
                if (activeWeaponCollider) activeWeaponCollider.enabled = false;
            }

            // ★ 动画真实播完（或状态切走）→ 立即结束循环。
            //   修复：timer 起点比动画晚一个过渡时长，若只等 timer 会"动画已收招但仍锁 InAction 0.15~0.25s"
            //   （攻击后摇后原地卡、闪避无法取消后摇、卡顿无法打断——④③同根）
            if (curState.IsName(move.AnimName))
            {
                if (curState.normalizedTime >= 1f) break;
            }
            else
            {
                break;   // 状态被切走（SmoothExit/外部打断）→ 不再死等
            }

            yield return null;
        }

        // 顿帧期间不强制恢复速度，让 PlayerHitStopRoutine 自己处理
        if (!isInHitStop)
            animator.speed = 1f;
        EndAction();
        IsCurrentAttackHeavy = false;

        if (bufferedMove != null && HasAnyNextMove(currentMove))
        {
            AttackData next = bufferedMove;
            bufferedMove = null;
            if (currentMoveCoroutine != null) StopCoroutine(currentMoveCoroutine);
            canDamageThisAttack = true;
            currentMoveCoroutine = StartCoroutine(PerformMove(next));
        }
        else
        {
            currentMove = null;
        }
    }

    private void EndAction()
    {
        InAction = false;
        canDamageThisAttack = false;               // ★ 攻击结束强制清伤害锁
        isCommittingToAttack = false;              // ★ 解除提交锁定

        // 刀光拖尾：挥砍结束
        StopWeaponTrail();

        if (activeWeaponCollider) activeWeaponCollider.enabled = false;
        animator.applyRootMotion = false;
        // 平滑退出 Attack Layer（在 Idle 和 Combo2/Slash3 之间平滑过渡）
        exitSmoothCoroutine = StartCoroutine(SmoothExitAttackLayer());
    }
    private IEnumerator SmoothExitAttackLayer()
    {
        // 对于 Combo2 或 Slash3 的收尾与 Idle 之间需要更长的过渡时间
        float fadeDuration = (lastMoveID == "Combo2" || lastMoveID == "Slash3") ? 0.25f : 0.1f;

        if (animator.HasState(attackLayerIndex, Animator.StringToHash("Empty")))
        {
            animator.CrossFade("Empty", fadeDuration, attackLayerIndex);
        }

        float startWeight = animator.GetLayerWeight(attackLayerIndex);
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            animator.SetLayerWeight(attackLayerIndex, Mathf.Lerp(startWeight, 0f, t));
            yield return null;
        }
        animator.SetLayerWeight(attackLayerIndex, 0f);

        if (animator.HasState(attackLayerIndex, Animator.StringToHash("Empty")))
        {
            animator.Play("Empty", attackLayerIndex);
        }

        lastMoveID = "";
        yield return null;
        isCommittingToAttack = false;
        exitSmoothCoroutine = null;
    }
    private IEnumerator BlockRoutine()
    {
        isBlockCoroutineActive = true;

        if (CurrentCombatState == CombatState.Sheathed)
        {
            InAction = true;
            animator.SetLayerWeight(attackLayerIndex, 1f);
            animator.Play(equipAnimName, attackLayerIndex, 0f);
            yield return null;
            yield return null;
            var state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
            float length = state.length;
            float attachTime = length * (26f / 90f);
            yield return new WaitForSeconds(attachTime);
            SwitchToRightWeapon();
            yield return new WaitForSeconds(length - attachTime);
            CurrentCombatState = CombatState.Drawn;
            InAction = false;
            animator.SetLayerWeight(attackLayerIndex, 0f);
            animator.Play("Empty", attackLayerIndex);
        }

        SwitchToLeftWeapon();
        // 强制确认左手显示，右手隐藏
        if (weaponRight) weaponRight.SetActive(false);
        if (weaponLeft) weaponLeft.SetActive(true);
        if (weaponBack) weaponBack.SetActive(false);

        // 禁用武器碰撞箱，防止格挡时碰撞箱误触 Boss
        if (activeWeaponCollider)
            activeWeaponCollider.enabled = false;

        IsBlocking = true;
        IsHyperArmor = true;
        BlockStartTime = Time.time;
        animator.SetTrigger("BlockStart");
        yield return new WaitForSeconds(0.15f);
        animator.SetBool("isBlocking", true);

        while (IsBlocking)
        {
            // ★ 怪猎式：格挡持续消耗体力（PlayerStamina.TryBlockTick）；体力耗尽 → 解除格挡并收盾（力竭动画由 Consume 内部触发）
            PlayerStamina stamina = GetComponent<PlayerStamina>();
            if (stamina != null && !stamina.TryBlockTick())
            {
                IsBlocking = false;
                BlockStartTime = -1f;
                animator.SetBool("isBlocking", false);
                animator.ResetTrigger("BlockStart");
                isBlockCoroutineActive = false;
                StartCoroutine(EndBlockRoutine());
                yield break;
            }
            yield return null;
        }
        isBlockCoroutineActive = false;
    }

    private IEnumerator EndBlockRoutine()
    {
        IsBlocking = false;
        IsHyperArmor = false;
        BlockStartTime = -1f;
        IsInBlockRecovery = true;

        animator.SetBool("isBlocking", false);
        animator.SetTrigger("BlockEnd");
        animator.applyRootMotion = false;
        yield return new WaitForSeconds(blockEndTransitionDelay);
        if (CurrentCombatState == CombatState.Drawn)
            SwitchToRightWeapon();
        lastBlockEndTime = Time.time;
        IsInBlockRecovery = false;
        animator.applyRootMotion = false;           // 再次锁定移动，防止惯性滑动
        if (playerController != null)
            playerController.LockMovement(0.4f);
    }
    // ★ 根运动应用已统一迁移到 PlayerController.OnAnimatorMove（applyRootMotion=true 时经 CharacterController 应用），
    //   这里不再定义，避免与 PlayerController 重复应用导致双倍位移。

    // ===================== 受击与弹刀（直接播放版） =====================
    public void PlayHitReaction(Vector3 hitDirectionWorld)
    {
        playerController?.ForceEndInvincibility();

        // 停止所有协程（含嵌套的 ChargeAttack/PerformMove 子协程与顿帧协程），
        // 防止嵌套攻击协程在受击后继续运行导致玩家卡死
        StopAllCoroutines();

        // 恢复动画速度与顿帧标志（防止顿帧协程被误杀后残留 animator.speed=0 / isInHitStop=true）
        animator.speed = 1f;
        isInHitStop = false;
        playerHitStopCoroutine = null;

        // 清空协程引用
        currentMoveCoroutine = null;
        hitReactionCoroutine = null;
        bounceCoroutine = null;

        if (activeWeaponCollider) activeWeaponCollider.enabled = false;

        // ★ 受击/弹刀立即清零伤害锁：即使武器碰撞体未及时禁用，hit 动画期间也不会再造成伤害
        canDamageThisAttack = false;

        IsStaggering = true;
        InAction = true;
        CanBeInterrupted = false;
        playerController?.ResetStaggerRecoveryTimer();
        playerController?.ForceDisableDodge();

        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);

        // 清除移动输入，防止 Any State 过渡到移动动画
        animator.SetFloat("moveAmount", 0f);
        animator.ResetTrigger("TakeHit");
        animator.SetInteger("HitDirection", -1);

        string animName = GetHitAnimationName(hitDirectionWorld);
        animator.Play(animName, 0, 0f);

        hitReactionCoroutine = StartCoroutine(HitReactionRoutine(animName));
    }

    public void PlayBounceReaction()
    {
        playerController?.ForceEndInvincibility();

        // 停止所有协程（含嵌套的 ChargeAttack/PerformMove 子协程与顿帧协程），
        // 防止嵌套攻击协程在弹刀后继续运行导致玩家卡死
        StopAllCoroutines();

        // 恢复动画速度与顿帧标志（防止顿帧协程被误杀后残留 animator.speed=0 / isInHitStop=true）
        animator.speed = 1f;
        isInHitStop = false;
        playerHitStopCoroutine = null;

        // 清空协程引用
        currentMoveCoroutine = null;
        hitReactionCoroutine = null;
        bounceCoroutine = null;

        if (activeWeaponCollider) activeWeaponCollider.enabled = false;

        // ★ 受击/弹刀立即清零伤害锁：即使武器碰撞体未及时禁用，hit 动画期间也不会再造成伤害
        canDamageThisAttack = false;

        IsStaggering = true;
        InAction = true;
        CanBeInterrupted = false;
        playerController?.ResetStaggerRecoveryTimer();
        playerController?.ForceDisableDodge();

        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);

        animator.SetFloat("moveAmount", 0f);
        animator.ResetTrigger("TakeHit");
        animator.ResetTrigger("BounceHit");

        animator.Play("Bounce", 0, 0f);
        bounceCoroutine = StartCoroutine(BounceRoutine());
    }

    private IEnumerator HitReactionRoutine(string animName)
    {
        yield return null;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        float length = state.IsName(animName) ? state.length : 0.5f;
        yield return new WaitForSeconds(length);
        OnHitOrBounceExit();
        hitReactionCoroutine = null;
    }

    private IEnumerator BounceRoutine()
    {
        yield return null;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        float length = state.IsName("Bounce") ? state.length : 0.5f;
        yield return new WaitForSeconds(length);
        OnHitOrBounceExit();
        bounceCoroutine = null;
    }

    public void OnHitOrBounceExit()
    {
        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null)
        {
            // 直接清零硬直恢复计时器，不再有 0.15 秒冻结
            pc.ClearStaggerRecoveryTimer();
            pc.LockMovement(0f);
            // 内部已包含 StopAllCoroutines
            pc.ForceUnlockDodge();
        }

        // 保险，双重解锁闪避
        StartCoroutine(ReEnableDodgeImmediately());

        IsStaggering = false;
        InAction = false;
        CanBeInterrupted = true;
        isCommittingToAttack = false;         // 防止攻击协程被暴力打断后残留
        IsCharging = false;                   // 防止蓄力被打断后残留
        CurrentChargeLevel = 0;               // 重置蓄力等级
        currentMove = null;
        bufferedMove = null;
        IsCurrentAttackHeavy = false;

        if (CurrentCombatState == CombatState.Drawn)
        {
            SwitchToRightWeapon();
            // 受击时武器碰撞体被关了，这里恢复
            if (activeWeaponCollider != null)
                activeWeaponCollider.enabled = true;
        }
        else
            SwitchToBackWeapon();

        StartCoroutine(EnableRootMotionNextFrame());
    }

    private IEnumerator ReEnableDodgeImmediately()
    {
        yield return null;
        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.ForceUnlockDodge();
            pc.ClearStaggerRecoveryTimer();
        }
    }

    /// <summary>
    /// 播放力竭动画
    /// </summary>
    public void PlayExhausted()
    {
        if (IsStaggering || InAction) return;

        IsStaggering = true;
        InAction = true;

        // ★ 玩家 controller 中力竭状态名是 OutOfBreath（不是 Exhausted）——原名播放失败导致力竭完全没有动画
        animator.Play("OutOfBreath", 0, 0f);
        StartCoroutine(RecoverFromExhausted());
    }

    private IEnumerator RecoverFromExhausted()
    {
        yield return new WaitForSeconds(2f);
        IsStaggering = false;
        InAction = false;
        // ★ OutOfBreath 状态无退出过渡（m_Transitions 为空），恢复时必须手动切回移动混合树，防止卡死在喘息动画
        if (animator.HasState(0, Animator.StringToHash("Blend Tree")))
            animator.Play("Blend Tree", 0, 0f);
    }
    private string GetHitAnimationName(Vector3 hitDirectionWorld)
    {
        int dir = GetHitDirectionIndex(hitDirectionWorld);
        return dir switch
        {
            0 => "Hit_F",
            1 => "Hit_B",
            2 => "Hit_L",
            3 => "Hit_R",
            _ => "Hit_F"
        };
    }

    private int GetHitDirectionIndex(Vector3 hitDirectionWorld)
    {
        Vector3 localDir = transform.InverseTransformDirection(-hitDirectionWorld.normalized);
        float angle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
        if (angle > -45 && angle <= 45) return 0;
        else if (angle > 45 && angle <= 135) return 2;
        else if (angle < -45 && angle >= -135) return 3;
        else return 1;
    }
    private IEnumerator EnableRootMotionNextFrame()
    {
        yield return null;
        animator.applyRootMotion = true;
    }

    // ═══════════════════════════════════════
    //  顿帧系统：玩家动画同步冻结（MHW大剑卡肉感核心）
    // ═══════════════════════════════════════
    private Coroutine playerHitStopCoroutine;
    private bool isInHitStop = false;

    /// <summary>
    /// 命中时冻结玩家动画，与敌人同步卡肉
    /// </summary>
    public void TriggerPlayerHitStop(float duration)
    {
        if (duration <= 0f) return;
        if (playerHitStopCoroutine != null)
        {
            StopCoroutine(playerHitStopCoroutine);
            isInHitStop = false;      // 重置标记，防止残留
        }
        playerHitStopCoroutine = StartCoroutine(PlayerHitStopRoutine(duration));
    }

    private IEnumerator PlayerHitStopRoutine(float duration)
    {
        isInHitStop = true;
        animator.speed = 0f;

        yield return new WaitForSecondsRealtime(duration);

        animator.speed = 1f;
        isInHitStop = false;
        playerHitStopCoroutine = null;
    }

    // ═══════════════════════════════════════
    //  Blade Afterimage (MHW-style motion trail)
    // ═══════════════════════════════════════
    [Header("刀光残影")]
    [SerializeField] private Material trailMaterial;       // 拖进去一个透明材质（如 Glow.mat）
    [SerializeField] private float trailWidth = 0.3f;       // 刀光宽度
    [SerializeField] private float trailTime = 0.08f;       // 残影残留时间

    private TrailRenderer weaponTrail;

    private void StartWeaponTrail()
    {
        if (activeWeapon == null) return;

        if (weaponTrail == null)
        {
            weaponTrail = activeWeapon.AddComponent<TrailRenderer>();
            weaponTrail.autodestruct = false;
            weaponTrail.minVertexDistance = 0.02f;
            weaponTrail.emitting = false;
        }

        weaponTrail.time = trailTime;
        weaponTrail.startWidth = trailWidth;
        weaponTrail.endWidth = 0f;
        if (trailMaterial != null)
            weaponTrail.material = trailMaterial;

        weaponTrail.Clear();
        weaponTrail.emitting = true;
        weaponTrail.enabled = true;
    }

    private void StopWeaponTrail()
    {
        if (weaponTrail != null)
            weaponTrail.emitting = false;
    }

    public void ForceHideWeapon()
    {
        if (weaponRight) weaponRight.SetActive(false);
        if (weaponLeft) weaponLeft.SetActive(false);
        if (weaponBack) weaponBack.SetActive(false);
        activeWeaponCollider = null;
        activeWeapon = null;
    }
    public void DisableAttackLayer()
    {
        animator.SetLayerWeight(attackLayerIndex, 0f);
    }

    public void EnableAttackLayer()
    {
        animator.SetLayerWeight(attackLayerIndex, 1f);
    }

    /// <summary>
    /// 开始处决（由 PlayerController 调用）
    /// </summary>
    public void StartExecution(EnemyController target)
    {
        if (isExecuting) return;
        StartCoroutine(ExecutionRoutine(target));
    }

    private IEnumerator ExecutionRoutine(EnemyController target)
    {
        isExecuting = true;

        // 锁定玩家动作
        InAction = true;
        CanBeInterrupted = false;
        IsHyperArmor = true;

        Vector3 dir = (target.transform.position - transform.position).normalized;
        dir.y = 0;
        if (dir.magnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);

        if (activeWeaponCollider != null)
            activeWeaponCollider.enabled = false;

        // 停止 Boss 的移动协程，防止每帧把 Boss 拉回原位
        BossPhaseManager phaseMgr = target.GetComponent<BossPhaseManager>();
        if (phaseMgr != null)
            phaseMgr.StopExecutionSequence();

        // 停止 Boss 一般行为
        target.StopCurrentAttack();
        target.EnableWeaponHitBox(false, false);
        target.DisableAttackLayer();

        // 解冻 Boss
        target.isExecutionFrozen = false;

        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;

        // 强制开启根运动，让动画位移生效
        target.anim.applyRootMotion = true;
        target.anim.speed = 1f;
        target.anim.Rebind();
        target.anim.Update(0);

        // 重置动画器所有层，只保留 Base Layer
        for (int i = 1; i < target.anim.layerCount; i++)
            target.anim.SetLayerWeight(i, 0f);

        // 播放被处决动画
        target.anim.Play(bossBeExecutionAnimName, 0, 0f);
        target.anim.Update(0);

        // 验证
        AnimatorStateInfo bossState = target.anim.GetCurrentAnimatorStateInfo(0);

        if (bossState.normalizedTime < 0.01f)
        {
            target.anim.Play(bossBeExecutionAnimName, 0, 0.01f);
            target.anim.Update(0);
        }

        // 播放玩家处决动画
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.Play(executionAnimName, attackLayerIndex, 0f);
        animator.Update(0);

        // 获取动画实际长度
        float playerLen = GetClipLength(animator, executionAnimName);
        float bossLen = GetClipLength(target.anim, bossBeExecutionAnimName);
        float waitTime = Mathf.Max(playerLen, bossLen, 0.1f);

        // 等待期间保持动画播放
        float timer = 0f;
        while (timer < waitTime)
        {
            if (target == null || target.anim == null) break;

            AnimatorStateInfo state = target.anim.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName(bossBeExecutionAnimName))
            {
                target.anim.Play(bossBeExecutionAnimName, 0, state.normalizedTime);
            }
            timer += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        // Boss 死亡
        if (target != null)
        {
            EnemyHealth bossHealth = target.GetComponent<EnemyHealth>();
            if (bossHealth != null)
                bossHealth.ForceDeath();
        }

        // 恢复玩家状态
        IsHyperArmor = false;
        InAction = false;
        isExecuting = false;
        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);
    }

    private float GetClipLength(Animator anim, string clipName)
    {
        if (anim == null || string.IsNullOrEmpty(clipName)) return 3f;
        RuntimeAnimatorController ctrl = anim.runtimeAnimatorController;
        if (ctrl == null) return 3f;
        foreach (AnimationClip clip in ctrl.animationClips)
            if (clip != null && clip.name == clipName)
                return clip.length;
        return 3f;
    }
}