using UnityEditor;
using UnityEngine;

internal static class ItemDefinitionFactory
{
    public static ItemDefinition Create(Sprite icon, Sprite worldSprite, float worldSize)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("displayName").stringValue = "测试物品";
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.FindProperty("worldSprite").objectReferenceValue = worldSprite;
        serialized.FindProperty("worldSize").floatValue = worldSize;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }
}
