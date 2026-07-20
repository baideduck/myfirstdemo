using UnityEngine;

public class BossPosture : MonoBehaviour
{
    [Header("����ֵ")]
    [SerializeField] private float maxPosture = 400f;
    [SerializeField] private float currentPosture;

    [Header("����")]
    [SerializeField] private float posturePerHit = 20f;         // ÿ�α���ҹ�����������

    private EnemyController enemyController;

    public float CurrentPosture { get => currentPosture; set => currentPosture = value; }
    public float MaxPosture => maxPosture;

    private void Awake()
    {
        enemyController = GetComponent<EnemyController>();
        currentPosture = maxPosture;
    }

    /// <summary>
    /// ����ҹ�������ʱ����
    /// </summary>
    public void OnPlayerHit()
    {
        currentPosture -= posturePerHit;
        CheckBreak();
    }

    /// <summary>
    /// 完美格挡时扣除架势（扣量 = 该招式的体力基础消耗）
    /// </summary>
    public void OnPerfectBlocked(float drainAmount)
    {
        currentPosture -= drainAmount;
        CheckBreak();
    }

    private void CheckBreak()
    {
        if (currentPosture <= 0f)
        {
            currentPosture = 0f;
            enemyController.OnPostureBroken();
        }
    }

    /// <summary>
    /// ���߽�������������
    /// </summary>
    public void ResetPosture()
    {
        currentPosture = maxPosture;
    }
}