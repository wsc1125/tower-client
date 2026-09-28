using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ShopServiceTests
{
    private ItemDefinition sword;
    private ItemDefinition bow;
    private ItemDefinition helmet;
    private ItemDefinition milk;
    private ItemDefinition medKit;
    private ItemDatabase database;

    [SetUp]
    public void SetUp()
    {
        sword = CreateItem("Sword", EquipmentSlotType.MainHand, 120);
        bow = CreateItem("Bow", EquipmentSlotType.OffHand, 90);
        helmet = CreateItem("Helmet", EquipmentSlotType.Helmet, 200);
        // 纯战利品：不占装备槽且未定价，不应上架。
        milk = CreateItem("Milk", EquipmentSlotType.None, 0);
        // 消耗品：不占装备槽但已定价，应上架。
        medKit = CreateItem("MedKit", EquipmentSlotType.None, 240);

        database = ScriptableObject.CreateInstance<ItemDatabase>();
        SetDatabaseItems(database, sword, bow, helmet, milk, medKit);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(database);
        Object.DestroyImmediate(sword);
        Object.DestroyImmediate(bow);
        Object.DestroyImmediate(helmet);
        Object.DestroyImmediate(milk);
        Object.DestroyImmediate(medKit);
    }

    [Test]
    public void GetCatalog_IncludesPricedItems_ExcludesUnpricedLoot()
    {
        var catalog = ShopService.GetCatalog(database);

        Assert.That(catalog.Contains(sword), Is.True);
        Assert.That(catalog.Contains(bow), Is.True);
        Assert.That(catalog.Contains(helmet), Is.True);
        // 消耗品已定价，应上架。
        Assert.That(catalog.Contains(medKit), Is.True);
        // 纯战利品未定价，不上架。
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
        var loadout = new PlayerLoadout();

        var result = ShopService.TryPurchase(sword, loadout, 200, out var remaining);

        Assert.That(result, Is.True);
        Assert.That(remaining, Is.EqualTo(80));
        // 商店卖的是模板，入库时现场创建实例。
        Assert.That(loadout.GetWarehouse(0), Is.Not.Null);
        Assert.That(loadout.GetWarehouse(0).Definition, Is.SameAs(sword));
    }

    [Test]
    public void TryPurchase_InsufficientBalance_FailsWithoutChanges()
    {
        var loadout = new PlayerLoadout();

        var result = ShopService.TryPurchase(sword, loadout, 119, out var remaining);

        Assert.That(result, Is.False);
        Assert.That(remaining, Is.EqualTo(119));
        Assert.That(loadout.GetWarehouse(0), Is.Null);
    }

    [Test]
    public void TryPurchase_FullWarehouse_FailsAndKeepsBalance()
    {
        var loadout = new PlayerLoadout();
        var filler = ItemInstance.Create(CreateItem("Filler", EquipmentSlotType.MainHand, 1));
        for (var i = 0; i < PlayerLoadout.WarehouseCapacity; i++)
            loadout.TryAddToWarehouse(filler);

        var result = ShopService.TryPurchase(sword, loadout, 999, out var remaining);

        Assert.That(result, Is.False);
        Assert.That(remaining, Is.EqualTo(999));
    }

    [Test]
    public void TryPurchase_UnpricedItem_Fails()
    {
        var unpriced = CreateItem("Unpriced", EquipmentSlotType.MainHand, 0);
        var loadout = new PlayerLoadout();

        var result = ShopService.TryPurchase(unpriced, loadout, 999, out var remaining);

        Assert.That(result, Is.False);
        Assert.That(remaining, Is.EqualTo(999));
        Object.DestroyImmediate(unpriced);
    }

    [Test]
    public void TryPurchase_NullItemOrLoadout_Fails()
    {
        var loadout = new PlayerLoadout();

        Assert.That(ShopService.TryPurchase(null, loadout, 999, out _), Is.False);
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
