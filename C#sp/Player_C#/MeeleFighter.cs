using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeeleFighter : MonoBehaviour
{
    [Header("��������")]
    [SerializeField] List<AttackData> allMoves;

    [Header("����ģ��")]
    [SerializeField] public GameObject weaponRight;
    [SerializeField] public GameObject weaponLeft;
    [SerializeField] public GameObject weaponBack;

    [Header("�յ�/�ε�����")]
    [SerializeField] string equipAnimName = "GreatSword_Equip_Inplace";
    [SerializeField] string sheatheAnimName = "GreatSword_Unarm_Inplace";

    [Header("��")]
    [SerializeField] bool enableBlocking = true;
    [SerializeField] float blockStaminaCostPerSecond = 0f;
    [SerializeField] float blockSharpnessCostPerHit = 0f;
    [SerializeField] float blockEndTransitionDelay = 0.3f;
    [SerializeField] float blockCooldown = 0.35f;

    [Header("ĥ��")]
    [SerializeField] float sharpenDuration = 2.5f;
    [SerializeField] string sharpenAnimName = "Sharpen";

    [Header("��������")]
    [SerializeField] private float minComboWindow = 0.15f;   // ������ʼ�����ٲ�����ô�ò����������ν�
    private bool isCommittingToAttack = false;   // �����ύ�У���ֹ������

    [Header("����")]
    [SerializeField] private string executionAnimName = "Execution";     // ��Ҵ���������
    [SerializeField] private float executionAnimLength = 2f;             // ��������ʱ��
    [SerializeField] private string bossBeExecutionAnimName = "BeExecution"; // Boss ������������
    private bool isExecuting = false;

    [Header("������Ч")]
    [SerializeField] private GameObject chargeLevel2Effect;   // ������������1�룩ʱ����Ч
    [SerializeField] private GameObject chargeLevel3Effect;   // ������������2�룩ʱ����Ч


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
    private float lastChargeEndTime = -10f;   // ��������ʱ�䣬������ȴ
    public bool CanBeInterrupted { get; private set; } = false;
    private Coroutine blockCoroutine;
    private bool isBlockCoroutineActive = false;
    private float lastBlockEndTime = -10f;
    private bool isSharpening = false;
    private Coroutine currentMoveCoroutine = null;

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
        if (isCommittingToAttack) return;   // �ύ�У���ֹ�κι���

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

        // ֹͣ�����Э�̺�״̬
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

        // �� �����Ʒ�������ȷ�� Animator ���� "BlockFail" �� "Block_Fail" ��������

        // 关闭 Root Motion，防止 Block_Fail 动画残留驱动角色滑步
        animator.applyRootMotion = false;

        // 禁用当前武器碰撞箱（BlockRoutine 切换到左手武器的那把）
        if (activeWeaponCollider)
            activeWeaponCollider.enabled = false;

        animator.SetBool("isBlocking", false);
        animator.ResetTrigger("BlockStart");
        animator.ResetTrigger("BlockEnd");

        // �� �����Ʒ�������ȷ�� Animator ���� "BlockFail" �� "Block_Fail" ��������
        animator.SetTrigger("BlockFail");

        // �л�����������ʾ�������ʱ�õ�������������
        if (CurrentCombatState == CombatState.Drawn)
            SwitchToRightWeapon();

        // 禁用右手武器碰撞箱（SwitchToRightWeapon 刚激活的）
        if (activeWeaponCollider)
            activeWeaponCollider.enabled = false;

        // InAction 不由 TriggerBlockFail 管理，由调用方（PlayerDefense.OnBlockFailed）决定
        lastBlockEndTime = Time.time;

        // �ӳٽ���Ӳֱ
        StartCoroutine(DelayedRecoveryUnlock(0.5f)); // ����ͨ�񵲺�ҡ����
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

        // �����ͷŽ׶β�������
        if (currentMove != null && currentMove.IsChargeable)
            return false;

        // �� ���������ƣ�ֻ���ڹ��������ĺ� 20% ȡ����ҡ
        if (animator != null && attackLayerIndex >= 0)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
            float progress = state.length > 0.001f ? state.normalizedTime % 1f : 0f;
            if (progress < 0.8f)
                return false;   // ǰ 80% ��ֹ����ȡ�������ܻᱻ PlayerController ����
        }

        // ��������ȡ��
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
            // �յ�״̬���ε� �� ĥ�� �� �յ�
            StartCoroutine(SharpenFromSheathedRoutine());
        }
        else
        {
            // �ε�״̬��ĥ�� �� �յ�
            StartCoroutine(SharpenFromDrawnRoutine());
        }
    }
    private IEnumerator SharpenFromSheathedRoutine()
    {
        isSharpening = true;
        InAction = true;

        // --- ��һ�����ε� ---
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

        // --- �ڶ�����ĥ�� ---
        animator.CrossFade(sharpenAnimName, 0.1f, attackLayerIndex);
        yield return new WaitForSeconds(sharpenDuration);
        PlayerSharpness sharpness = GetComponent<PlayerSharpness>();
        sharpness?.Sharpen();

        // --- ���������յ� ---
        animator.CrossFade(sheatheAnimName, 0.1f, attackLayerIndex);
        yield return null;
        var sheatheState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float sheatheLength = sheatheState.length;
        float sheatheAttachTime = sheatheLength * (60f / 90f);
        yield return new WaitForSeconds(sheatheAttachTime);

        // �л�����������
        SwitchToBackWeapon();
        // �� ǿ�ư�����ģ�͹ҵ����ϣ��������б����ҵ�Ļ���
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

        // --- ��һ����ĥ�� ---
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.CrossFade(sharpenAnimName, 0.1f, attackLayerIndex);
        yield return new WaitForSeconds(sharpenDuration);
        PlayerSharpness sharpness = GetComponent<PlayerSharpness>();
        sharpness?.Sharpen();

        // --- �ڶ������յ� ---
        // --- �ڶ������յ� ---
        animator.CrossFade(sheatheAnimName, 0.1f, attackLayerIndex);
        yield return null;
        var sheatheState = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        float sheatheLength = sheatheState.length;
        float sheatheAttachTime = sheatheLength * (60f / 90f);
        yield return new WaitForSeconds(sheatheAttachTime);

        // �л�����������
        SwitchToBackWeapon();
        // �� ǿ�ư�����ģ�͹ҵ�����
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
        float[] thresholds = move.ChargeThresholds;
        isCommittingToAttack = false;
        IsCharging = true;
        while (Input.GetKey(KeyCode.Mouse1))
        {
            if (!IsCharging) yield break;   // ��ȡ���������˳�
            chargeTimer += Time.unscaledDeltaTime;

            if (thresholds.Length >= 2 && chargeTimer >= thresholds[1])
                chargeLevel = 3;
            else if (thresholds.Length >= 1 && chargeTimer >= thresholds[0])
                chargeLevel = 2;
            else
                chargeLevel = 1;

            // �����׶���Ч
            if (chargeLevel >= 2 && chargeLevel2Effect != null && !chargeLevel2Effect.activeSelf)
                chargeLevel2Effect.SetActive(true);

            if (chargeLevel >= 3 && chargeLevel3Effect != null && !chargeLevel3Effect.activeSelf)
            {
                chargeLevel3Effect.SetActive(true);
                Debug.Log("������Ч�Ѽ���");
            }
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

        // ����������Ч
        if (chargeLevel2Effect != null) chargeLevel2Effect.SetActive(false);
        if (chargeLevel3Effect != null) chargeLevel3Effect.SetActive(false);

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

            timer += Time.deltaTime;
            float normTime = Mathf.Clamp01(timer / animLength);

            if (!impactActive && normTime >= impactStart / animLength && normTime <= impactEnd / animLength)
            {
                impactActive = true;
                IsHyperArmor = true;

                CameraController camCtrl = Camera.main?.GetComponent<CameraController>();
                if (chargeLevel == 2) camCtrl?.TriggerTier2ChargeShake();
                else if (chargeLevel >= 3) camCtrl?.TriggerHeavySlashImpact(enemyPos);

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

        if (chargeLevel >= 3)
        {
            AttackData secondAttack = GetAttackDataByAnimName("ChargeCombo");
            if (secondAttack == null) secondAttack = GetAttackDataByMoveID("ChargeCombo");
            if (secondAttack != null)
            {
                if (activeWeaponCollider) activeWeaponCollider.enabled = false;
                canDamageThisAttack = true;
                yield return StartCoroutine(PerformMove(secondAttack));
                yield break;
            }
        }

        IsHyperArmor = false;
        EndAction();
        lastChargeEndTime = Time.time;   // ��¼��ȴ
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
    /// ȡ����ǰ���������ͷŹ���
    /// </summary>
    public void CancelCharge()
    {
        isCommittingToAttack = false;
        IsCharging = false;

        if (chargeLevel2Effect != null) chargeLevel2Effect.SetActive(false);
        if (chargeLevel3Effect != null) chargeLevel3Effect.SetActive(false);
        // ֹͣ���й������Э��
        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }

        // ֹͣ MeeleFighter ������Э�̣�ȷ�� ChargeAttack �� while ѭ��Ҳ��ɱ��
        StopAllCoroutines();

        // ����״̬
        IsCurrentAttackHeavy = false;
        InAction = false;
        currentMove = null;
        bufferedMove = null;

        if (activeWeaponCollider != null)
            activeWeaponCollider.enabled = false;

        // �ָ�����
        animator.speed = 1f;
        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);
    }

    /// <summary>
    /// ���ù������Σ��´ι����ӵ�һ�ο�ʼ
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
        animator.applyRootMotion = true;
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.CrossFade(move.AnimName, 0.1f, attackLayerIndex);
        yield return new WaitForSeconds(0.15f);
        isCommittingToAttack = false;

        var state = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        if (state.length < 0.02f) yield break;
        float timer = 0f;
        bool impactActive = false;

        if (activeWeapon != null)
        {
            PlayerWeaponHitbox hitbox = activeWeapon.GetComponent<PlayerWeaponHitbox>();
            hitbox?.PlaySwingSound();
        }

        while (timer < state.length)
        {
            timer += Time.deltaTime;
            float normTime = Mathf.Clamp01(timer / state.length);

            // �������ڣ���һ���� minComboWindow�������μӳ� 1.5 ��
            float effectiveWindow = minComboWindow;
            if (currentMove != null && !string.IsNullOrEmpty(currentMove.MoveID) && currentMove.MoveID != "Attack1")
                effectiveWindow = minComboWindow * 1.5f;

            if ((move.MoveID == "Combo2" || move.MoveID == "Slash3") && timer >= state.length * 0.95f)
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

            if (!impactActive && normTime >= move.ImpactStartTime && normTime <= move.ImpactEndTime)
            {
                impactActive = true;

                if (activeWeapon != null)
                    activeWeaponCollider = activeWeapon.GetComponentInChildren<Collider>(true);

                if (activeWeaponCollider)
                {
                    activeWeaponCollider.enabled = true;
                    var weaponHitbox = activeWeaponCollider.GetComponent<PlayerWeaponHitbox>();
                    if (weaponHitbox != null)
                        weaponHitbox.damage = move.Damage;
                }
            }
            else if (impactActive && normTime > move.ImpactEndTime)
            {
                impactActive = false;
                if (activeWeaponCollider) activeWeaponCollider.enabled = false;
            }

            yield return null;
        }

        animator.speed = 1f;   // �ָ�
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
        if (activeWeaponCollider) activeWeaponCollider.enabled = false;
        animator.applyRootMotion = false;
        // ����ƽ������ Idle ��Э�̣������������㹥����
        StartCoroutine(SmoothExitAttackLayer());
    }
    private IEnumerator SmoothExitAttackLayer()
    {
        // �� Combo2 �� Slash3 ����β�� Idle �������Ҫ������������������
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

        while (IsBlocking) yield return null;
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
        animator.applyRootMotion = false;           // �� ����һ�Σ���ֹ�������ط�����
        if (playerController != null)
            playerController.LockMovement(0.4f);
    }
    private void OnAnimatorMove()
    {
        if (animator.applyRootMotion && InAction)
            characterController.Move(animator.deltaPosition);
    }

    // ===================== �ܻ��뵯����ֱ�Ӳ��Ű棩 =====================
    public void PlayHitReaction(Vector3 hitDirectionWorld)
    {
        playerController?.ForceEndInvincibility();

        if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
        if (bounceCoroutine != null) StopCoroutine(bounceCoroutine);

        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }
        if (activeWeaponCollider) activeWeaponCollider.enabled = false;

        IsStaggering = true;
        InAction = true;
        CanBeInterrupted = false;
        playerController?.ResetStaggerRecoveryTimer();
        playerController?.ForceDisableDodge();

        animator.SetLayerWeight(attackLayerIndex, 0f);
        animator.Play("Empty", attackLayerIndex);

        // �����ƶ���������ֹ Any State ���߶���
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

        if (hitReactionCoroutine != null) StopCoroutine(hitReactionCoroutine);
        if (bounceCoroutine != null) StopCoroutine(bounceCoroutine);

        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }
        if (activeWeaponCollider) activeWeaponCollider.enabled = false;

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
            pc.ClearStaggerRecoveryTimer();   // �� ֱ�����㣬��������0.15�붳��
            pc.LockMovement(0f);
            pc.ForceUnlockDodge();            // �� �ڲ��Ѱ��� StopAllCoroutines
        }

        // ���Ᵽ�գ�������������
        StartCoroutine(ReEnableDodgeImmediately());

        IsStaggering = false;
        InAction = false;
        CanBeInterrupted = true;
        currentMove = null;
        bufferedMove = null;
        IsCurrentAttackHeavy = false;

        if (CurrentCombatState == CombatState.Drawn)
            SwitchToRightWeapon();
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
    /// ���������ľ�ƣ������
    /// </summary>
    public void PlayExhausted()
    {
        if (IsStaggering || InAction) return;

        IsStaggering = true;
        InAction = true;

        animator.Play("Exhausted", 0, 0f);
        StartCoroutine(RecoverFromExhausted());
    }

    private IEnumerator RecoverFromExhausted()
    {
        yield return new WaitForSeconds(2f);
        IsStaggering = false;
        InAction = false;
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
    /// ��ʼ�������� PlayerController ����
    /// </summary>
    /// <summary>
    /// ��ʼ�������� PlayerController ����
    /// </summary>
    public void StartExecution(EnemyController target)
    {
        if (isExecuting) return;
        StartCoroutine(ExecutionRoutine(target));
    }

    private IEnumerator ExecutionRoutine(EnemyController target)
    {
        Debug.Log("[����] ExecutionRoutine ��ʼ");
        isExecuting = true;

        // ������Ҳ���
        InAction = true;
        CanBeInterrupted = false;
        IsHyperArmor = true;

        Vector3 dir = (target.transform.position - transform.position).normalized;
        dir.y = 0;
        if (dir.magnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);

        if (activeWeaponCollider != null)
            activeWeaponCollider.enabled = false;

        // ֹͣ Boss �Ķ���Э�̣���ֹ��ÿ֡�� Boss ����ԭ��
        BossPhaseManager phaseMgr = target.GetComponent<BossPhaseManager>();
        if (phaseMgr != null)
            phaseMgr.StopExecutionSequence();

        // ֹͣ Boss һ����Ϊ
        target.StopCurrentAttack();
        target.EnableWeaponHitBox(false, false);
        target.HasSuperArmor = true;
        target.DisableAttackLayer();

        // �ⶳ Boss
        target.isExecutionFrozen = false;

        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;

        // �� ǿ�ƿ������˶����ö���λ����Ч
        target.anim.applyRootMotion = true;
        target.anim.speed = 1f;
        target.anim.Rebind();
        target.anim.Update(0);

        // �������в㣬ֻ���� Base Layer
        for (int i = 1; i < target.anim.layerCount; i++)
            target.anim.SetLayerWeight(i, 0f);

        // ���ű���������
        target.anim.Play(bossBeExecutionAnimName, 0, 0f);
        target.anim.Update(0);

        // ��֤
        AnimatorStateInfo bossState = target.anim.GetCurrentAnimatorStateInfo(0);
        Debug.Log($"[����] Boss ����: {bossState.IsName(bossBeExecutionAnimName)}, speed={target.anim.speed}");

        if (bossState.normalizedTime < 0.01f)
        {
            Debug.LogWarning("[����] normalizedTime Ϊ 0��ǿ���ƶ�����");
            target.anim.Play(bossBeExecutionAnimName, 0, 0.01f);
            target.anim.Update(0);
        }

        // ������Ҵ�������
        animator.SetLayerWeight(attackLayerIndex, 1f);
        animator.Play(executionAnimName, attackLayerIndex, 0f);
        animator.Update(0);
        Debug.Log($"[����] ��Ҷ��� {executionAnimName} �Ѳ���");

        // ��ȡ������ʵ����
        float playerLen = GetClipLength(animator, executionAnimName);
        float bossLen = GetClipLength(target.anim, bossBeExecutionAnimName);
        float waitTime = Mathf.Max(playerLen, bossLen, 0.1f);
        Debug.Log($"[����] �ȴ�ʱ��: {waitTime}s");

        // �ȴ��ڼ䱣��������������
        float timer = 0f;
        while (timer < waitTime)
        {
            if (target == null || target.anim == null) break;

            AnimatorStateInfo state = target.anim.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName(bossBeExecutionAnimName))
            {
                Debug.LogWarning("[����] ���������ߣ����²���");
                target.anim.Play(bossBeExecutionAnimName, 0, state.normalizedTime);
            }
            timer += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        // Boss ����
        if (target != null)
        {
            EnemyHealth bossHealth = target.GetComponent<EnemyHealth>();
            if (bossHealth != null)
                bossHealth.ForceDeath();
        }

        // �ָ����״̬
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