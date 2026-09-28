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
    public void HandleDied_SpawnsNothingWhenLootPoolMissing()
    {
        dropper.HandleDied();

        Assert.That(SpawnedCount(), Is.Zero);
    }

    [Test]
    public void HandleDied_SpawnsOneDropWhenChanceIsFull()
    {
        WirePool(1f);

        dropper.HandleDied();

        Assert.That(SpawnedCount(), Is.EqualTo(1));
    }

    [Test]
    public void HandleDied_SpawnsNothingWhenChanceIsZero()
    {
        WirePool(0f);

        dropper.HandleDied();

        Assert.That(SpawnedCount(), Is.Zero);
    }

    [Test]
    public void HandleDied_SpawnsWithinScatterRadius()
    {
        WirePool(1f);

        dropper.HandleDied();

        var spawned = FirstSpawned();
        var offset = (Vector2)spawned.transform.position - (Vector2)dropperRoot.transform.position;
        Assert.That(offset.magnitude, Is.LessThanOrEqualTo(0.35f + 0.0001f));
    }

    [Test]
    public void HandleDied_InitializesSpawnedItem()
    {
        WirePool(1f);

        dropper.HandleDied();

        // 掉落池掷出的是模板，落地时现场创建实例，因此断言「类型一致」而非同一引用。
        var spawned = FirstSpawned();
        Assert.That(spawned.Item, Is.Not.Null);
        Assert.That(spawned.Definition, Is.SameAs(item));
    }

    [Test]
    public void Configure_StoresPoolAndClampsFloor()
    {
        var pool = ScriptableObject.CreateInstance<LootPool>();

        dropper.Configure(pool, 0);

        Assert.That(dropper.LootPool, Is.SameAs(pool));
        Assert.That(dropper.Floor, Is.EqualTo(1));

        dropper.Configure(pool, 7);
        Assert.That(dropper.Floor, Is.EqualTo(7));

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void HandleDied_UsesInjectedFloorForChance()
    {
        // 概率曲线：第 1 层 0，第 10 层 1。低层不掉、高层必掉，验证层数确实参与掷点。
        var pool = ScriptableObject.CreateInstance<LootPool>();
        pool.Configure(
            new[] { item },
            new[]
            {
                new LootRarityCurve
                {
                    rarity = ItemRarity.Common,
                    points = new[]
                    {
                        new LootRarityCurve.Point { floor = 1, chance = 0f },
                        new LootRarityCurve.Point { floor = 10, chance = 1f },
                    },
                },
            });

        dropper.Configure(pool, 1);
        dropper.HandleDied();
        Assert.That(SpawnedCount(), Is.Zero);

        dropper.Configure(pool, 10);
        dropper.HandleDied();
        Assert.That(SpawnedCount(), Is.EqualTo(1));

        Object.DestroyImmediate(pool);
    }

    private void WirePool(float chance)
    {
        var pool = ScriptableObject.CreateInstance<LootPool>();
        pool.Configure(
            new[] { item },
            new[]
            {
                new LootRarityCurve
                {
                    rarity = ItemRarity.Common,
                    points = new[] { new LootRarityCurve.Point { floor = 1, chance = chance } },
                },
            });

        dropper.Configure(pool, 1);
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
