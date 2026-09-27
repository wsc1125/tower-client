using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="CollectionCatalog"/> 的图鉴条目计算：自动收录材料、排除武器防具与排除名单。
/// </summary>
public sealed class CollectionCatalogTests
{
    private ItemDatabase database;
    private CollectionCatalog catalog;

    [SetUp]
    public void SetUp()
    {
        database = ScriptableObject.CreateInstance<ItemDatabase>();
        catalog = ScriptableObject.CreateInstance<CollectionCatalog>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(database);
        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void Collectibles_ExcludesWeaponsAndArmor()
    {
        var material = TestItems.Create(EquipmentSlotType.None, "材料");
        var weapon = TestItems.Weapon("武器");
        var armor = TestItems.Armor(EquipmentSlotType.Helmet, "头盔");
        SetDatabaseItems(material, weapon, armor);

        var result = catalog.Collectibles(database);

        Assert.That(result, Has.Count.EqualTo(1), "只有材料应进图鉴");
        Assert.That(result[0], Is.SameAs(material));
    }

    [Test]
    public void Collectibles_ExcludesExplicitlyExcludedItems()
    {
        var material = TestItems.Create(EquipmentSlotType.None, "材料");
        var questItem = TestItems.Create(EquipmentSlotType.None, "任务道具");
        SetDatabaseItems(material, questItem);
        SetExcluded(questItem);

        var result = catalog.Collectibles(database);

        Assert.That(result, Has.Count.EqualTo(1), "排除名单里的物品不应进图鉴");
        Assert.That(result[0], Is.SameAs(material));
    }

    [Test]
    public void Collectibles_WithNullDatabase_ReturnsEmpty()
    {
        Assert.That(catalog.Collectibles(null), Is.Empty);
    }

    [Test]
    public void RewardFor_ReturnsConfiguredAmountPerRarity()
    {
        Assert.That(catalog.RewardFor(ItemRarity.Common), Is.EqualTo(30));
        Assert.That(catalog.RewardFor(ItemRarity.Fine), Is.EqualTo(60));
        Assert.That(catalog.RewardFor(ItemRarity.Rare), Is.EqualTo(150));
        Assert.That(catalog.RewardFor(ItemRarity.Epic), Is.EqualTo(400));
        Assert.That(catalog.RewardFor(ItemRarity.Legendary), Is.EqualTo(1000));
    }

    [Test]
    public void AllCollectedReward_DefaultsTo3000()
    {
        Assert.That(catalog.AllCollectedReward, Is.EqualTo(3000));
    }

    private void SetDatabaseItems(params ItemDefinition[] items)
    {
        var serialized = new SerializedObject(database);
        var array = serialized.FindProperty("items");
        array.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private void SetExcluded(params ItemDefinition[] items)
    {
        var serialized = new SerializedObject(catalog);
        var array = serialized.FindProperty("excluded");
        array.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
