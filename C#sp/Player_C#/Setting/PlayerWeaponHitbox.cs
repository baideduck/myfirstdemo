using System.Collections;
using UnityEngine;

public class PlayerWeaponHitbox : MonoBehaviour
{
    [HideInInspector] public float damage;
    [HideInInspector] public bool isChargeAttack = false;

    // ── 蓄力顿帧分档（照抄MHW大剑手感）──
    [Header("顿帧分档（秒）")]
    [SerializeField] private float hitStopLv1 = 0.08f;   // 一蓄
    [SerializeField] private float hitStopLv2 = 0.15f;   // 二蓄

    private GameObject owner;
    private int lastHitFrame = -1;
    private GameObject lastHitTarget;
    private MeeleFighter meleeFighter;
    private AudioSource audioSource;

    [Header("��Ч")]
    public AudioClip swingSound;            // �ջ��ƿ���
    public AudioClip hitEnemySound;         // ��ͨ���е���
    public AudioClip heavyHitSound;         // �ػ����е���
    public AudioClip groundHitSound;        // ���е����ײ����
    public AudioClip earthHitSound;         // ��ʯ�ɽ���
    public AudioClip bounceSound;           // ������
    public AudioClip equipSound;            // �ε���
    public AudioClip sheatheSound;          // �յ���

    // ������Ч��ȴ���ӳ�
    private float lastGroundHitTime = -1f;
    private float groundHitCooldown = 0.5f;     // ��ȴ��Ϊ 0.5 ��
    private float groundHitDelay = 0.1f;        // �ӳ� 0.3 �벥��
    private Coroutine groundSoundCoroutine;     // ��ֹ�ظ�����Э��

    private void Awake()
    {
        owner = GetComponentInParent<MeeleFighter>()?.gameObject;
        meleeFighter = GetComponentInParent<MeeleFighter>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.volume = 1f;
    }

    public void ResetHitState()
    {
        lastHitFrame = -1;
        lastHitTarget = null;
    }

    /// <summary>
    /// 强制清空命中记录（蓄力攻击释放时调用，防止上一帧残留）
    /// </summary>
    public void ForceClearHitRecord()
    {
        lastHitFrame = -1;
        lastHitTarget = null;
    }

    // ===================== �������ŷ��� =====================

    /// <summary>
    /// �ջ���Ч���� MeeleFighter �ڹ���������ʼʱ���ã�
    /// </summary>
    public void PlaySwingSound()
    {
        if (audioSource != null && swingSound != null)
            audioSource.PlayOneShot(swingSound);
    }

    /// <summary>
    /// �ε���Ч���� MeeleFighter �ڰε�������ʼʱ���ã�
    /// </summary>
    public void PlayEquipSound()
    {
        if (audioSource != null && equipSound != null)
            audioSource.PlayOneShot(equipSound);
    }

    /// <summary>
    /// �յ���Ч���� MeeleFighter ���յ�������ʼʱ���ã�
    /// </summary>
    public void PlaySheatheSound()
    {
        if (audioSource != null && sheatheSound != null)
            audioSource.PlayOneShot(sheatheSound);
    }

    /// <summary>
    /// ����������ײ�岢����һ֡�������ã����ڵ�����Чˢ�£���ѡ��
    /// </summary>
    public IEnumerator ResetAndEnableCollider()
    {
        if (gameObject.TryGetComponent<Collider>(out var col))
        {
            col.enabled = false;
            yield return null;
            col.enabled = true;
        }
    }

    // ===================== ��ײ��� =====================

