using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="CollectionService"/> 的结算、点亮与发奖。
/// </summary>
public sealed class CollectionServiceTests
{
    private ItemDatabase database;
    private CollectionCatalog catalog;
    private ItemDefinition alloy;
    private ItemDefinition ration;
    private ItemDefinition weapon;
    private readonly List<int> grantedRewards = new List<int>();

    [SetUp]
    public void SetUp()
    {
        CollectionStore.Delete();
        CollectionService.ResetForTests();
        grantedRewards.Clear();

        database = ScriptableObject.CreateInstance<ItemDatabase>();
        catalog = ScriptableObject.CreateInstance<CollectionCatalog>();

        alloy = TestItems.Create(EquipmentSlotType.None, "废合金");
        ration = TestItems.Create(EquipmentSlotType.None, "口粮");
        weapon = TestItems.Weapon("步枪");
        SetDatabaseItems(alloy, ration, weapon);

        CollectionService.Configure(database, catalog);
        CollectionService.RewardGranted += grantedRewards.Add;
    }

    [TearDown]
    public void TearDown()
    {
        CollectionService.RewardGranted -= grantedRewards.Add;
        CollectionStore.Delete();
        CollectionService.ResetForTests();
        Object.DestroyImmediate(database);
        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void RecordRaidLoot_UnlocksLootItems()
    {
        var loot = new List<ItemInstance> { TestItems.Instance(alloy) };

        var added = CollectionService.RecordRaidLoot(loot);

        Assert.That(added, Is.EqualTo(1));
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained));
    }

    [Test]
    public void RecordRaidLoot_IgnoresNonCollectibleItems()
    {
        var loot = new List<ItemInstance> { TestItems.Instance(weapon) };

        var added = CollectionService.RecordRaidLoot(loot);

        Assert.That(added, Is.EqualTo(0), "武器不应进图鉴");
        Assert.That(CollectionService.StateOf(weapon.Id), Is.EqualTo(CollectionState.Locked));
    }

    [Test]
    public void RecordRaidLoot_IsIdempotent()
    {
        var loot = new List<ItemInstance> { TestItems.Instance(alloy) };
        CollectionService.RecordRaidLoot(loot);

        var added = CollectionService.RecordRaidLoot(loot);

        Assert.That(added, Is.EqualTo(0), "重复带出不应重复计数");
    }

    [Test]
    public void RecordRaidLoot_WithNull_DoesNotThrow()
    {
        Assert.That(CollectionService.RecordRaidLoot(null), Is.EqualTo(0));
    }

    [Test]
    public void TryLight_GrantsRarityReward()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });

        var ok = CollectionService.TryLight(alloy.Id, out var reward);

        Assert.That(ok, Is.True);
        Assert.That(reward, Is.EqualTo(30), "普通品质奖励 30");
        Assert.That(grantedRewards, Is.EqualTo(new[] { 30 }));
    }

    [Test]
    public void TryLight_RequiresObtained()
    {
        var ok = CollectionService.TryLight(alloy.Id, out var reward);

        Assert.That(ok, Is.False);
        Assert.That(reward, Is.EqualTo(0));
        Assert.That(grantedRewards, Is.Empty);
    }

    [Test]
    public void TryLight_OnLastItem_GrantsAllCollectedBonus()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance>
        {
            TestItems.Instance(alloy),
            TestItems.Instance(ration),
        });

        CollectionService.TryLight(alloy.Id, out _);
        CollectionService.TryLight(ration.Id, out _);

        // 两件单件奖励 + 全收集 3000。
        Assert.That(grantedRewards, Is.EqualTo(new[] { 30, 30, 3000 }));
    }

    [Test]
    public void TryLight_DoesNotGrantAllCollectedBonusTwice()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance>
        {
            TestItems.Instance(alloy),
            TestItems.Instance(ration),
        });
        CollectionService.TryLight(alloy.Id, out _);
        CollectionService.TryLight(ration.Id, out _);
        grantedRewards.Clear();

        // 再点亮一次已点亮的物品，不应再发任何奖励。
        CollectionService.TryLight(alloy.Id, out _);

        Assert.That(grantedRewards, Is.Empty);
    }

    [Test]
    public void Progress_IgnoresOrphanIds()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });
        // 手工塞一个不在图鉴里的 ID。
        CollectionService.Progress.Obtain("OrphanItem");

        Assert.That(CollectionService.TotalCount, Is.EqualTo(2), "图鉴总数应为 2（合金 + 口粮）");
        Assert.That(CollectionService.ObtainedCount, Is.EqualTo(1), "孤儿 ID 不应计入");
    }

    [Test]
    public void StateOf_ReflectsThreeStates()
    {
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Locked));

        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained));

        CollectionService.TryLight(alloy.Id, out _);
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Lit));
    }

    [Test]
    public void RecordRaidLoot_PersistsAcrossReload()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });
        CollectionStore.Flush();

        CollectionService.ResetForTests();
        CollectionService.Configure(database, catalog);

        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained));
    }

    [Test]
    public void ExtractRaid_UnlocksLootButNotCarriedEquipment()
    {
        // 出征：把武器放进携带（模拟玩家带进地牢的装备）。
        GameSession.ResetForTests();
        GameSession.EnsureInitialized(database);
        GameSession.Raid.TryAddToCarry(TestItems.Instance(weapon));

        // 局内捡到一件材料。
        GameSession.Raid.TryAddToCarry(TestItems.Instance(alloy));
        GameSession.Raid.RecordLoot(TestItems.Instance(alloy));

        GameSession.ExtractRaid();

        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained),
            "局内拾取的材料应获得收藏资格");
        Assert.That(CollectionService.StateOf(weapon.Id), Is.EqualTo(CollectionState.Locked),
            "带入地牢的装备不应获得收藏资格");
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
}
