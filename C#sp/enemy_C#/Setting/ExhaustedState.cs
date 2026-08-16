using System.Collections;
using UnityEngine;

public class ExhaustedState : State<EnemyController>
{
    private EnemyController enemy;
    private bool finished = false;
    private float savedAttackLayerWeight;
    private int attackLayerIndex;
    private Coroutine freezeCoroutine;

    [Header("��ȡ���ض�����60֡��1.5���٣�")]
    [SerializeField] private float startAnimPlaySpeed = 1.5f;
    [SerializeField] private float startAnimOriginalLength = 1f;
    private float startAnimActualTime => startAnimOriginalLength / startAnimPlaySpeed;

    [Header("����ѭ��ʱ�����룩")]
    [SerializeField] private float loopDurationX = 0.3f;

    [Header("��λ��������")]
    [Header("���߽��������λ�ƾ���")]
    [SerializeField] private float recoverBackDistance = 5f;

    [Header("�󳷶�����")]
    [SerializeField] private string recoverAnimName = "ExitHasted_Start";

    [Header("��λ��ʱ�����룩")]
    [SerializeField] private float recoverMoveDuration = 0.6f;

    [Header("���������߶�����")]
    [SerializeField] private float hipsYOffset = 0.02f;

    // ��架�嵼�µĹ̶�����ʱ��
    [Header("��架�嵼�µĹ̶�����ʱ�䣩")]
    [SerializeField] private float postureBreakFloorDuration = 4f;

    // 狂暴打断透支惩罚：额外倒地时长（秒）。由 BossComboChain 在触发打断（体力不足 10%）前设置，力竭结束重置
    public float extraDownTime = 0f;

    // 是否由架势清空触发
    private bool isPostureBreak;

    // 动态碰撞体
    private Collider dynamicExhaustedHitbox;

    Transform hipsBone;
    Vector3 hipsLocalPos;
    Quaternion hipsLocalRot;

    public float TotalRestTime => startAnimActualTime + loopDurationX + recoverMoveDuration;

    public override void Enter(EnemyController owner)
    {
        enemy = owner;
        finished = false;

        // 读取触发来源，但不清掉标志——EnemyHealth 还需要它判断 ×1.5
        isPostureBreak = enemy.isPostureBreakExhaust;

        // 1. 记录髋骨位置
        if (hipsBone != null)
        {
            hipsLocalPos = hipsBone.localPosition;
            hipsLocalRot = hipsBone.localRotation;
        }

        // 2. �رո��˶��� CharacterController
        enemy.anim.applyRootMotion = false;
        CharacterController cc = enemy.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // 3. �رչ�����
        attackLayerIndex = enemy.anim.GetLayerIndex("Attack Layer");
        if (attackLayerIndex != -1)
        {
            savedAttackLayerWeight = enemy.anim.GetLayerWeight(attackLayerIndex);
            enemy.anim.SetLayerWeight(attackLayerIndex, 0f);
        }

        enemy.DisableAttackLayer();

        // 4. �ر�������ײ
        enemy.EnableWeaponHitBox(false, false);
        if (enemy.weaponHitBox != null)
            enemy.weaponHitBox.SetActive(false);

        // �� 5. ��̬�������ش��ܻ���
        CreateExhaustedHitbox();

        enemy.anim.SetBool("isExhausted", true);
        enemy.anim.speed = startAnimPlaySpeed;

        freezeCoroutine = enemy.StartCoroutine(ExhaustedSequence());

        // �� Enter ĩβ��������̬��ײ��֮��
        Collider mainCol = enemy.GetComponent<Collider>();
        if (mainCol != null) mainCol.enabled = true;
    }

    private void CreateExhaustedHitbox()
    {
        GameObject hitboxObj = new GameObject("ExhaustedHitbox_Temp");
        hitboxObj.transform.SetParent(enemy.transform);
        // ����ײ������̧�ߣ�����������������
        hitboxObj.transform.localPosition = new Vector3(0, 1.0f, 0);
        hitboxObj.transform.localRotation = Quaternion.identity;

        SphereCollider sc = hitboxObj.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 2.5f;   // ��΢�Ӵ�뾶���ô���������
        sc.center = Vector3.zero;

        // ȷ���� Enemy ��
        hitboxObj.layer = enemy.gameObject.layer;

        dynamicExhaustedHitbox = sc;
    }

