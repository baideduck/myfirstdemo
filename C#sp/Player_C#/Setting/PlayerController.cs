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

    [Header("Dodge (Monster Hunter Style)")]
    [SerializeField] float dodgeDistance = 5f;
    [SerializeField] float dodgeDuration = 0.5f;
    [SerializeField] float invincibilityDuration = 0.2f;
    [SerializeField] float dodgeCooldown = 1f;

    private float ySpeed;
    private Quaternion targetRotation;

    private int combatLayerIndex;
    private float combatLayerWeight = 0f;
    private float moveLockTimer = 0f;
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
                Debug.Log("[调试] P键触发角力 PhaseThree");
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
        if (isDodging || (meleeFighter != null && (meleeFighter.IsBlocking || meleeFighter.IsInBlockRecovery)))
        {
            if (animator.applyRootMotion)
                animator.applyRootMotion = false;   // ← 格挡恢复期间强制关根运动
            ApplyGravityOnly();
            return;
        }
        if (meleeFighter != null && (meleeFighter.InAction || meleeFighter.IsStaggering))
        {
            if (animator.applyRootMotion)
                animator.applyRootMotion = false;
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
        Vector3 velocity = moveDir * currentSpeed;
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
        animator.applyRootMotion = false;
        ySpeed = -0.5f;

        isDodgeInputLocked = true;
        isDodging = true;
        canDodge = false;

        animator.ResetTrigger(dodgeTriggerID);
        animator.SetTrigger(dodgeTriggerID);
        isInvincible = true;

        float elapsed = 0f;
        Vector3 startPos = transform.position;

        while (elapsed < dodgeDuration)
        {
            Vector3 verticalMove = new Vector3(0, ySpeed, 0) * Time.deltaTime;
            float t = elapsed / dodgeDuration;
            t = Mathf.SmoothStep(0f, 1f, t);
            Vector3 targetPos = startPos + direction * dodgeDistance;
            Vector3 newPos = Vector3.Lerp(startPos, targetPos, t);
            Vector3 horizontalMove = newPos - transform.position;

            characterController.Move(horizontalMove + verticalMove);

            if (elapsed >= invincibilityDuration)
                isInvincible = false;

            elapsed += Time.deltaTime;
            yield return null;
        }

        isInvincible = false;

        Vector3 finalPos = startPos + direction * dodgeDistance;
        Vector3 finalDelta = finalPos - transform.position;
        finalDelta.y = 0;
        characterController.Move(finalDelta);

        ySpeed = 0f;
        animator.SetInteger(dodgeDirectionID, -1);

        yield return new WaitForSeconds(0.25f);

        isDodging = false;
        isDodgeInputLocked = false;

        float recoveryTime = 0.15f;
        float timer = 0f;
        while (timer < recoveryTime)
        {
            timer += Time.deltaTime;
            animator.SetFloat(moveAmountID, 0f);
            yield return null;
        }
        // 翻滚彻底结束前不解锁移动
        isDodgeInputLocked = false;
        moveLockTimer = dodgeCooldown;               // ← 整段dodgeCooldown期间锁住
        yield return new WaitForSeconds(dodgeCooldown);
        isDodging = false;                           // ← 挪到cooldown之后才解除
        canDodge = true;
        dodgeLocked = false;
        animator.applyRootMotion = originalRootMotion;
        animator.SetInteger(dodgeDirectionID, -1);
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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 0, 0.5f);
        Gizmos.DrawSphere(transform.TransformPoint(groundCheckOffset), groundCheckRadius);
    }
}