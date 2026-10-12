using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Definition", fileName = "ItemDefinition")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private Sprite worldSprite;
    [SerializeField] private EquipmentSlotType equipSlotType;
    [SerializeField] private ItemRarity rarity;
    [SerializeField] private int value;
    [SerializeField] private int buyPrice;
    [SerializeField] private float worldSize = 0.4f;

    [Header("武器表现（仅远程武器使用）")]
    [SerializeField] private float damage = 10f;
    [SerializeField] private float shotsPerSecond = 6f;
    [SerializeField] private float bulletSpeed = 14f;

    [Tooltip("每次开火生成的弹丸数量。1 为单发，霰弹枪等可设为多发。")]
    [SerializeField] private int projectilesPerShot = 1;

    [Tooltip("多发时的总散射角度（度）。弹丸在该角度内均匀铺开；单发时无意义。")]
    [SerializeField] private float spreadAngle;

    [SerializeField] private Sprite bulletSprite;
    [SerializeField] private Sprite[] flashFrames;
    [SerializeField] private Sprite impactSprite;

    [Header("防具属性（仅防具槽使用）")]
    [Tooltip("穿戴后提供的减伤率（0~1）。0.15 表示减免 15% 伤害。0 表示不提供减伤。")]
    [Range(0f, 1f)]
    [SerializeField] private float damageReduction;

    [Tooltip("耐久上限。每次受到攻击扣 1 点，归零后该件护甲不再提供减伤。0 表示不提供护甲。")]
    [SerializeField] private int durability;

    [Header("消耗品效果（仅 equipSlotType = None 使用）")]
    [Tooltip("使用后恢复的血量。0 表示不补血。")]
    [SerializeField] private float healAmount;

    [Tooltip("使用后修复的护甲耐久点数。0 表示不修甲。")]
    [SerializeField] private int repairAmount;

    /// <summary>测试用：注入世界贴图。</summary>
    public void SetWorldSpriteForTests(Sprite sprite)
    {
        worldSprite = sprite;
    }

    /// <summary>存档使用的稳定 ID：取 ScriptableObject 的资源名（asset 文件名）。</summary>
    public string Id => name;

    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public Sprite WorldSprite => worldSprite;
    public EquipmentSlotType EquipSlotType => equipSlotType;

    /// <summary>品质，仅用于展示（格子底板描边色等）。</summary>
    public ItemRarity Rarity => rarity;

    /// 价值。本轮仅用于掉落列表展示，不做累加与消费。
    public int Value => value;

    /// 商店购买价。小于等于 0 表示不可购买（未定价）。
    public int BuyPrice => buyPrice;

    /// 地面掉落物「贴图最大边」的目标世界长度。只决定体量，不改变贴图比例。
    public float WorldSize => worldSize;

    /// 单发伤害。仅远程武器有意义。
    public float Damage => damage;

    /// 每秒射速。
    public float ShotsPerSecond => shotsPerSecond;

    /// <summary>弹丸初速（世界单位/秒）。
    public float BulletSpeed => bulletSpeed;

    /// <summary>每次开火生成的弹丸数量，至少为 1。</summary>
    public int ProjectilesPerShot => projectilesPerShot > 1 ? projectilesPerShot : 1;

    /// <summary>多发时的总散射角度（度）。单发或未配置时为 0。</summary>
    public float SpreadAngle => spreadAngle > 0f ? spreadAngle : 0f;

    /// 弹丸贴图。为空时回退到现有步枪弹丸。
    public Sprite BulletSprite => bulletSprite;

    /// 枪口火光帧序列。为空时回退到现有步枪火光。
    public Sprite[] FlashFrames => flashFrames;

    /// 命中特效贴图。为空时回退到现有步枪命中特效。
    public Sprite ImpactSprite => impactSprite;

    /// <summary>穿戴后提供的减伤率（0~1）。仅防具槽有意义，非防具为 0。</summary>
    public float DamageReduction => Mathf.Clamp01(damageReduction);

    /// <summary>耐久上限。仅防具槽有意义，非防具为 0。</summary>
    public int Durability => durability > 0 ? durability : 0;

    /// <summary>使用后恢复的血量。非消耗品为 0。</summary>
    public float HealAmount => healAmount > 0f ? healAmount : 0f;

    /// <summary>使用后修复的护甲耐久点数。非消耗品为 0。</summary>
    public int RepairAmount => repairAmount > 0 ? repairAmount : 0;

    /// <summary>
    /// 是否是可用道具：不占装备槽，且至少配了一种效果。
    /// 用「效果字段」而非「枚举类型」判定 —— 一件道具可以同时补血与修甲，
    /// 且不会出现「标了消耗品却没配效果」的空转条目。
    /// </summary>
    public bool IsConsumable => equipSlotType == EquipmentSlotType.None
                             && (HealAmount > 0f || RepairAmount > 0);

    /// <summary>是否是补血类道具（点「使用」按钮生效）。</summary>
    public bool IsHealing => IsConsumable && HealAmount > 0f;

    /// <summary>是否是修甲类道具（拖到防具槽上生效）。</summary>
    public bool IsRepairing => IsConsumable && RepairAmount > 0;
}
