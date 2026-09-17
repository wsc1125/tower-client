using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Definition", fileName = "ItemDefinition")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private Sprite worldSprite;
    [SerializeField] private EquipmentSlotType equipSlotType;
    [SerializeField] private int value;
    [SerializeField] private float worldSize = 0.4f;

    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public Sprite WorldSprite => worldSprite;
    public EquipmentSlotType EquipSlotType => equipSlotType;

    /// 价值。本轮仅用于掉落列表展示，不做累加与消费。
    public int Value => value;

    /// 地面掉落物「贴图最大边」的目标世界长度。只决定体量，不改变贴图比例。
    public float WorldSize => worldSize;
}
