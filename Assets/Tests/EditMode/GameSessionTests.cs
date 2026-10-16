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
        PlayerSaveStore.Delete("account-alpha");
        PlayerSaveStore.Delete("account-beta");
        GameSession.ResetForTests();
        database = ScriptableObject.CreateInstance<ItemDatabase>();
    }

    [TearDown]
    public void TearDown()
    {
        PlayerSaveStore.Delete();
        PlayerSaveStore.Delete("account-alpha");
        PlayerSaveStore.Delete("account-beta");
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
    public void BeginRaid_KeepsEquipmentPreparedWhileLoggedIn()
    {
        // 回归：登录后身份绑定到账号，主菜单配装写进账号上下文。
        // 出发时若把身份切回 local，局内读到的就是空装备 —— 表现为「装备带不进局内」。
        var rifle = TestItems.WeaponInstance("步枪");
        Register(rifle.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);

        GameSession.BeginRaid();

        Assert.That(GameSession.LocalPlayer.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(rifle));
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
    public void LoadoutChanged_FollowsBoundLocalPlayerOnly()
    {
        GameSession.SetLocalPlayerId("member");
        var fired = 0;
        GameSession.LoadoutChanged += () => fired++;

        GameSession.GetPlayer("host").Loadout.TryAddToWarehouse(TestItems.WeaponInstance());
        GameSession.LocalPlayer.Loadout.TryAddToWarehouse(TestItems.WeaponInstance());

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
    public void RaidChanged_FollowsBoundLocalPlayerOnly()
    {
        GameSession.SetLocalPlayerId("member");
        var fired = 0;
        GameSession.RaidChanged += () => fired++;

        GameSession.GetPlayer("host").Raid.TryAddToCarry(TestItems.WeaponInstance());
        GameSession.LocalPlayer.Raid.TryAddToCarry(TestItems.WeaponInstance());

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

    [Test]
    public void AccountSwitch_RestoresIndependentSaveWithoutChangingSinglePlayerSave()
    {
        var solo = TestItems.WeaponInstance("solo");
        var alpha = TestItems.WeaponInstance("alpha");
        Register(solo.Definition, alpha.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.Loadout.TryAddToWarehouse(solo);

        GameSession.SetLocalPlayerId("account-alpha");
        Assert.That(GameSession.LocalPlayer.Loadout.WarehouseCount(), Is.Zero);
        GameSession.LocalPlayer.Loadout.TryAddToWarehouse(alpha);
        GameSession.SetLocalPlayerId("account-beta");
        Assert.That(GameSession.LocalPlayer.Loadout.WarehouseCount(), Is.Zero);

        GameSession.ResetForTests();
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        Assert.That(GameSession.LocalPlayer.Loadout.GetWarehouse(0).Definition, Is.SameAs(alpha.Definition));
        GameSession.SetLocalPlayerId(null);
        Assert.That(GameSession.Loadout.GetWarehouse(0).Definition, Is.SameAs(solo.Definition));
    }

    [Test]
    public void SwitchingBetweenSingleAndAccountPreservesPreparedCarryAndEquipment()
    {
        var solo = TestItems.WeaponInstance("solo");
        var account = TestItems.WeaponInstance("account");
        Register(solo.Definition, account.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, solo);
        GameSession.Raid.TryAddToCarry(solo);

        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, account);
        GameSession.LocalPlayer.Raid.TryAddToCarry(account);
        GameSession.BeginRaid();
        Assert.That(GameSession.LocalPlayer.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(account));
        Assert.That(GameSession.LocalPlayer.Raid.GetCarry(0), Is.SameAs(account));

        GameSession.SetLocalPlayerId(null);
        GameSession.BeginRaid();
        Assert.That(GameSession.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(solo));
        Assert.That(GameSession.Raid.GetCarry(0), Is.SameAs(solo));
        GameSession.SetLocalPlayerId("account-alpha");
        Assert.That(GameSession.LocalPlayer.Raid.GetCarry(0), Is.SameAs(account));
    }

    [Test]
    public void AccountFirstLoadBeforeEnsureInitialized_LoadsWhenDatabaseArrives()
    {
        var weapon = TestItems.WeaponInstance("saved");
        Register(weapon.Definition);
        var saved = new PlayerLoadout();
        saved.TryAddToWarehouse(weapon);
        PlayerSaveStore.Save(saved, "account-alpha");

        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.EnsureInitialized(database);

        Assert.That(GameSession.LocalPlayer.Loadout.GetWarehouse(0).Definition, Is.SameAs(weapon.Definition));
    }

    [Test]
    public void AccountTeamWipe_ClearsOnlyCurrentEquipmentAndRaidButKeepsWarehouse()
    {
        var weapon = TestItems.WeaponInstance("weapon");
        Register(weapon.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, weapon);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, weapon);
        GameSession.LocalPlayer.Loadout.TryAddToWarehouse(weapon);
        GameSession.BeginRaid();
        GameSession.LocalPlayer.Raid.TryAddToCarry(weapon);
        GameSession.AbandonRaid();

        Assert.That(GameSession.LocalPlayer.Raid.CarryCount(), Is.Zero);
        Assert.That(GameSession.LocalPlayer.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
        Assert.That(GameSession.LocalPlayer.Loadout.WarehouseCount(), Is.EqualTo(1));
        Assert.That(PlayerSaveStore.Load("account-alpha").equipment, Is.Empty);
        Assert.That(PlayerSaveStore.Load("account-alpha").warehouse.Count, Is.EqualTo(1));
        GameSession.SetLocalPlayerId(null);
        Assert.That(GameSession.Loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(weapon));
    }

    [Test]
    public void AccountExtraction_SettlesOnlyBoundAccount()
    {
        var loot = TestItems.WeaponInstance("loot");
        Register(loot.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.BeginRaid();
        GameSession.LocalPlayer.Raid.TryAddToCarry(loot);
        GameSession.ExtractRaid();

        Assert.That(GameSession.LocalPlayer.Loadout.WarehouseCount(), Is.EqualTo(1));
        Assert.That(PlayerSaveStore.Load("account-alpha").warehouse.Count, Is.EqualTo(1));
        GameSession.SetLocalPlayerId("account-beta");
        Assert.That(GameSession.LocalPlayer.Loadout.WarehouseCount(), Is.Zero);
        GameSession.SetLocalPlayerId(null);
        Assert.That(GameSession.Loadout.WarehouseCount(), Is.Zero);
    }

    [Test]
    public void ReloadPlayer_RestoresContextFromFreshCache()
    {
        var rifle = TestItems.WeaponInstance("步枪");
        var shotgun = TestItems.WeaponInstance("霰弹枪");
        Register(rifle.Definition, shotgun.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);

        // 模拟云同步落地：直接改写缓存，再要求上下文重载。
        var fresh = PlayerSaveData.CreateEmpty();
        fresh.warehouse.Add(new PlayerSaveData.SlotEntry("0", shotgun.Definition.Id, shotgun.InstanceId, -1));
        PlayerSaveStore.Save(fresh, "account-alpha");
        PlayerSaveStore.Flush();

        GameSession.ReloadPlayer("account-alpha");

        var loadout = GameSession.LocalPlayer.Loadout;
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
        Assert.That(loadout.GetWarehouse(0).Definition.Id, Is.EqualTo(shotgun.Definition.Id));
    }

    [Test]
    public void RaidEnded_FiresOnExtractAndAbandon()
    {
        var fired = 0;
        System.Action handler = () => fired++;
        GameSession.RaidEnded += handler;
        try
        {
            GameSession.EnsureInitialized(database);
            GameSession.ExtractRaid();
            GameSession.AbandonRaid();
            Assert.That(fired, Is.EqualTo(2));
        }
        finally
        {
            GameSession.RaidEnded -= handler;
        }
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
