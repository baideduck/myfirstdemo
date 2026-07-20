using System.Collections;
using UnityEngine;

public class PlayerWeaponHitbox : MonoBehaviour
{
    [HideInInspector] public float damage;
    [HideInInspector] public bool isChargeAttack = false;
    [SerializeField] private float hitStopDuration = 0.05f;

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
        if (enemyHealth == null) return;

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

        enemyHealth.TakeDamage(finalDamage, hitPoint, hitStopDuration);
        BossPosture posture = targetRoot.GetComponentInChildren<BossPosture>();
        if (posture != null) posture.OnPlayerHit();

        // 本次攻击已造成伤害，锁定不再判定
        if (meleeFighter != null) meleeFighter.canDamageThisAttack = false;

        lastHitFrame = Time.frameCount;
        lastHitTarget = targetRoot;
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

    public void ForceClearHitRecord()
    {
        lastHitFrame = -1;
        lastHitTarget = null;
    }
}