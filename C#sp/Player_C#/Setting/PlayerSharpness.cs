using UnityEngine;

public class PlayerSharpness : MonoBehaviour
{
    [Header("斩味点数")]
    [SerializeField] private int maxSharpness = 100;
    [SerializeField] private int currentSharpness;

    [Header("消耗（点数）")]
    [SerializeField] private int hitCost = 1;
    [SerializeField] private int bounceExtraCost = 2;
    [SerializeField] private int blockCost = 1;

    [Header("等级阈值")]
    [SerializeField] private int blueThreshold = 60;
    [SerializeField] private int greenThreshold = 30;

    [Header("伤害倍率")]
    [SerializeField] private float blueMult = 1.3f;
    [SerializeField] private float greenMult = 1.0f;
    [SerializeField] private float whiteMult = 0.6f;

    [Header("弾刀概率（白色斩味 + 肉质=2）")]
    [SerializeField] private float sideBounceChance = 0.3f;

    public enum SharpnessLevel { Blue, Green, White }

    private void Start()
    {
        currentSharpness = maxSharpness;
    }

    // ===== 消耗接口 =====

    /// <summary>攻击命中（根据是否弹刀决定实际消耗）</summary>
    public void OnAttackHit(bool isBounced)
    {
        int cost = hitCost;
        if (isBounced)
            cost += bounceExtraCost;
        currentSharpness = Mathf.Max(0, currentSharpness - cost);
    }

    /// <summary>普通格挡消耗</summary>
    public void ConsumeBlock()
    {
        currentSharpness = Mathf.Max(0, currentSharpness - blockCost);
    }

    /// <summary>磨刀恢复</summary>
    public void Sharpen()
    {
        currentSharpness = maxSharpness;
    }

    // ===== 查询接口 =====

    public SharpnessLevel CurrentLevel
    {
        get
        {
            if (currentSharpness >= blueThreshold) return SharpnessLevel.Blue;
            if (currentSharpness >= greenThreshold) return SharpnessLevel.Green;
            return SharpnessLevel.White;
        }
    }

    public float GetDamageMultiplier()
    {
        return CurrentLevel switch
        {
            SharpnessLevel.Blue => blueMult,
            SharpnessLevel.Green => greenMult,
            SharpnessLevel.White => whiteMult,
            _ => greenMult
        };
    }

    /// <summary>当前斩味是否可能弹刀</summary>
    public bool IsBouncePossible() => CurrentLevel == SharpnessLevel.White;

    public float SideBounceChance => sideBounceChance;
    public int CurrentSharpnessPoints => currentSharpness;
    public int MaxSharpness => maxSharpness;
}