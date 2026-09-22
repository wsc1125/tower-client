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

    /// 存档使用的稳定 ID：取 ScriptableObject 的资源名（asset 文件名）。
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
}
