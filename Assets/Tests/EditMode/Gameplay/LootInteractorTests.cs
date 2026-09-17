using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LootInteractorTests
{
    private GameObject playerRoot;
    private GameObject holderRoot;
    private GameObject panelRoot;
    private GameObject droppedRoot;
    private LootInteractor interactor;
    private InventoryHolder holder;
    private LootPanelController panel;
    private DroppedItem2D dropped;
    private ItemDefinition item;

    [SetUp]
    public void SetUp()
    {
        item = ItemDefinitionFactory.Create(null, null, 0.4f);

        playerRoot = new GameObject("Player");
        interactor = playerRoot.AddComponent<LootInteractor>();

        holderRoot = new GameObject("Holder");
        holder = holderRoot.AddComponent<InventoryHolder>();

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
        dropped.Initialize(item);

        var serialized = new SerializedObject(interactor);
        serialized.FindProperty("inventory").objectReferenceValue = holder;
        serialized.FindProperty("panel").objectReferenceValue = panel;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerRoot);
        Object.DestroyImmediate(holderRoot);
        Object.DestroyImmediate(panelRoot);
        Object.DestroyImmediate(droppedRoot);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void TryPickup_AddsItemToInventoryAndDestroysDrop()
    {
        var result = interactor.TryPickup(dropped);

        Assert.That(result, Is.True);
        Assert.That(holder.Model.GetBagItem(0), Is.SameAs(item));
        Assert.That(dropped == null, Is.True, "掉落物应被销毁");
    }

    [Test]
    public void TryPickup_ReturnsFalseWhenBagIsFull()
    {
        for (var i = 0; i < Inventory.BagCapacity; i++)
        {
            var filler = ItemDefinitionFactory.Create(null, null, 0.4f);
            holder.Model.TryAdd(filler);
        }

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

        for (var i = 0; i < Inventory.BagCapacity; i++)
            holder.Model.TryAdd(ItemDefinitionFactory.Create(null, null, 0.4f));

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
}
