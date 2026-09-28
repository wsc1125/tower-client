using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// <see cref="PlayerSaveStore"/> 的存档往返、损坏回退与旧格式迁移。
/// 新格式用「槽位键 → 物品 ID」字典，因此这里也验证槽位名不依赖枚举下标。
/// </summary>
public sealed class PlayerSaveStoreTests
{
    private ItemDatabase database;

    [SetUp]
    public void SetUp()
    {
        PlayerSaveStore.Delete();
        database = ScriptableObject.CreateInstance<ItemDatabase>();
    }

    [TearDown]
    public void TearDown()
    {
        PlayerSaveStore.Delete();
        Object.DestroyImmediate(database);
    }

    [Test]
    public void SaveAndLoad_RoundTripsWarehouseAndEquipment()
    {
        var weapon = TestItems.WeaponInstance("步枪");
        var helmet = TestItems.ArmorInstance(EquipmentSlotType.Helmet, "头盔");
        Register(weapon.Definition, helmet.Definition);

        var source = new PlayerLoadout();
        source.SetWarehouse(0, weapon);
        source.SetWarehouse(40, helmet);
        source.Equipment.Set(EquipmentSlotType.MainHand, weapon);

        PlayerSaveStore.Save(source);

        var restored = new PlayerLoadout();
        Assert.That(PlayerSaveStore.Apply(PlayerSaveStore.Load(), restored, database), Is.True);

        // 存档往返后是「同类型、同实例 ID、同耐久」的新对象，不是同一个引用。
        Assert.That(restored.GetWarehouse(0).Definition, Is.SameAs(weapon.Definition));
        Assert.That(restored.GetWarehouse(0).InstanceId, Is.EqualTo(weapon.InstanceId));
        Assert.That(restored.GetWarehouse(40).Definition, Is.SameAs(helmet.Definition));
        Assert.That(restored.Equipment.Get(EquipmentSlotType.MainHand).Definition, Is.SameAs(weapon.Definition));
    }

    [Test]
    public void SaveAndLoad_PreservesDurability()
    {
        var helmet = TestItems.ArmorInstance(EquipmentSlotType.Helmet, "头盔", durability: 30);
        helmet.RestoreDurability(7);
        Register(helmet.Definition);

        var source = new PlayerLoadout();
        source.Equipment.Set(EquipmentSlotType.Helmet, helmet);
        PlayerSaveStore.Save(source);

        var restored = new PlayerLoadout();
        PlayerSaveStore.Apply(PlayerSaveStore.Load(), restored, database);

        Assert.That(restored.Equipment.Get(EquipmentSlotType.Helmet).CurrentDurability, Is.EqualTo(7));
    }

    [Test]
    public void SaveAndLoad_DoesNotPersistRaidBag()
    {
        var weapon = TestItems.WeaponInstance();
        Register(weapon.Definition);

        var source = new PlayerLoadout();
        source.SetWarehouse(0, weapon);
        PlayerSaveStore.Save(source);

        // 局内携带不属于局外存档，重新读档后不应出现在仓库里。
        var restored = new PlayerLoadout();
        PlayerSaveStore.Apply(PlayerSaveStore.Load(), restored, database);

        Assert.That(restored.WarehouseCount(), Is.EqualTo(1));
    }

    [Test]
    public void Apply_UnknownItemId_LeavesCellEmpty()
    {
        var data = PlayerSaveData.CreateEmpty();
        data.warehouse.Add(new PlayerSaveData.SlotEntry("0", "不存在的物品"));

        var restored = new PlayerLoadout();
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("无法在 ItemDatabase 中解析"));
        PlayerSaveStore.Apply(data, restored, database);

