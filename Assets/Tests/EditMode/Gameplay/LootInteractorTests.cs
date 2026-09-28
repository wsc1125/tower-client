using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LootInteractorTests
{
    private GameObject playerRoot;
    private GameObject panelRoot;
    private GameObject droppedRoot;
    private LootInteractor interactor;
    private LootPanelController panel;
    private DroppedItem2D dropped;
    private ItemDefinition item;
    private ItemInstance instance;

    [SetUp]
    public void SetUp()
    {
        GameSession.ResetForTests();
        item = ItemDefinitionFactory.Create(null, null, 0.4f);
        instance = ItemInstance.Create(item);

        playerRoot = new GameObject("Player");
        interactor = playerRoot.AddComponent<LootInteractor>();

        panelRoot = new GameObject("LootPanel");
        panel = panelRoot.AddComponent<LootPanelController>();
        var innerPanel = new GameObject("PanelRoot");
        innerPanel.transform.SetParent(panelRoot.transform);
        var serializedPanel = new SerializedObject(panel);
        serializedPanel.FindProperty("panelRoot").objectReferenceValue = innerPanel;
        serializedPanel.FindProperty("rows").arraySize = 0;
        serializedPanel.ApplyModifiedPropertiesWithoutUndo();

        droppedRoot = new GameObject("Dropped");
        dropped = droppedRoot.AddComponent<DroppedItem2D>();
        dropped.Initialize(instance);

        var serialized = new SerializedObject(interactor);
        serialized.FindProperty("panel").objectReferenceValue = panel;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerRoot);
        Object.DestroyImmediate(panelRoot);
        Object.DestroyImmediate(droppedRoot);
        Object.DestroyImmediate(item);
        GameSession.ResetForTests();
    }

    [Test]
    public void TryPickup_AddsItemToCarryAndDestroysDrop()
    {
        var result = interactor.TryPickup(dropped);

        Assert.That(result, Is.True);
        Assert.That(GameSession.Raid.GetCarry(0), Is.SameAs(instance));
        Assert.That(dropped == null, Is.True, "掉落物应被销毁");
    }

    [Test]
    public void TryPickup_RecordsLootForResultPanel()
    {
        interactor.TryPickup(dropped);

        Assert.That(GameSession.Raid.Loot.Count, Is.EqualTo(1));
        Assert.That(GameSession.Raid.Loot[0], Is.SameAs(instance));
    }

    [Test]
    public void TryPickup_ReturnsFalseWhenCarryIsFull()
    {
        for (var i = 0; i < RaidBag.CarryCapacity; i++)
            GameSession.Raid.TryAddToCarry(ItemInstance.Create(ItemDefinitionFactory.Create(null, null, 0.4f)));

        var result = interactor.TryPickup(dropped);

        Assert.That(result, Is.False);
        Assert.That(dropped != null, Is.True, "背包满时掉落物必须保留");
    }

    [Test]
    public void TryPickup_ShowsHintWhenBagIsFull()
    {
        var hintObject = new GameObject("Hint");
        hintObject.transform.SetParent(panelRoot.transform);
        var hint = hintObject.AddComponent<UnityEngine.UI.Text>();
        var serializedPanel = new SerializedObject(panel);
        serializedPanel.FindProperty("hintText").objectReferenceValue = hint;
        serializedPanel.ApplyModifiedPropertiesWithoutUndo();

        for (var i = 0; i < RaidBag.CarryCapacity; i++)
            GameSession.Raid.TryAddToCarry(ItemInstance.Create(ItemDefinitionFactory.Create(null, null, 0.4f)));

        interactor.TryPickup(dropped);

        Assert.That(hint.text, Is.EqualTo("背包已满"));
    }

    [Test]
    public void TryPickup_ReturnsFalseForNullArguments()
    {
        Assert.That(interactor.TryPickup(null), Is.False);
    }

    [Test]
    public void Scan_HidesPanelWhenAliveEnemyIsNearby()
    {
        var enemyRoot = new GameObject("Enemy");
        enemyRoot.transform.position = new Vector3(1f, 0f, 0f);
        enemyRoot.AddComponent<Rigidbody2D>();
        enemyRoot.AddComponent<EnemyBrain2D>();
        panel.Show(new System.Collections.Generic.List<LootEntry>
        {
            new LootEntry { DisplayName = "甲", Value = 1 },
        });

        interactor.Scan();

        Assert.That(panel.IsVisible, Is.False);
        Object.DestroyImmediate(enemyRoot);
    }

    [Test]
    public void Scan_HidesPanelWhenNoEnemyAndNoDrop()
    {
        panel.Show(new System.Collections.Generic.List<LootEntry>
        {
            new LootEntry { DisplayName = "甲", Value = 1 },
        });

        interactor.Scan();

        Assert.That(panel.IsVisible, Is.False);
    }

    [Test]
    public void Scan_DoesNothingWhenPanelNotWired()
    {
        var serialized = new SerializedObject(interactor);
        serialized.FindProperty("panel").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.DoesNotThrow(() => interactor.Scan());
    }

    [Test]
    public void CollectDamageables_IncludesSwarmAndBoss()
    {
        // 虫群与 Boss 也是 IDamageable，必须被收集，否则「附近有敌人」判定会漏。
        var swarmRoot = new GameObject("Swarm");
        swarmRoot.AddComponent<Rigidbody2D>();
        swarmRoot.AddComponent<SwarmChaser2D>();

        var bossRoot = new GameObject("Boss");
        bossRoot.AddComponent<Rigidbody2D>();
        bossRoot.AddComponent<BossBrain2D>();

        var collected = LootInteractor.CollectDamageables();

        Assert.That(collected, Has.Some.InstanceOf<SwarmChaser2D>());
        Assert.That(collected, Has.Some.InstanceOf<BossBrain2D>());

        Object.DestroyImmediate(swarmRoot);
        Object.DestroyImmediate(bossRoot);
    }

    [Test]
    public void CollectDamageables_IncludesEnemyBrain()
    {
        var enemyRoot = new GameObject("Enemy");
        enemyRoot.AddComponent<Rigidbody2D>();
        enemyRoot.AddComponent<EnemyBrain2D>();

        var collected = LootInteractor.CollectDamageables();

        Assert.That(collected, Has.Some.InstanceOf<EnemyBrain2D>());

        Object.DestroyImmediate(enemyRoot);
    }

    [Test]
    public void Scan_HidesPanelWhenSwarmIsWithinCombatRadius()
    {
        // 虫群在战斗半径内时，面板必须被隐藏（即使附近有掉落物）。
        var swarmRoot = new GameObject("Swarm");
        swarmRoot.transform.position = playerRoot.transform.position + new Vector3(1f, 0f, 0f);
        swarmRoot.AddComponent<Rigidbody2D>();
        swarmRoot.AddComponent<SwarmChaser2D>();
        panel.Show(new System.Collections.Generic.List<LootEntry>
        {
            new LootEntry { DisplayName = "甲", Value = 1 },
        });

        interactor.Scan();

        Assert.That(panel.IsVisible, Is.False, "虫群在战斗半径内时不应显示拾取列表");
        Object.DestroyImmediate(swarmRoot);
    }

    [Test]
    public void Scan_HidesPanelWhenBossIsWithinCombatRadius()
    {
        var bossRoot = new GameObject("Boss");
        bossRoot.transform.position = playerRoot.transform.position + new Vector3(1f, 0f, 0f);
        bossRoot.AddComponent<Rigidbody2D>();
        bossRoot.AddComponent<BossBrain2D>();
        panel.Show(new System.Collections.Generic.List<LootEntry>
        {
            new LootEntry { DisplayName = "甲", Value = 1 },
        });

        interactor.Scan();

        Assert.That(panel.IsVisible, Is.False, "Boss 在战斗半径内时不应显示拾取列表");
        Object.DestroyImmediate(bossRoot);
    }
}
