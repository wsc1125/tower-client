using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Definition", fileName = "ItemDefinition")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private EquipmentSlotType equipSlotType;

    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public EquipmentSlotType EquipSlotType => equipSlotType;
}
