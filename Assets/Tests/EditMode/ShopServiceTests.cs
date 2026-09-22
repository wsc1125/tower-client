using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ShopServiceTests
{
    private ItemDefinition sword;
    private ItemDefinition bow;
    private ItemDefinition helmet;
    private ItemDefinition milk;
    private ItemDatabase database;

    [SetUp]
    public void SetUp()
    {
        sword = CreateItem("Sword", EquipmentSlotType.MeleeWeapon, 120);
        bow = CreateItem("Bow", EquipmentSlotType.RangedWeapon, 90);
        helmet = CreateItem("Helmet", EquipmentSlotType.Helmet, 200);
        milk = CreateItem("Milk", EquipmentSlotType.None, 30);

        database = ScriptableObject.CreateInstance<ItemDatabase>();
        SetDatabaseItems(database, sword, bow, helmet, milk);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(database);
        Object.DestroyImmediate(sword);
        Object.DestroyImmediate(bow);
        Object.DestroyImmediate(helmet);
        Object.DestroyImmediate(milk);
    }

    [Test]
    public void GetCatalog_IncludesWeaponsAndArmor_ExcludesConsumables()
    {
        var catalog = ShopService.GetCatalog(database);

        Assert.That(catalog.Contains(sword), Is.True);
        Assert.That(catalog.Contains(bow), Is.True);
        Assert.That(catalog.Contains(helmet), Is.True);
        Assert.That(catalog.Contains(milk), Is.False);
    }

    [Test]
    public void GetCatalog_NullDatabase_ReturnsEmpty()
    {
        Assert.That(ShopService.GetCatalog(null), Is.Empty);
    }

    [Test]
    public void TryPurchase_Success_AddsItemAndDeductsBalance()
    {
        var model = new Inventory();

        var result = ShopService.TryPurchase(sword, model, 200, out var remaining);

        Assert.That(result, Is.True);
        Assert.That(remaining, Is.EqualTo(80));
        Assert.That(model.GetBagItem(0), Is.SameAs(sword));
    }

    [Test]
    public void TryPurchase_InsufficientBalance_FailsWithoutChanges()
    {
        var model = new Inventory();

        var result = ShopService.TryPurchase(sword, model, 119, out var remaining);

        Assert.That(result, Is.False);
        Assert.That(remaining, Is.EqualTo(119));
        Assert.That(model.GetBagItem(0), Is.Null);
    }

    [Test]
    public void TryPurchase_FullBag_FailsAndKeepsBalance()
    {
        var model = new Inventory();
        var filler = CreateItem("Filler", EquipmentSlotType.MeleeWeapon, 1);
        for (var i = 0; i < Inventory.BagCapacity; i++)
            model.TryAdd(filler);

        var result = ShopService.TryPurchase(sword, model, 999, out var remaining);

        Assert.That(result, Is.False);
        Assert.That(remaining, Is.EqualTo(999));
    }

    [Test]
    public void TryPurchase_UnpricedItem_Fails()
    {
        var unpriced = CreateItem("Unpriced", EquipmentSlotType.MeleeWeapon, 0);
        var model = new Inventory();

        var result = ShopService.TryPurchase(unpriced, model, 999, out var remaining);

        Assert.That(result, Is.False);
        Assert.That(remaining, Is.EqualTo(999));
        Object.DestroyImmediate(unpriced);
    }

    [Test]
    public void TryPurchase_NullItemOrModel_Fails()
    {
        var model = new Inventory();

        Assert.That(ShopService.TryPurchase(null, model, 999, out _), Is.False);
        Assert.That(ShopService.TryPurchase(sword, null, 999, out _), Is.False);
    }

    private static ItemDefinition CreateItem(string id, EquipmentSlotType slot, int buyPrice)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.name = id;
        var serialized = new SerializedObject(item);
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)slot;
        serialized.FindProperty("buyPrice").intValue = buyPrice;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static void SetDatabaseItems(ItemDatabase target, params ItemDefinition[] items)
    {
        var serialized = new SerializedObject(target);
        var list = serialized.FindProperty("items");
        list.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
