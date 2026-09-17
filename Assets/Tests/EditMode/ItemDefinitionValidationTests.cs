using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ItemDefinitionValidationTests
{
    private static readonly string[] ItemPaths =
    {
        "Assets/Inventory/Items/Rifle.asset",
        "Assets/Inventory/Items/TrainingSword.asset",
        "Assets/Inventory/Items/TrainingBow.asset",
        "Assets/Inventory/Items/WildDogMilk.asset",
    };

    [Test]
    public void NewInstance_UsesExpectedDefaults()
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();

        Assert.That(item.Value, Is.Zero);
        Assert.That(item.WorldSize, Is.EqualTo(0.4f).Within(0.0001f));

        Object.DestroyImmediate(item);
    }

    [Test]
    public void EveryItemAsset_HasNameAndPositiveWorldSize()
    {
        foreach (var path in ItemPaths)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

            Assert.That(item, Is.Not.Null, $"{path} 加载失败");
            Assert.That(item.DisplayName, Is.Not.Empty, $"{path} 的 displayName 为空");
            Assert.That(item.Icon, Is.Not.Null, $"{path} 缺少 icon");
            Assert.That(item.WorldSize, Is.GreaterThan(0f), $"{path} 的 worldSize 必须为正数");
        }
    }

    [Test]
    public void EveryItemAsset_HasValueAssigned()
    {
        var sword = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/TrainingSword.asset");
        var bow = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/TrainingBow.asset");
        var rifle = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/Rifle.asset");
        var milk = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/WildDogMilk.asset");

        Assert.That(sword.Value, Is.EqualTo(60));
        Assert.That(bow.Value, Is.EqualTo(45));
        Assert.That(rifle.Value, Is.EqualTo(120));
        Assert.That(milk.Value, Is.EqualTo(30));
    }
}