    private void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.transform.root == owner.transform) return;

        int otherLayer = other.gameObject.layer;
        bool isEnemy = (otherLayer == LayerMask.NameToLayer("Enemy") ||
                        otherLayer == LayerMask.NameToLayer("EnemyHitbox"));

        // ========== ���� / ǽ�ڣ��޵��˱�ǩ��==========
        if (!isEnemy)
        {
            // ��ȴ��飬����û�����ڵȴ����ŵ��ӳ�Э��
            if (Time.time < lastGroundHitTime + groundHitCooldown || groundSoundCoroutine != null)
                return;

            // �����ӳٲ���Э��
            groundSoundCoroutine = StartCoroutine(PlayGroundSoundsDelayed());
            return;
        }

        // ========== ���е��� ==========
        GameObject targetRoot = other.transform.root.gameObject;
        if (lastHitFrame == Time.frameCount && lastHitTarget == targetRoot) return;

        // 单次攻击伤害锁：每次鼠标按下只能造成一次伤害
        if (!meleeFighter.canDamageThisAttack) return;

        EnemyHealth enemyHealth = other.GetComponentInParent<EnemyHealth>();
        BotHealth botHealth = other.GetComponentInParent<BotHealth>();
        if (enemyHealth == null && botHealth == null) return;

        PlayerSharpness sharpness = GetComponentInParent<PlayerSharpness>();
        EnemyMeatQuality meat = other.GetComponent<EnemyMeatQuality>();
        int meatValue = (meat != null) ? meat.meatValue : 2;
        float finalDamage = damage;

        // �����ж�
        if (sharpness != null && sharpness.IsBouncePossible() && !isChargeAttack)
        {
            bool bounce = false;
            if (meatValue >= 3) bounce = true;
            else if (meatValue == 2) bounce = Random.value < sharpness.SideBounceChance;

            if (bounce)
            {
                if (bounceSound != null && audioSource != null)
                    audioSource.PlayOneShot(bounceSound);

                sharpness.OnAttackHit(true);
                MeeleFighter mf = GetComponentInParent<MeeleFighter>();
                mf?.PlayBounceReaction();
                return;
            }
        }

        if (sharpness != null)
        {
            finalDamage *= sharpness.GetDamageMultiplier();
            sharpness.OnAttackHit(false);
        }

        if (finalDamage <= 0f) return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);

        // ����������Ч���������أ�
        if (audioSource != null)
        {
            AudioClip clip = (meleeFighter != null && meleeFighter.IsCurrentAttackHeavy) ? heavyHitSound : hitEnemySound;
            if (clip != null) audioSource.PlayOneShot(clip);
        }

        // ���� + ����
        int chargeLevel = (meleeFighter != null) ? meleeFighter.CurrentChargeLevel : 0;
        bool isHeavy = meleeFighter != null && meleeFighter.IsCurrentAttackHeavy;
        HitFeedbackManager.Instance?.TriggerHitFeedback(hitPoint, isHeavy, chargeLevel);

        // ── 顿帧分档：蓄力越高卡肉越久，玩家敌人同步冻结（MHW大剑核心手感）──
        float chargeHitStop = GetChargeHitStopDuration(chargeLevel);
        meleeFighter?.TriggerPlayerHitStop(chargeHitStop);

        // ★ 轻重判定（回合结构）：重击标志 或 蓄力≥2 → 重击（打断 Boss）；普通轻击不打断。
        //   不能用伤害数值判定——蓝斩 1.3x 会把普通攻击推过阈值
        bool heavyHit = meleeFighter != null &&
                        (meleeFighter.IsCurrentAttackHeavy || meleeFighter.CurrentChargeLevel >= 2);

        if (botHealth != null)
        {
            botHealth.TakeDamage(finalDamage, hitPoint, chargeHitStop);
        }
        else
        {
            enemyHealth.TakeDamage(finalDamage, hitPoint, chargeHitStop, heavyHit);

            BossPosture posture = targetRoot.GetComponentInChildren<BossPosture>();
            if (posture != null) posture.OnPlayerHit();
        }

        // 本次攻击已造成伤害，锁定不再判定
        if (meleeFighter != null) meleeFighter.canDamageThisAttack = false;

        lastHitFrame = Time.frameCount;
        lastHitTarget = targetRoot;
    }

    // ── 蓄力顿帧分档计算 ──
    private float GetChargeHitStopDuration(int chargeLevel)
    {
        return chargeLevel switch
        {
            >= 2 => hitStopLv2,   // 二蓄 = 0.15s
            >= 1 => hitStopLv1,   // 一蓄 = 0.08s
            _    => 0.05f         // 普攻 = 0.05s
        };
    }

    // �ӳٲ��ŵ�����Ч��Э��
    private IEnumerator PlayGroundSoundsDelayed()
    {
        // �ȴ� 0.3 ��
        yield return new WaitForSeconds(groundHitDelay);

        // ��ȴʱ���¼�ڲ���ǰ����
        lastGroundHitTime = Time.time;

        if (audioSource != null)
        {
            if (groundHitSound != null) audioSource.PlayOneShot(groundHitSound);
            if (earthHitSound != null) audioSource.PlayOneShot(earthHitSound);
        }

        // ���Э�����ã�������һ�δ���
        groundSoundCoroutine = null;
    }
}