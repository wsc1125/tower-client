using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="GameSession"/> 的会话生命周期：初始化、出征、撤离、死亡。
/// 这是「武器带进局内」与「战利品带出局外」两条链路的顶层编排。
/// </summary>
public sealed class GameSessionTests
{
    private ItemDatabase database;

    [SetUp]
    public void SetUp()
    {
        PlayerSaveStore.Delete();
        GameSession.ResetForTests();
        database = ScriptableObject.CreateInstance<ItemDatabase>();
    }

    [TearDown]
    public void TearDown()
    {
        PlayerSaveStore.Delete();
        GameSession.ResetForTests();
        Object.DestroyImmediate(database);
    }

    [Test]
    public void EnsureInitialized_IsIdempotent()
    {
        GameSession.EnsureInitialized(database);
        var loadout = GameSession.Loadout;

        GameSession.EnsureInitialized(database);

        Assert.That(GameSession.Loadout, Is.SameAs(loadout));
    }

    [Test]
    public void EnsureInitialized_AppliesInitialEquipmentOnFirstRun()
    {
        var rifle = TestItems.WeaponInstance("步枪");

        GameSession.EnsureInitialized(database, new[] { rifle.Definition });

        Assert.That(GameSession.Loadout.Equipment.Get(EquipmentSlotType.MainHand).Definition, Is.SameAs(rifle.Definition));
    }

    [Test]
    public void EnsureInitialized_DoesNotReapplyInitialEquipmentWhenSaveExists()
    {
        var rifle = TestItems.WeaponInstance("步枪");
        GameSession.EnsureInitialized(database, new[] { rifle.Definition });
        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, null);
        GameSession.Persist();

        // 模拟重进游戏：会话重置，但存档还在。
        GameSession.ResetForTests();
        GameSession.EnsureInitialized(database, new[] { rifle.Definition });

        Assert.That(GameSession.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
    }

    [Test]
    public void BeginRaid_KeepsEquipmentSharedWithLoadout()
    {
        var rifle = TestItems.WeaponInstance("步枪");
        GameSession.EnsureInitialized(database);
        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);

        GameSession.BeginRaid();

        // 装备槽局内外是同一份：出征不复制、不清空，局内读到的就是局外那件。
        Assert.That(GameSession.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(rifle));
    }

    [Test]
    public void ExtractRaid_MovesLootIntoWarehouse()
    {
        var loot = TestItems.WeaponInstance("战利品");
        GameSession.EnsureInitialized(database);
        GameSession.BeginRaid();
        GameSession.Raid.TryAddToCarry(loot);
        GameSession.Raid.RecordLoot(loot);

        var overflow = GameSession.ExtractRaid();

        Assert.That(overflow, Is.Zero);
        Assert.That(GameSession.Loadout.GetWarehouse(0), Is.SameAs(loot));
        Assert.That(GameSession.Raid.CarryCount(), Is.Zero);
    }

    [Test]
    public void ExtractRaid_PersistsToSave()
    {
        var loot = TestItems.WeaponInstance("战利品");
        Register(loot.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.BeginRaid();
        GameSession.Raid.TryAddToCarry(loot);

        GameSession.ExtractRaid();

        // 重新读档应能还原带出的战利品。
        var restored = new PlayerLoadout();
        PlayerSaveStore.Apply(PlayerSaveStore.Load(), restored, database);
        Assert.That(restored.WarehouseCount(), Is.EqualTo(1));
    }

    [Test]
    public void AbandonRaid_ClearsCarryEquipmentAndLootButKeepsWarehouse()
    {
        var rifle = TestItems.WeaponInstance("步枪");
        var loot = TestItems.WeaponInstance("战利品");
        GameSession.EnsureInitialized(database);
        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);
        GameSession.Loadout.TryAddToWarehouse(TestItems.WeaponInstance("仓库里的"));

        GameSession.BeginRaid();
        GameSession.Raid.TryAddToCarry(loot);
        GameSession.Raid.RecordLoot(loot);

        GameSession.AbandonRaid();

        // 局内携带与战利品丢失。
        Assert.That(GameSession.Raid.CarryCount(), Is.Zero);
        Assert.That(GameSession.Raid.Loot, Is.Empty);

        // 穿在身上的装备也丢失 —— 装备槽与局外是同一份，死亡一并清空。
        Assert.That(GameSession.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);

        // 仓库是安全区，不受死亡影响。
        Assert.That(GameSession.Loadout.WarehouseCount(), Is.EqualTo(1));
    }

    [Test]
    public void AbandonRaid_DoesNotPersistRaidContents()
    {
        var loot = TestItems.WeaponInstance("战利品");
        GameSession.EnsureInitialized(database);
        GameSession.BeginRaid();
        GameSession.Raid.TryAddToCarry(loot);

        GameSession.AbandonRaid();

        var restored = new PlayerLoadout();
        PlayerSaveStore.Apply(PlayerSaveStore.Load(), restored, database);
        Assert.That(restored.WarehouseCount(), Is.Zero);
    }

    [Test]
    public void LoadoutChanged_FiresOnWarehouseMutation()
    {
        GameSession.EnsureInitialized(database);
        var fired = 0;
        GameSession.LoadoutChanged += () => fired++;

        GameSession.Loadout.TryAddToWarehouse(TestItems.WeaponInstance());

        Assert.That(fired, Is.EqualTo(1));
    }

    [Test]
    public void RaidChanged_FiresOnCarryMutation()
    {
        GameSession.EnsureInitialized(database);
        var fired = 0;
        GameSession.RaidChanged += () => fired++;

        GameSession.Raid.TryAddToCarry(TestItems.WeaponInstance());

        Assert.That(fired, Is.EqualTo(1));
    }

    [Test]
    public void ResetAll_ClearsEverythingAndDeletesSave()
    {
        GameSession.EnsureInitialized(database);
        GameSession.Loadout.TryAddToWarehouse(TestItems.WeaponInstance());
        GameSession.Raid.TryAddToCarry(TestItems.WeaponInstance());

        GameSession.ResetAll();

        Assert.That(GameSession.Loadout.WarehouseCount(), Is.Zero);
        Assert.That(GameSession.Raid.CarryCount(), Is.Zero);
        Assert.That(PlayerSaveStore.HasSave, Is.False);
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
}
