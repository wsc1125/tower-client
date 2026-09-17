using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LootDropperTests
{
    private GameObject dropperRoot;
    private GameObject prefabRoot;
    private DroppedItem2D dropPrefab;
    private LootDropper dropper;
    private ItemDefinition item;

    [SetUp]
    public void SetUp()
    {
        item = ItemDefinitionFactory.Create(null, null, 0.4f);

        // 模板保持 active：inactive 的话 Instantiate 出来的克隆也是 inactive，
        // 而 Object.FindObjectsOfType 默认不返回 inactive 对象。
        // 模板自身 Item 为 null，用它把模板与实例化的掉落物区分开。
        prefabRoot = new GameObject("DroppedItemPrefab");
        prefabRoot.AddComponent<CircleCollider2D>().isTrigger = true;
        dropPrefab = prefabRoot.AddComponent<DroppedItem2D>();

        dropperRoot = new GameObject("Dropper");
        dropperRoot.transform.position = new Vector3(5f, 5f, 0f);
        dropper = dropperRoot.AddComponent<LootDropper>();

        var serialized = new SerializedObject(dropper);
        serialized.FindProperty("dropPrefab").objectReferenceValue = dropPrefab;
        serialized.FindProperty("scatterRadius").floatValue = 0.35f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        // 这个循环会连模板一起销毁，因此后面不再单独销毁 prefabRoot。
        foreach (var found in Object.FindObjectsOfType<DroppedItem2D>())
            Object.DestroyImmediate(found.gameObject);

        if (dropperRoot != null)
            Object.DestroyImmediate(dropperRoot);

        if (item != null)
            Object.DestroyImmediate(item);
    }

    [Test]
    public void HandleDied_SpawnsNothingWhenLootTableMissing()
    {
        dropper.HandleDied();

        Assert.That(SpawnedCount(), Is.Zero);
    }

    [Test]
    public void HandleDied_SpawnsOneDropWhenChanceIsFull()
    {
        WireTable(1f);

        dropper.HandleDied();

        Assert.That(SpawnedCount(), Is.EqualTo(1));
    }

    [Test]
    public void HandleDied_SpawnsNothingWhenChanceIsZero()
    {
        WireTable(0f);

        dropper.HandleDied();

        Assert.That(SpawnedCount(), Is.Zero);
    }

    [Test]
    public void HandleDied_SpawnsWithinScatterRadius()
    {
        WireTable(1f);

        dropper.HandleDied();

        var spawned = FirstSpawned();
        var offset = (Vector2)spawned.transform.position - (Vector2)dropperRoot.transform.position;
        Assert.That(offset.magnitude, Is.LessThanOrEqualTo(0.35f + 0.0001f));
    }

    [Test]
    public void HandleDied_InitializesSpawnedItem()
    {
        WireTable(1f);

        dropper.HandleDied();

        Assert.That(FirstSpawned().Item, Is.SameAs(item));
    }

    private void WireTable(float chance)
    {
        var table = ScriptableObject.CreateInstance<LootTable>();
        table.Configure(new[] { new LootTable.Entry { item = item, dropChance = chance } });
        var serialized = new SerializedObject(dropper);
        serialized.FindProperty("lootTable").objectReferenceValue = table;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static int SpawnedCount()
    {
        var count = 0;
        foreach (var found in Object.FindObjectsOfType<DroppedItem2D>())
        {
            if (found.Item != null)
                count++;
        }

        return count;
    }

    private static DroppedItem2D FirstSpawned()
    {
        foreach (var found in Object.FindObjectsOfType<DroppedItem2D>())
        {
            if (found.Item != null)
                return found;
        }

        return null;
    }
}
