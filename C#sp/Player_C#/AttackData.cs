using UnityEngine;

[CreateAssetMenu(menuName = "Combat System/Create a new attack")]
public class AttackData : ScriptableObject
{
    [SerializeField] private string animName;
    [SerializeField] private float impactStartTime;
    [SerializeField] private float impactEndTime;
    [SerializeField] private float damage;

    [Header("Combo Settings")]
    [SerializeField] private string moveID;
    [SerializeField] private string[] allowedPreviousMoves;
    [SerializeField] private AttackInputType requiredInput;
    [SerializeField] private bool requiresForwardInput;
    [SerializeField] private bool requiresDrawn;
    [SerializeField] private bool canUseFromSheathed;
    [Header("Enhance Animation")]

    [SerializeField] private bool hasEnhanceAnim = false;          // 是否启用增强动画
    [SerializeField] private float enhanceTriggerTime = 2f;        // 触发时间（秒）
    [SerializeField] private string enhanceAnimName = "";          // 增强动画名

    [Header("Charge Settings")]
    [SerializeField] private bool isChargeable;               // 是否可蓄力
    [SerializeField] private string chargeStartAnim = "";
    [SerializeField] private string chargeHoldAnim = "";      // 蓄力保持动画（循环）
    [SerializeField] private float[] chargeThresholds;        // 各段蓄力所需时间（秒），如 [0.8, 1.6]
    [SerializeField] private string[] chargeReleaseAnims;     // 对应各段（及0段）的释放动画，长度应比 thresholds 大1
    [SerializeField] private bool isHeavyAttack = false;
    // 公共属性
    public string AnimName => animName;
    public float ImpactStartTime => impactStartTime;
    public float ImpactEndTime => impactEndTime;
    public float Damage => damage;
    public string MoveID => moveID;
    public string[] AllowedPreviousMoves => allowedPreviousMoves;
    public AttackInputType RequiredInput => requiredInput;
    public bool RequiresForwardInput => requiresForwardInput;
    public bool RequiresDrawn => requiresDrawn;
    public bool CanUseFromSheathed => canUseFromSheathed;
    public bool IsChargeable => isChargeable;
    public string ChargeHoldAnim => chargeHoldAnim;
    public float[] ChargeThresholds => chargeThresholds;
    public string[] ChargeReleaseAnims => chargeReleaseAnims;
    public string ChargeStartAnim => chargeStartAnim;
    public bool HasChargeStartAnim => !string.IsNullOrEmpty(chargeStartAnim);

    public bool isFinalMove;
    public bool HasEnhanceAnim => hasEnhanceAnim;
    public float EnhanceTriggerTime => enhanceTriggerTime;
    public string EnhanceAnimName => enhanceAnimName;
    public bool IsHeavyAttack => isHeavyAttack;
    public enum AttackInputType
    {
        Light,
        Heavy
    }
}