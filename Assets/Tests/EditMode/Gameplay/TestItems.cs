using UnityEditor;
using UnityEngine;

/// <summary>
/// 测试用物品工厂：直接构造 <see cref="ItemDefinition"/> 并写入私有序列化字段。
/// 数据层测试共用，避免每个测试文件各写一份。
/// </summary>
internal static class TestItems
{
    private static int counter;

    public static ItemDefinition Create(
        EquipmentSlotType slot = EquipmentSlotType.None,
        string displayName = null,
        int value = 0,
        float damageReduction = 0f,
        int durability = 0)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        // Id 取资源名，存档往返依赖它，因此测试物品必须有唯一名字。
        item.name = "TestItem_" + (++counter);
        var serialized = new SerializedObject(item);
        serialized.FindProperty("displayName").stringValue = displayName ?? slot.ToString();
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)slot;
        serialized.FindProperty("value").intValue = value;
        serialized.FindProperty("damageReduction").floatValue = damageReduction;
        serialized.FindProperty("durability").intValue = durability;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    /// <summary>创建一把武器（主手槽类型）。</summary>
    public static ItemDefinition Weapon(string displayName = "武器", int value = 0)
    {
        return Create(EquipmentSlotType.MainHand, displayName, value);
    }

    /// <summary>创建一件防具。</summary>
    public static ItemDefinition Armor(
        EquipmentSlotType slot,
        string displayName = null,
        float damageReduction = 0.2f,
        int durability = 10)
    {
        return Create(slot, displayName, 0, damageReduction, durability);
    }

    /// <summary>把模板包成物品实例（满耐久）。</summary>
    public static ItemInstance Instance(ItemDefinition definition)
    {
        return ItemInstance.Create(definition);
    }

    /// <summary>创建一把武器实例。</summary>
    public static ItemInstance WeaponInstance(string displayName = "武器", int value = 0)
    {
        return ItemInstance.Create(Weapon(displayName, value));
    }

    /// <summary>创建一件防具实例。</summary>
    public static ItemInstance ArmorInstance(
        EquipmentSlotType slot,
        string displayName = null,
        float damageReduction = 0.2f,
        int durability = 10)
    {
        return ItemInstance.Create(Armor(slot, displayName, damageReduction, durability));
    }
}
