using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LootPoolTests
{
    private ItemDefinition common;
    private ItemDefinition epic;

    [SetUp]
    public void SetUp()
    {
        common = CreateItem("普通物品", ItemRarity.Common);
        epic = CreateItem("史诗物品", ItemRarity.Epic);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(common);
        Object.DestroyImmediate(epic);
    }

    [Test]
    public void ShouldDrop_ZeroChanceNeverDrops()
    {
        Assert.That(LootPool.ShouldDrop(0f, 0.0), Is.False);
        Assert.That(LootPool.ShouldDrop(0f, 0.999), Is.False);
    }

    [Test]
    public void ShouldDrop_FullChanceAlwaysDrops()
    {
        Assert.That(LootPool.ShouldDrop(1f, 0.0), Is.True);
        Assert.That(LootPool.ShouldDrop(1f, 0.999), Is.True);
    }

    [Test]
    public void ShouldDrop_ComparesRollAgainstChance()
    {
        Assert.That(LootPool.ShouldDrop(0.5f, 0.49), Is.True);
        Assert.That(LootPool.ShouldDrop(0.5f, 0.5), Is.False);
        Assert.That(LootPool.ShouldDrop(0.5f, 0.51), Is.False);
    }

    [Test]
    public void ResolveChance_ReturnsZeroForUnconfiguredRarity()
    {
        var pool = CreatePool(new[] { common }, new[] { Curve(ItemRarity.Epic, 1, 0.5f) });

        Assert.That(pool.ResolveChance(ItemRarity.Common, 1), Is.EqualTo(0f));
        Assert.That(pool.ResolveChance(ItemRarity.Epic, 1), Is.EqualTo(0.5f).Within(0.0001f));

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void ResolveChance_InterpolatesBetweenFloors()
    {
        var pool = CreatePool(new[] { epic }, new[]
        {
            Curve(ItemRarity.Epic, 1, 0.1f, 11, 0.5f),
        });

        Assert.That(pool.ResolveChance(ItemRarity.Epic, 1), Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(pool.ResolveChance(ItemRarity.Epic, 6), Is.EqualTo(0.3f).Within(0.0001f));
        Assert.That(pool.ResolveChance(ItemRarity.Epic, 11), Is.EqualTo(0.5f).Within(0.0001f));

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void ResolveChance_ClampsOutsideConfiguredRange()
    {
        var pool = CreatePool(new[] { epic }, new[]
        {
            Curve(ItemRarity.Epic, 5, 0.2f, 10, 0.6f),
        });

        Assert.That(pool.ResolveChance(ItemRarity.Epic, 1), Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(pool.ResolveChance(ItemRarity.Epic, 100), Is.EqualTo(0.6f).Within(0.0001f));

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void Roll_HigherFloorYieldsMoreHighRarityDrops()
    {
        var pool = CreatePool(new[] { epic }, new[]
        {
            Curve(ItemRarity.Epic, 1, 0.05f, 20, 0.9f),
        });

        var low = CountDrops(pool, 1, 4000);
        var high = CountDrops(pool, 20, 4000);

        Assert.That(high, Is.GreaterThan(low));

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void Roll_IsReproducibleWithSameSeed()
    {
        var pool = CreatePool(new[] { common, epic }, new[]
        {
            Curve(ItemRarity.Common, 1, 0.5f),
            Curve(ItemRarity.Epic, 1, 0.5f),
        });

        var a = pool.Roll(3, new System.Random(12345));
        var b = pool.Roll(3, new System.Random(12345));

        Assert.That(a.Count, Is.EqualTo(b.Count));
        for (var i = 0; i < a.Count; i++)
            Assert.That(a[i], Is.SameAs(b[i]));

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void Roll_EntriesAreIndependent()
    {
        var pool = CreatePool(new[] { common, epic }, new[]
        {
            Curve(ItemRarity.Common, 1, 1f),
            Curve(ItemRarity.Epic, 1, 1f),
        });

        var result = pool.Roll(1, new System.Random(7));

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0], Is.SameAs(common));
        Assert.That(result[1], Is.SameAs(epic));

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void Roll_SkipsEmptyItemsAndZeroChance()
    {
        var pool = CreatePool(new[] { null, common }, new[]
        {
            Curve(ItemRarity.Common, 1, 0f),
        });

        Assert.That(pool.Roll(1, new System.Random(7)), Is.Empty);

        Object.DestroyImmediate(pool);
    }

    [Test]
    public void Roll_ReturnsEmptyWhenRandomIsNull()
    {
        var pool = CreatePool(new[] { common }, new[] { Curve(ItemRarity.Common, 1, 1f) });

        Assert.That(pool.Roll(1, null), Is.Empty);

        Object.DestroyImmediate(pool);
    }

    private static int CountDrops(LootPool pool, int floor, int iterations)
    {
        var random = new System.Random(2024);
        var count = 0;
        for (var i = 0; i < iterations; i++)
            count += pool.Roll(floor, random).Count;

        return count;
    }

    private static LootRarityCurve Curve(ItemRarity rarity, params float[] floorChancePairs)
    {
        var points = new LootRarityCurve.Point[floorChancePairs.Length / 2];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new LootRarityCurve.Point
            {
                floor = (int)floorChancePairs[i * 2],
                chance = floorChancePairs[i * 2 + 1],
            };
        }

        return new LootRarityCurve { rarity = rarity, points = points };
    }

    private static LootPool CreatePool(ItemDefinition[] items, LootRarityCurve[] curves)
    {
        var pool = ScriptableObject.CreateInstance<LootPool>();
        pool.Configure(items, curves);
        return pool;
    }

    private static ItemDefinition CreateItem(string name, ItemRarity rarity)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("displayName").stringValue = name;
        serialized.FindProperty("rarity").enumValueIndex = (int)rarity;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }
}
