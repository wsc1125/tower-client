using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PlayerInventoryStoreTests
{
    private ItemDatabase database;
    private ItemDefinition sword;
    private ItemDefinition bow;

    [SetUp]
    public void SetUp()
    {
        PlayerInventoryStore.Delete();

        database = ScriptableObject.CreateInstance<ItemDatabase>();
        sword = CreateItem("Sword", EquipmentSlotType.MeleeWeapon);
        bow = CreateItem("Bow", EquipmentSlotType.RangedWeapon);
        SetDatabaseItems(database, sword, bow);
    }

    [TearDown]
    public void TearDown()
    {
        PlayerInventoryStore.Delete();
        Object.DestroyImmediate(database);
        Object.DestroyImmediate(sword);
        Object.DestroyImmediate(bow);
    }

    [Test]
    public void Save_ThenLoad_RestoresBagAndEquipment()
    {
        var source = new Inventory();
        source.TryAdd(sword);
        source.TryAdd(bow);
        source.TryEquipFromBag(1, EquipmentSlotType.RangedWeapon);

        PlayerInventoryStore.Save(source, database);

        var restored = new Inventory();
        Assert.That(PlayerInventoryStore.Apply(PlayerInventoryStore.Load(), restored, database), Is.True);
        Assert.That(restored.GetBagItem(0), Is.SameAs(sword));
        Assert.That(restored.GetEquippedItem(EquipmentSlotType.RangedWeapon), Is.SameAs(bow));
    }

    [Test]
    public void Save_ThenLoad_RestoresCarryBag()
    {
        var source = new Inventory();
        source.TryAddToCarry(sword);
        source.TryAddToCarry(bow);

        PlayerInventoryStore.Save(source, database);

        var restored = new Inventory();
        Assert.That(PlayerInventoryStore.Apply(PlayerInventoryStore.Load(), restored, database), Is.True);
        Assert.That(restored.GetCarryItem(0), Is.SameAs(sword));
        Assert.That(restored.GetCarryItem(1), Is.SameAs(bow));
    }

    [Test]
    public void Save_DoesNotMutateCarryWhenItemIsUnregistered()
    {
        var stray = CreateItem("Stray", EquipmentSlotType.MeleeWeapon);
        var model = new Inventory();
        model.TryAddToCarry(stray);

        LogAssert.Expect(LogType.Warning, new Regex("未登记到 ItemDatabase"));
        PlayerInventoryStore.Save(model, database);

        // 保存不得修改模型：未登记的携带物品必须原样留在携带背包里。
        Assert.That(model.GetCarryItem(0), Is.SameAs(stray));

        Object.DestroyImmediate(stray);
    }

    [Test]
    public void Apply_UnknownId_LeavesThatSlotEmptyAndKeepsOthers()
    {
        var data = PlayerInventoryData.CreateEmpty();
        data.bag[0] = "Missing";
        data.bag[1] = "Sword";

        LogAssert.Expect(LogType.Error, new Regex("无法在 ItemDatabase 中解析"));

        var restored = new Inventory();
        PlayerInventoryStore.Apply(data, restored, database);

        Assert.That(restored.GetBagItem(0), Is.Null);
        Assert.That(restored.GetBagItem(1), Is.SameAs(sword));
    }

    [Test]
    public void Apply_ResizesMismatchedArrays()
    {
        var data = new PlayerInventoryData { bag = new[] { "Sword" }, equipped = new string[0] };

        var restored = new Inventory();
        Assert.That(PlayerInventoryStore.Apply(data, restored, database), Is.True);
        Assert.That(restored.GetBagItem(0), Is.SameAs(sword));
        Assert.That(restored.GetBagItem(Inventory.BagCapacity - 1), Is.Null);
    }

    [Test]
    public void Validate_ExpandsLegacyTwentySlotSave()
    {
        var legacy = new PlayerInventoryData
        {
            bag = new string[20],
            equipped = new string[PlayerInventoryData.EquippedSlotCount],
        };
        legacy.bag[0] = "Sword";
        legacy.bag[19] = "Bow";

        legacy.Validate();

        Assert.That(legacy.bag.Length, Is.EqualTo(Inventory.BagCapacity));
        Assert.That(legacy.bag[0], Is.EqualTo("Sword"));
        Assert.That(legacy.bag[19], Is.EqualTo("Bow"));
        Assert.That(legacy.bag[20], Is.Null);
        Assert.That(legacy.bag[Inventory.BagCapacity - 1], Is.Null);
    }

    [Test]
    public void Apply_IgnoresEquippedSlotThatIsNotUsable()
    {
        var data = PlayerInventoryData.CreateEmpty();
        data.equipped[(int)EquipmentSlotType.Helmet] = "Sword";

        LogAssert.Expect(LogType.Error, new Regex("当前不可用"));

        var restored = new Inventory();
        PlayerInventoryStore.Apply(data, restored, database);

        Assert.That(restored.GetEquippedItem(EquipmentSlotType.Helmet), Is.Null);
    }

    [Test]
    public void Load_CorruptJson_FallsBackToEmptyInventory()
    {
        PlayerPrefs.SetString(PlayerInventoryStore.PrefsKey, "{ this is not json");
        PlayerPrefs.Save();

        LogAssert.Expect(LogType.Error, new Regex("解析失败"));

        var restored = new Inventory();
        restored.TryAdd(sword);
        PlayerInventoryStore.Apply(PlayerInventoryStore.Load(), restored, database);

        Assert.That(restored.GetBagItem(0), Is.Null);
    }

    [Test]
    public void Delete_RemovesSave()
    {
        var model = new Inventory();
        model.TryAdd(sword);
        PlayerInventoryStore.Save(model, database);
        Assert.That(PlayerInventoryStore.HasSave, Is.True);

        PlayerInventoryStore.Delete();

        Assert.That(PlayerInventoryStore.HasSave, Is.False);
    }

    private static ItemDefinition CreateItem(string id, EquipmentSlotType slot)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.name = id;
        var serialized = new SerializedObject(item);
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)slot;
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
