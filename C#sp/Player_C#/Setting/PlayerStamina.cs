using System.Collections;
using UnityEngine;

public class PlayerStamina : MonoBehaviour
{
    [Header("����")]
    [SerializeField] float maxStamina = 100f;
    [SerializeField] float currentStamina;

    [Header("����")]
    [SerializeField] float dodgeCost = 25f;      // ��������
    [SerializeField] float blockCostPerSec = 15f; // ��ÿ������

    [Header("�ظ�")]
    [SerializeField] float regenRate = 30f;       // ÿ��ظ���
    [SerializeField] float regenDelay = 0.8f;     // ���ĺ��ÿ�ʼ�ظ�

    private float lastConsumeTime = -10f;
    private bool isExhausted = false;             // �����ľ��ͷ�
    private MeeleFighter meleeFighter;

    public float CurrentStamina => currentStamina;
    public float MaxStamina => maxStamina;
    public bool HasEnough(float amount) => currentStamina >= amount;



    void Start()
    {
        currentStamina = maxStamina;
        meleeFighter = GetComponent<MeeleFighter>();
    }

    void Update()
    {
        if (isExhausted) return;

        // ���в��Զ��ָ�����
        if (!meleeFighter.IsBlocking && Time.time > lastConsumeTime + regenDelay && currentStamina < maxStamina)
        {
            currentStamina += regenRate * Time.deltaTime;
            currentStamina = Mathf.Min(currentStamina, maxStamina);
        }
    }

    /// <summary>
    /// ���������������Ƿ�ɹ�
    /// </summary>
    public bool Consume(float amount)
    {
        if (isExhausted) return false;
        if (currentStamina < amount) return false;   // �� �������������� false

        currentStamina -= amount;
        lastConsumeTime = Time.time;

        if (currentStamina <= 0)
        {
            currentStamina = 0;
            StartCoroutine(ExhaustRoutine());
        }
        return true;   // ���ĳɹ�
    }

    IEnumerator ExhaustRoutine()
    {
        isExhausted = true;
        meleeFighter?.PlayExhausted();
        yield return new WaitForSeconds(2f);
        isExhausted = false;
        currentStamina = maxStamina * 0.2f;
    }

    /// <summary>
    /// �����������������
    /// </summary>
    public bool TryDodge()
    {
        return Consume(dodgeCost);
    }

    /// <summary>
    /// ÿ֡���������������Ƿ�������ʣ��
    /// </summary>
    public bool ConsumeOverTime(float amountPerSecond)
    {
        float amount = amountPerSecond * Time.deltaTime;
        if (currentStamina < amount)
            return false;
        currentStamina -= amount;
        lastConsumeTime = Time.time;
        return true;
    }

    /// <summary>
    /// ��ÿ�����ģ���MeeleFighter��ʱ���ã�
    /// </summary>
    public bool TryBlockTick()
    {
        return Consume(blockCostPerSec * Time.deltaTime);
    }
}