        Assert.That(restored.GetWarehouse(0), Is.Null);
    }

    [Test]
    public void Apply_UnknownEquipmentSlotName_IsIgnored()
    {
        var weapon = TestItems.WeaponInstance();
        Register(weapon.Definition);

        var data = PlayerSaveData.CreateEmpty();
        data.equipment.Add(new PlayerSaveData.SlotEntry("NotASlot", weapon.Definition.Id));

        var restored = new PlayerLoadout();
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("无法识别"));
        PlayerSaveStore.Apply(data, restored, database);

        Assert.That(restored.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
    }

    [Test]
    public void Apply_UnparsableWarehouseKey_IsIgnored()
    {
        var weapon = TestItems.WeaponInstance();
        Register(weapon.Definition);

        var data = PlayerSaveData.CreateEmpty();
        data.warehouse.Add(new PlayerSaveData.SlotEntry("abc", weapon.Definition.Id));

        var restored = new PlayerLoadout();
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("无法解析"));
        PlayerSaveStore.Apply(data, restored, database);

        Assert.That(restored.WarehouseCount(), Is.Zero);
    }

    [Test]
    public void Apply_NullLists_DoesNotThrow()
    {
        var data = new PlayerSaveData { warehouse = null, carry = null, equipment = null };

        var restored = new PlayerLoadout();
        Assert.DoesNotThrow(() => PlayerSaveStore.Apply(data, restored, database));
    }

    [Test]
    public void Load_CorruptedJson_FallsBackToEmpty()
    {
        PlayerPrefs.SetString(PlayerSaveStore.PrefsKey, "{ this is not json");
        PlayerPrefs.Save();

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("解析失败"));
        var data = PlayerSaveStore.Load();

        Assert.That(data, Is.Not.Null);
        Assert.That(data.warehouse, Is.Empty);
    }

    [Test]
    public void HasSave_ReflectsSaveAndDelete()
    {
        Assert.That(PlayerSaveStore.HasSave, Is.False);

        PlayerSaveStore.Save(new PlayerLoadout());
        Assert.That(PlayerSaveStore.HasSave, Is.True);

        PlayerSaveStore.Delete();
        Assert.That(PlayerSaveStore.HasSave, Is.False);
    }

    [Test]
    public void Load_MigratesLegacyArraySave()
    {
        var weapon = TestItems.WeaponInstance("旧枪");
        Register(weapon.Definition);

        // 旧格式：定长数组 + 下标映射装备槽（下标 1 = MainHand）。
        var legacy = new LegacySave
        {
            version = 2,
            bag = new string[72],
            carry = new string[20],
            equipped = new string[6],
        };
        legacy.bag[0] = weapon.Definition.Id;
        legacy.equipped[1] = weapon.Definition.Id;
        PlayerPrefs.SetString("PlayerInventory", JsonUtility.ToJson(legacy));
        PlayerPrefs.Save();

        var data = PlayerSaveStore.Load();
        var restored = new PlayerLoadout();
        PlayerSaveStore.Apply(data, restored, database);

        // 旧存档没有实例 ID，读回时现场生成，因此只断言「类型一致」。
        Assert.That(restored.GetWarehouse(0).Definition, Is.SameAs(weapon.Definition));
        Assert.That(restored.Equipment.Get(EquipmentSlotType.MainHand).Definition, Is.SameAs(weapon.Definition));
    }

    [Test]
    public void Load_MigratesLegacyCarryIntoWarehouse()
    {
        var weapon = TestItems.WeaponInstance("旧携带");
        Register(weapon.Definition);

        var legacy = new LegacySave
        {
            version = 2,
            bag = new string[72],
            carry = new string[20],
            equipped = new string[6],
        };
        legacy.carry[0] = weapon.Definition.Id;
        PlayerPrefs.SetString("PlayerInventory", JsonUtility.ToJson(legacy));
        PlayerPrefs.Save();

        var restored = new PlayerLoadout();
        PlayerSaveStore.Apply(PlayerSaveStore.Load(), restored, database);

        // 旧「出征携带」语义上属于局内，迁移时并入仓库避免丢失。
        Assert.That(restored.WarehouseCount(), Is.EqualTo(1));
    }

    private void Register(params ItemDefinition[] items)
    {
        var serialized = new SerializedObject(database);
        var array = serialized.FindProperty("items");
        array.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>旧版存档结构，仅用于迁移测试。</summary>
    private sealed class LegacySave
    {
        public int version;
        public string[] bag;
        public string[] carry;
        public string[] equipped;
    }
}
