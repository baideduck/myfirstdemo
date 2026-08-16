using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float MoveSpeed = 5f;
    [SerializeField] float rotationSpeed = 500f;

    [Header("Ground Check")]
    [SerializeField] float groundCheckRadius = 0.2f;
    [SerializeField] Vector3 groundCheckOffset;
    [SerializeField] LayerMask groundLayer;

    [Header("Movement Accel")]
    [SerializeField] float moveAccel = 20f;   // ★ 水平速度平滑加速度：越大越跟手（12≈0.15s、20≈0.08s 达到目标速度）

    [Header("Dodge (Monster Hunter Style)")]
    [SerializeField] float dodgeDistance = 5f;
    [SerializeField] float dodgeDuration = 0.5f;
    [SerializeField] float invincibilityDuration = 0.2f;
    [SerializeField] float dodgeCooldown = 0.45f;   // ★ 1→0.45：翻滚起身后连续闪避诉求不再被静默丢弃（"翻滚后卡"主因）

    private float ySpeed;
    private Quaternion targetRotation;

    private int combatLayerIndex;
    private float combatLayerWeight = 0f;
    private float moveLockTimer = 0f;
    private Vector3 smoothedVelocity = Vector3.zero;   // ★ 水平速度平滑：丝滑起步/刹车 + 翻滚起身无缝衔接
    public float combatLayerSmoothTime = 10f;

    private CameraController cameraController;
    private Animator animator;
    private CharacterController characterController;
    private MeeleFighter meleeFighter;

    private Coroutine currentDodgeCoroutine;
    private Coroutine currentSprintCoroutine;

    public bool IsInvincible => isInvincible;
    private bool isInvincible = false;
    private bool isDodging = false;
    private bool canDodge = true;
    private bool dodgeQueued = false;
    private Vector3 queuedDodgeDirection;
    private bool isDodgeInputLocked = false;
    private bool dodgeLocked = false;

    private int dodgeTriggerID;
    private int dodgeDirectionID;
    private int isDrawnID;
    private int moveAmountID;
    private int isBlockingID;
    private float staggerRecoveryTimer = 0f;
    private bool skipAttackThisFrame = false;

    private void Awake()
    {
        cameraController = Camera.main.GetComponent<CameraController>();
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
        meleeFighter = GetComponent<MeeleFighter>();

        combatLayerIndex = animator.GetLayerIndex("Combat Layer");
        animator.SetLayerWeight(combatLayerIndex, 0f);

        dodgeTriggerID = Animator.StringToHash("Dodge");
        dodgeDirectionID = Animator.StringToHash("DodgeDirection");
        isDrawnID = Animator.StringToHash("isDrawn");
        moveAmountID = Animator.StringToHash("moveAmount");
        isBlockingID = Animator.StringToHash("isBlocking");

        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.speed = 1f;
        animator.applyRootMotion = true;
    }

    private void Start()
    {
        isDodging = false;
        canDodge = true;
        dodgeQueued = false;
        isInvincible = false;
        animator.SetInteger(dodgeDirectionID, -1);
        animator.ResetTrigger(dodgeTriggerID);

        StartCoroutine(EnableDodgeAfterDelay());
    }

    private IEnumerator EnableDodgeAfterDelay()
    {
        canDodge = false;
        yield return new WaitForSeconds(0.2f);
        canDodge = true;
    }

    private void Update()
    {
        bool allowAttackThisFrame = true;

        // --- 处决（鼠标左键，Boss 处于 PhaseFinalFlee 且在范围内）---
        if (Input.GetMouseButtonDown(0) && !isDodging)
        {
            EnemyController boss = FindObjectOfType<EnemyController>();
            BossPhaseManager phaseMgr = FindObjectOfType<BossPhaseManager>();
            if (boss != null && phaseMgr != null && phaseMgr.CurrentPhase == BossPhaseManager.BossPhase.PhaseFinalFlee)
            {
                float dist = Vector3.Distance(transform.position, boss.transform.position);
                Collider bossCol = boss.GetComponent<Collider>();
                if (bossCol != null)
                {
                    Vector3 closest = bossCol.ClosestPoint(transform.position);
                    dist = Vector3.Distance(transform.position, closest);
                }
                if (dist < 5f)
                {
                    meleeFighter?.StartExecution(boss);
                }
            }
        }

        // --- 调试：直接进入角力流程（P键）---
        if (Input.GetKeyDown(KeyCode.P))
        {
            BossPhaseManager phaseMgr = FindObjectOfType<BossPhaseManager>();
            if (phaseMgr != null)
            {
                phaseMgr.TriggerPhaseThree();
            }
        }

        // --- 磨刀（F键）---
        if (Input.GetKeyDown(KeyCode.F))
        {
            meleeFighter?.TrySharpen();
        }

        // 移动输入传递
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        meleeFighter?.SetMoveInput(new Vector2(h, v));

        // 同步战斗状态到动画（离散值，不混合）
        if (meleeFighter != null)
        {
            float targetDrawn = meleeFighter.CurrentCombatState == MeeleFighter.CombatState.Drawn ? 1f : 0f;
            animator.SetFloat(isDrawnID, targetDrawn);
        }

        // --- 格挡（空格）---
        if (Input.GetKeyDown(KeyCode.Space) && !isDodging && !meleeFighter.InAction)
        {
            meleeFighter.TryStartBlock();
        }
        else if (Input.GetKeyUp(KeyCode.Space) && meleeFighter.IsBlocking)
        {
            meleeFighter.StopBlock();
        }

        // --- 闪避 / 冲刺（左Shift）---
        if (Input.GetKeyDown(KeyCode.LeftShift) && canDodge && !isDodging && !isDodgeInputLocked && !dodgeLocked
            && (meleeFighter == null || !meleeFighter.IsInBlockRecovery)
            && (meleeFighter == null || !meleeFighter.IsStaggering)
            && staggerRecoveryTimer <= 0f)
        {
            if (meleeFighter != null && meleeFighter.IsCharging)
            {
                meleeFighter.CancelCharge();
            }

            if (meleeFighter != null && meleeFighter.IsBlocking)
            {
                meleeFighter.StopBlock();
                return;
            }

            allowAttackThisFrame = false;
            dodgeLocked = true;

            if (meleeFighter != null && meleeFighter.CurrentCombatState == MeeleFighter.CombatState.Sheathed)
            {
                if (currentSprintCoroutine != null) StopCoroutine(currentSprintCoroutine);
                currentSprintCoroutine = StartCoroutine(PerformSprint());
            }
            else
            {
                // ★ 怪猎式：闪避消耗体力（PlayerStamina.TryDodge）；体力不足则本次闪避输入作废
                PlayerStamina stamina = GetComponent<PlayerStamina>();
                if (stamina != null && !stamina.TryDodge())
                {
                    dodgeLocked = false;
                    return;
                }

                bool wasInAction = meleeFighter != null && meleeFighter.InAction;
                if (meleeFighter != null && meleeFighter.TryInterruptActionOnDodge())
                {
                    if (wasInAction) meleeFighter.ResetAttackCombo();
                    if (currentDodgeCoroutine != null) StopCoroutine(currentDodgeCoroutine);
                    currentDodgeCoroutine = StartCoroutine(PerformDodge(CalculateDodgeDirection()));
                }
                else if (meleeFighter != null && meleeFighter.InAction)
                {
                    dodgeQueued = true;
                    queuedDodgeDirection = CalculateDodgeDirection();
                    dodgeLocked = false;
                }
                else
                {
                    if (currentDodgeCoroutine != null) StopCoroutine(currentDodgeCoroutine);
                    currentDodgeCoroutine = StartCoroutine(PerformDodge(CalculateDodgeDirection()));
                }
            }
        }

        // --- 攻击输入（闪避中完全禁止）---
        if (allowAttackThisFrame && !isDodging)
        {
            if (Input.GetMouseButtonDown(0))
            {
                meleeFighter?.TryAttack(AttackData.AttackInputType.Light);
            }
            if (Input.GetMouseButtonDown(1))
            {
                meleeFighter?.TryAttack(AttackData.AttackInputType.Heavy);
            }
        }

        // 攻击结束后执行缓存的闪避
        if (dodgeQueued && meleeFighter != null && !meleeFighter.InAction && !isDodging && !isDodgeInputLocked && !dodgeLocked)
        {
            dodgeQueued = false;
            dodgeLocked = true;
            if (currentDodgeCoroutine != null) StopCoroutine(currentDodgeCoroutine);
            currentDodgeCoroutine = StartCoroutine(PerformDodge(queuedDodgeDirection));
        }

        // --- 收刀（R键）---
        if (Input.GetKeyDown(KeyCode.R))
        {
            meleeFighter?.SheatheWeapon();
        }

        // 战斗层权重动态更新
        bool inCombatNow = meleeFighter != null && meleeFighter.CurrentCombatState == MeeleFighter.CombatState.Drawn;
        float targetWeight = inCombatNow ? 1f : 0f;
        combatLayerWeight = Mathf.Lerp(combatLayerWeight, targetWeight, Time.deltaTime * combatLayerSmoothTime);
        animator.SetLayerWeight(combatLayerIndex, combatLayerWeight);

        // ========== 移动相关 ==========
        bool hasBlock = meleeFighter != null && (meleeFighter.IsBlocking || meleeFighter.IsInBlockRecovery);
        bool hasAction = meleeFighter != null && (meleeFighter.InAction || meleeFighter.IsStaggering);

        if (isDodging || hasBlock)
        {
            // ★ 不再强制关根运动：闪避/格挡的位移由动画根运动驱动（OnAnimatorMove 经 CharacterController 应用）
            ApplyGravityOnly();
            return;
        }
        if (hasAction)
        {
            // ★ 不再强制关根运动：攻击/蓄力释放的位移由动画根运动驱动（OnAnimatorMove 经 CharacterController 应用）
            ApplyGravityOnly();
            return;
        }

        if (moveLockTimer > 0f)
        {
            moveLockTimer -= Time.deltaTime;
            ApplyGravityOnly();
            return;
        }

        HandleMovement(h, v);
    }

    private void ApplyGravityOnly()
    {
        bool isGrounded = Physics.CheckSphere(transform.TransformPoint(groundCheckOffset), groundCheckRadius, groundLayer);
        if (isGrounded)
            ySpeed = -0.5f;
        else
            ySpeed += Physics.gravity.y * Time.deltaTime;

        characterController.Move(new Vector3(0, ySpeed, 0) * Time.deltaTime);
    }

    private void HandleMovement(float h, float v)
    {
        float targetMoveAmount = Mathf.Clamp01(Mathf.Abs(h) + Mathf.Abs(v));
        ApplyMovement(h, v, targetMoveAmount);
    }

    private void ApplyMovement(float h, float v, float moveAmount)
    {
        float currentSpeed = MoveSpeed;
        if (meleeFighter != null && meleeFighter.CurrentCombatState == MeeleFighter.CombatState.Drawn)
            currentSpeed = MoveSpeed * 0.6f;

        Vector3 moveInput = new Vector3(h, 0, v).normalized;
        Vector3 moveDir = cameraController.PlanarRotation * moveInput;
        Vector3 targetVelocity = moveDir * currentSpeed;

        // ★ 水平速度指数平滑（丝滑起步/刹车）：
        //   翻滚/攻击/格挡期间 ApplyMovement 暂停 → smoothedVelocity 冻结在暂停前的值；
        //   恢复移动时从冻结值无缝继续——按住方向键翻滚时，起身瞬间直接以走路速度移动，
        //   消除"速度 0 → 全速突变"的卡一下；站立起步也从 0 柔和加速（更真实跟手）。
        float accel = 12f;   // 加速度系数：越大越跟手（≈0.15s 达到目标速度）
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, targetVelocity, 1f - Mathf.Exp(-accel * Time.deltaTime));
        Vector3 velocity = smoothedVelocity;
        velocity.y = ySpeed;

        bool isGrounded = Physics.CheckSphere(transform.TransformPoint(groundCheckOffset), groundCheckRadius, groundLayer);
        characterController.Move(velocity * Time.deltaTime);

        if (isGrounded)
            ySpeed = -0.5f;
        else
            ySpeed += Physics.gravity.y * Time.deltaTime;

        if (moveAmount > 0 && moveDir != Vector3.zero)
            targetRotation = Quaternion.LookRotation(moveDir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

        // ===== 动画参数设置 =====
        // 1. 方向参数（用于2D Blend Tree，拔刀时有效）
        float directionParam = 0f;
        if (moveAmount > 0.1f)
        {
            Vector3 worldMoveDir = moveDir.normalized;
            float angle = Vector3.SignedAngle(transform.forward, worldMoveDir, Vector3.up);
            directionParam = angle / 180f;   // 范围 -1 ~ 1
        }
        animator.SetFloat("Direction", directionParam);   // 注意：参数名必须与Animator中完全一致（大写D）

        // 2. 转身角度（用于空闲转身动画，可选）
        float turnAngle = Vector3.SignedAngle(transform.forward, targetRotation.eulerAngles, Vector3.up);
        float turnParam = turnAngle / 180f;
        animator.SetFloat("turnAngle", turnParam);

        // 3. 移动量参数（核心，驱动1D Blend Tree）
        animator.SetFloat(moveAmountID, moveAmount, 0.2f, Time.deltaTime);
    }

    private Vector3 CalculateDodgeDirection()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector2 moveInput = new Vector2(h, v);
        Vector3 worldDir;
        int directionIndex;

        if (moveInput != Vector2.zero)
        {
            Vector3 inputDir = new Vector3(moveInput.x, 0, moveInput.y).normalized;
            worldDir = cameraController.PlanarRotation * inputDir;
            float angle = Vector3.SignedAngle(transform.forward, worldDir, Vector3.up);
            if (angle >= -45f && angle < 45f) directionIndex = 0;
            else if (angle >= 135f || angle < -135f) directionIndex = 1;
            else if (angle >= 45f && angle < 135f) directionIndex = 2;
            else directionIndex = 3;
        }
        else
        {
            worldDir = -transform.forward;
            directionIndex = 1;
        }

        animator.SetInteger(dodgeDirectionID, directionIndex);
        return worldDir.normalized;
    }

    private IEnumerator PerformDodge(Vector3 direction)
    {
        bool originalRootMotion = animator.applyRootMotion;
        // ★ 闪避位移改由动画根运动驱动（Dodge_F/B/L/R 已切换为 Root 版片段），不再用代码平移 dodgeDistance
        animator.applyRootMotion = true;
        ySpeed = -0.5f;

        isDodgeInputLocked = true;
        isDodging = true;
        canDodge = false;

        animator.ResetTrigger(dodgeTriggerID);
        // ★ 强制播放 Dodge 动画（不依赖 Trigger 过渡）：修复连续闪避时第二次不播动画/直接滑步的问题。
        //   按 DodgeDirection 选择对应翻滚动画（0前/1后/2右/3左），Play 直接切换 Base Layer 状态，Trigger 被吞也无影响。
        int dirIdx = animator.GetInteger(dodgeDirectionID);
        if (dirIdx < 0 || dirIdx > 3) dirIdx = 1;   // 默认后滚
        string dodgeAnim = dirIdx switch
        {
            0 => "Dodge_F",
            1 => "Dodge_B",
            2 => "Dodge_R",
            3 => "Dodge_L",
            _ => "Dodge_B"
        };
        animator.Play(dodgeAnim, 0, 0f);
        isInvincible = true;

        // ★ 等动画真正播完：normalizedTime≥1 判定 + 状态切走兜底。
        //   注意：Dodge 动画播完会自动经 Exit=1 过渡回 Blend Tree，此时 GetCurrentAnimatorStateInfo 的
        //   IsName(dodgeAnim) 会变 false——若只判断 IsName&&normalizedTime 会永远不满足 → isDodging 死等 6s 超时
        //   → 期间走路动画原地踏步、无法移动（恶性 bug）。因此用 dodgeStarted 标记：已开始播 Dodge 后，
        //   状态一旦切走（回 Blend）立即视为播完退出。
        float elapsed = 0f;
        float maxDodgeTime = 6f;   // 超时兜底（Dodge 动画最长约 5.3s @30fps）
        bool dodgeStarted = false;
        while (elapsed < maxDodgeTime)
        {
            if (elapsed >= invincibilityDuration)
                isInvincible = false;

            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName(dodgeAnim))
            {
                dodgeStarted = true;
                if (st.normalizedTime >= 1f)
                    break;   // 动画播完
            }
            else if (dodgeStarted)
            {
                break;   // 已播完并切回 Blend Tree（动画播完的另一种信号）
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        isInvincible = false;
        ySpeed = 0f;

        // ★ 起身：立即交还移动，无缝衔接。
        //   关键：不强制 SetFloat(moveAmountID, 0f) 归零——玩家按住方向键翻滚时，翻滚中 Update 早退导致
        //   moveAmount 保持翻滚前的走路值(如0.5)，起身后直接驱动走路动画立即衔接，位移(CC全速)与动画同步 → 丝滑。
        //   若归零，走路动画要从 0 平滑爬升(0.2s 阻尼≈0.3s)，而 CC 已全速 → 起步滑步"卡一下"。
        //   不按方向键时 moveAmount 本来就是 0 → Idle 站立，无残留走路问题（"原地走路"根因是 isDodging 死等，已修复）。
        //   同样不 CrossFade：Animator 4 方向 Dodge 均已配 Exit=1 自动回 Blend，CrossFade 反而会重启走路动画造成起步卡顿。
        isDodging = false;
        isDodgeInputLocked = false;
        dodgeLocked = false;
        animator.SetInteger(dodgeDirectionID, -1);
        animator.applyRootMotion = originalRootMotion;

        // 仅保留闪避冷却：dodgeCooldown 内不可再次闪避，但移动不受锁
        yield return new WaitForSeconds(dodgeCooldown);
        canDodge = true;
    }

    private IEnumerator PerformSprint()
    {
        if (meleeFighter != null && meleeFighter.InAction && meleeFighter.IsCurrentAttackHeavy)
        {
            dodgeLocked = false;
            yield break;
        }

        if (meleeFighter != null && meleeFighter.InAction)
        {
            dodgeLocked = false;
            yield break;
        }

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 direction;
        if (new Vector2(h, v).magnitude > 0.1f)
        {
            Vector3 inputDir = new Vector3(h, 0, v).normalized;
            direction = cameraController.PlanarRotation * inputDir;
        }
        else
        {
            direction = transform.forward;
        }

        PlayerStamina stamina = GetComponent<PlayerStamina>();
        if (stamina == null || !stamina.Consume(5f))
        {
            dodgeLocked = false;
            yield break;
        }

        isDodging = true;
        float sprintSpeed = 8f;
        animator.speed = 1.5f;
        bool originalRootMotion = animator.applyRootMotion;
        animator.applyRootMotion = false;
        ySpeed = 0f;

        while (Input.GetKey(KeyCode.LeftShift) && stamina.ConsumeOverTime(20f))
        {
            float delta = sprintSpeed * Time.deltaTime;
            Vector3 move = direction * delta;
            move.y = 0;
            characterController.Move(move);
            yield return null;
        }

        animator.speed = 1f;
        animator.SetFloat(moveAmountID, 0f);
        animator.applyRootMotion = true;
        animator.Play("Sprint_End", 0, 0f);
        yield return new WaitForSeconds(0.3f);

        animator.applyRootMotion = originalRootMotion;
        animator.SetFloat(moveAmountID, 0f);

        isDodging = false;
        dodgeLocked = false;

        yield return new WaitForSeconds(dodgeCooldown);
        canDodge = true;
    }

    public void ForceEndInvincibility() => isInvincible = false;
    public void ResetStaggerRecoveryTimer() => staggerRecoveryTimer = 0.15f;
    public void ClearStaggerRecoveryTimer() => staggerRecoveryTimer = 0f;
    public void LockMovement(float duration) => moveLockTimer = duration;

    public void ForceDisableDodge()
    {
        dodgeQueued = false;
        isDodgeInputLocked = false;
        canDodge = false;
        if (currentDodgeCoroutine != null) StopCoroutine(currentDodgeCoroutine);
        if (currentSprintCoroutine != null) StopCoroutine(currentSprintCoroutine);
        StartCoroutine(ReEnableDodgeAfterDelay(0.3f));
    }

    private IEnumerator ReEnableDodgeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        canDodge = true;
    }

    public void ForceUnlockDodge()
    {
        if (currentDodgeCoroutine != null) StopCoroutine(currentDodgeCoroutine);
        if (currentSprintCoroutine != null) StopCoroutine(currentSprintCoroutine);
        dodgeLocked = false;
        dodgeQueued = false;
        isDodgeInputLocked = false;
        canDodge = true;
        isDodging = false;
    }

    /// <summary>
    /// 根运动应用入口：applyRootMotion=true 时（闪避/攻击/蓄力释放/格挡击退等），
    /// 把动画 deltaPosition 通过 CharacterController 应用，避免 transform 直接位移与 CC 碰撞体脱节。
    /// 注：MeeleFighter 原有的 OnAnimatorMove 已移除，统一由这里处理，防止重复应用导致双倍位移。
    /// </summary>
    private void OnAnimatorMove()
    {
        if (animator == null || characterController == null) return;
        if (animator.applyRootMotion)
            characterController.Move(animator.deltaPosition);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 0, 0.5f);
        Gizmos.DrawSphere(transform.TransformPoint(groundCheckOffset), groundCheckRadius);
    }
}