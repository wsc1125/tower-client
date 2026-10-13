using UnityEditor;
using UnityEngine;

internal static class ItemDefinitionFactory
{
    private static int counter;

    public static ItemDefinition Create(
        Sprite icon,
        Sprite worldSprite,
        float worldSize,
        EquipmentSlotType slot = EquipmentSlotType.None)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        // ItemDefinition.Id 取资源名，联机拾取按 ID 反查定义；不命名会导致 Id 为空串、无法解析。
        item.name = "TestItem_" + (++counter);
        var serialized = new SerializedObject(item);
        serialized.FindProperty("displayName").stringValue = "测试物品";
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.FindProperty("worldSprite").objectReferenceValue = worldSprite;
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)slot;
        serialized.FindProperty("worldSize").floatValue = worldSize;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }
}