    private IEnumerator ExhaustedSequence()
    {
        Vector3 frozenPosition = enemy.transform.position;
        Quaternion frozenRotation = enemy.transform.rotation;

        // ★ 二阶段力竭缩短(阶段4): phaseTwoExhaustShorten(0.7)
        BossPhaseManager pm = enemy.GetComponent<BossPhaseManager>();
        float shorten = (pm != null && pm.CurrentPhase == BossPhaseManager.BossPhase.PhaseTwoBurst) ? pm.PhaseTwoExhaustShorten : 1f;

        if (isPostureBreak)
        {
            // ===== 架势清空路径：播倒地动画 4 秒 =====
            // 设 bool 让 Animator Controller 走正常过渡
            enemy.anim.speed = 1f;
            enemy.anim.SetBool("isExhausted", true);

            // 强制起始帧
            enemy.anim.Play("Exhausted_Start", 0, 0f);
            enemy.anim.Update(0f);
            yield return null;
            AnimatorStateInfo startInfo = enemy.anim.GetCurrentAnimatorStateInfo(0);
            float startLen = startInfo.IsName("Exhausted_Start") ? startInfo.length : 0.5f;
            float startTimer = 0f;
            while (startTimer < startLen)
            {
                enemy.transform.position = frozenPosition;
                enemy.transform.rotation = frozenRotation;
                if (hipsBone != null)
                {
                    Vector3 lockedPos = hipsLocalPos;
                    lockedPos.y += hipsYOffset;
                    hipsBone.localPosition = lockedPos;
                    hipsBone.localRotation = hipsLocalRot;
                }
                startTimer += Time.deltaTime;
                yield return null;
            }

            // 留 4 秒让 Exhausted_Loop 循环播放
            float loopTimer = postureBreakFloorDuration * shorten;
            while (loopTimer > 0f)
            {
                enemy.transform.position = frozenPosition;
                enemy.transform.rotation = frozenRotation;
                if (hipsBone != null)
                {
                    Vector3 lockedPos = hipsLocalPos;
                    lockedPos.y += hipsYOffset;
                    hipsBone.localPosition = lockedPos;
                    hipsBone.localRotation = hipsLocalRot;
                }
                loopTimer -= Time.deltaTime;
                yield return null;
            }

            // 重置架势断裂标记
            enemy.isPostureBreakExhaust = false;
        }
        else
        {
            // ===== 体力耗尽路径：播倒地动画 + 等待恢复 =====
            // 阶段1：倒地动画（固定时长 + 狂暴打断透支延长）
            float freezeTimer = ((startAnimActualTime + loopDurationX) * shorten) + extraDownTime;
            while (freezeTimer > 0f)
            {
                enemy.transform.position = frozenPosition;
                enemy.transform.rotation = frozenRotation;

                if (hipsBone != null)
                {
                    Vector3 lockedPos = hipsLocalPos;
                    lockedPos.y += hipsYOffset;
                    hipsBone.localPosition = lockedPos;
                    hipsBone.localRotation = hipsLocalRot;
                }

                freezeTimer -= Time.deltaTime;
                yield return null;
            }

            // 阶段2：等待体力恢复到目标值
            BossStamina stamina = enemy.GetComponent<BossStamina>();
            if (stamina != null)
            {
                stamina.StartRecovery();
                while (!stamina.RecoveryComplete)
                {
                    enemy.transform.position = frozenPosition;
                    enemy.transform.rotation = frozenRotation;
                    if (hipsBone != null)
                    {
                        Vector3 lockedPos = hipsLocalPos;
                        lockedPos.y += hipsYOffset;
                        hipsBone.localPosition = lockedPos;
                        hipsBone.localRotation = hipsLocalRot;
                    }
                    yield return null;
                }
                stamina.ResetExhaustState();
            }

            // 体力路径起身后重置架势
            BossPosture posture = enemy.GetComponent<BossPosture>();
            posture?.ResetPosture();
        }

        // ===== 统一起身后撤 + Dodge =====
        enemy.anim.speed = 1f;
        enemy.anim.SetBool("isExhausted", false);

        Vector3 backDir = -enemy.transform.forward;
        backDir.y = 0;
        Vector3 startPos = enemy.transform.position;
        Vector3 targetPos = startPos + backDir * recoverBackDistance;
        targetPos.y = GetGroundHeight();

        if (!string.IsNullOrEmpty(recoverAnimName))
        {
            enemy.anim.Play(recoverAnimName, 0, 0f);
        }

        float elapsed = 0f;
        while (elapsed < recoverMoveDuration * shorten)
        {
            elapsed += Time.deltaTime;
            enemy.transform.position = Vector3.Lerp(startPos, targetPos, elapsed / recoverMoveDuration);
            yield return null;
        }
        enemy.transform.position = targetPos;

        finished = true;
    }

    private float GetGroundHeight()
    {
        Vector3 origin = enemy.transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5f, LayerMask.GetMask("Default")))
            return hit.point.y;
        return enemy.transform.position.y;
    }

    public override void Execute()
    {
        if (enemy == null) return;

        // �����ر�������ײ
        if (enemy.weaponHitBox != null && enemy.weaponHitBox.activeSelf)
            enemy.weaponHitBox.SetActive(false);

        if (finished)
            enemy.ChangeState(EnemyStates.Dodge);
    }

    public override void Exit()
    {
        if (freezeCoroutine != null) enemy.StopCoroutine(freezeCoroutine);

        // 站起来时恢复架势
        BossPosture posture = enemy.GetComponent<BossPosture>();
        if (posture != null) posture.ResetPosture();

        // 销毁动态碰撞体
        if (dynamicExhaustedHitbox != null)
        {
            Object.Destroy(dynamicExhaustedHitbox.gameObject);
            dynamicExhaustedHitbox = null;
        }

        if (enemy != null && enemy.anim != null)
        {
            if (attackLayerIndex != -1)
                enemy.anim.SetLayerWeight(attackLayerIndex, savedAttackLayerWeight);

            enemy.anim.speed = 1f;
            enemy.anim.SetBool("isExhausted", false);   // ★ 力竭被中途打断（如二阶段转场）时必须清标志，否则 Animator 会被 Common→Exhausted_Start 过渡拉回力竭动画，转场动画无法播放
            enemy.anim.applyRootMotion = true;

            CharacterController cc = enemy.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
        }

        // ★ 力竭被中途打断（转场/其他状态切换）时清理残留标记：
        //   1) isPostureBreakExhaust：否则伤害永久 ×1.5、下次体力力竭被误判为架势路径
        //   2) BossStamina.isExhausted：否则体力永不消耗（ConsumeStamina 直接 return）
        //   3) extraDownTime：否则透支延长残留到下次力竭
        enemy.isPostureBreakExhaust = false;
        extraDownTime = 0f;
        enemy.GetComponent<BossStamina>()?.ResetExhaustState();
    }
}
