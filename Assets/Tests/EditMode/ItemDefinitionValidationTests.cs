using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ItemDefinitionValidationTests
{
    private static readonly string[] ItemPaths =
    {
        "Assets/Inventory/Items/Rifle.asset",
        "Assets/Inventory/Items/WildDogMilk.asset",
        "Assets/Inventory/Items/Scavenger_Helmet.asset",
        "Assets/Inventory/Items/Scavenger_BodyArmor.asset",
        "Assets/Inventory/Items/Scavenger_Boots.asset",
        "Assets/Inventory/Items/Patrol_Helmet.asset",
        "Assets/Inventory/Items/Patrol_BodyArmor.asset",
        "Assets/Inventory/Items/Patrol_Boots.asset",
        "Assets/Inventory/Items/Phase_Helmet.asset",
        "Assets/Inventory/Items/Phase_BodyArmor.asset",
        "Assets/Inventory/Items/Phase_Boots.asset",
        "Assets/Inventory/Items/StarCore_Helmet.asset",
        "Assets/Inventory/Items/StarCore_BodyArmor.asset",
        "Assets/Inventory/Items/StarCore_Boots.asset",
    };

    /// <summary>12 件护甲：资源名 → (槽位, 减伤率, 耐久)。</summary>
    private static readonly (string Name, EquipmentSlotType Slot, float Reduction, int Durability)[] ArmorItems =
    {
        ("Scavenger_Helmet", EquipmentSlotType.Helmet, 0.15f, 30),
        ("Scavenger_BodyArmor", EquipmentSlotType.BodyArmor, 0.15f, 30),
        ("Scavenger_Boots", EquipmentSlotType.Boots, 0.15f, 30),
        ("Patrol_Helmet", EquipmentSlotType.Helmet, 0.25f, 60),
        ("Patrol_BodyArmor", EquipmentSlotType.BodyArmor, 0.25f, 60),
        ("Patrol_Boots", EquipmentSlotType.Boots, 0.25f, 60),
        ("Phase_Helmet", EquipmentSlotType.Helmet, 0.40f, 120),
        ("Phase_BodyArmor", EquipmentSlotType.BodyArmor, 0.40f, 120),
        ("Phase_Boots", EquipmentSlotType.Boots, 0.40f, 120),
        ("StarCore_Helmet", EquipmentSlotType.Helmet, 0.60f, 200),
        ("StarCore_BodyArmor", EquipmentSlotType.BodyArmor, 0.60f, 200),
        ("StarCore_Boots", EquipmentSlotType.Boots, 0.60f, 200),
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
        var rifle = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/Rifle.asset");
        var milk = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/WildDogMilk.asset");

        Assert.That(rifle.Value, Is.EqualTo(120));
        Assert.That(milk.Value, Is.EqualTo(30));
    }

    [Test]
    public void EveryArmorAsset_HasMatchingSlotReductionAndDurability()
    {
        foreach (var (name, slot, reduction, durability) in ArmorItems)
        {
            var path = $"Assets/Inventory/Items/{name}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

            Assert.That(item, Is.Not.Null, $"{path} 加载失败");
            Assert.That(item.EquipSlotType, Is.EqualTo(slot), $"{path} 的槽位不符");
            Assert.That(item.DamageReduction, Is.EqualTo(reduction).Within(0.0001f), $"{path} 的减伤率不符");
            Assert.That(item.Durability, Is.EqualTo(durability), $"{path} 的耐久不符");
            Assert.That(item.WorldSprite, Is.Not.Null, $"{path} 缺少 worldSprite");
        }
    }

    [Test]
    public void EveryArmorAsset_IsRegisteredInDatabase()
    {
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/Inventory/Items/ItemDatabase.asset");
        Assert.That(database, Is.Not.Null, "ItemDatabase 加载失败");

        foreach (var (name, _, _, _) in ArmorItems)
        {
            Assert.That(database.Find(name), Is.Not.Null, $"ItemDatabase 未登记 {name}");
        }
    }
}
