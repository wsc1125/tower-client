using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LootTableTests
{
    private ItemDefinition first;
    private ItemDefinition second;

    [SetUp]
    public void SetUp()
    {
        first = CreateItem("第一件");
        second = CreateItem("第二件");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
    }

    [Test]
    public void ShouldDrop_ZeroChanceNeverDrops()
    {
        Assert.That(LootTable.ShouldDrop(0f, 0.0), Is.False);
        Assert.That(LootTable.ShouldDrop(0f, 0.999), Is.False);
    }

    [Test]
    public void ShouldDrop_FullChanceAlwaysDrops()
    {
        Assert.That(LootTable.ShouldDrop(1f, 0.0), Is.True);
        Assert.That(LootTable.ShouldDrop(1f, 0.999), Is.True);
    }

    [Test]
    public void ShouldDrop_ComparesRollAgainstChance()
    {
        Assert.That(LootTable.ShouldDrop(0.5f, 0.49), Is.True);
        Assert.That(LootTable.ShouldDrop(0.5f, 0.5), Is.False);
        Assert.That(LootTable.ShouldDrop(0.5f, 0.51), Is.False);
    }

    [Test]
    public void Roll_IsReproducibleWithSameSeed()
    {
        var table = CreateTable(new[]
        {
            new LootTable.Entry { item = first, dropChance = 0.5f },
            new LootTable.Entry { item = second, dropChance = 0.5f },
        });

        var a = table.Roll(new System.Random(12345));
        var b = table.Roll(new System.Random(12345));

        Assert.That(a.Count, Is.EqualTo(b.Count));
        for (var i = 0; i < a.Count; i++)
            Assert.That(a[i], Is.SameAs(b[i]));

        Object.DestroyImmediate(table);
    }

    [Test]
    public void Roll_EntriesAreIndependent()
    {
        var table = CreateTable(new[]
        {
            new LootTable.Entry { item = first, dropChance = 1f },
            new LootTable.Entry { item = second, dropChance = 1f },
        });

        var result = table.Roll(new System.Random(7));

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0], Is.SameAs(first));
        Assert.That(result[1], Is.SameAs(second));

        Object.DestroyImmediate(table);
    }

    [Test]
    public void Roll_SkipsEmptyEntriesAndZeroChance()
    {
        var table = CreateTable(new[]
        {
            new LootTable.Entry { item = null, dropChance = 1f },
            new LootTable.Entry { item = first, dropChance = 0f },
        });

        var result = table.Roll(new System.Random(7));

        Assert.That(result, Is.Empty);

        Object.DestroyImmediate(table);
    }

    [Test]
    public void Roll_ReturnsEmptyWhenRandomIsNull()
    {
        var table = CreateTable(new[] { new LootTable.Entry { item = first, dropChance = 1f } });

        Assert.That(table.Roll(null), Is.Empty);

        Object.DestroyImmediate(table);
    }

    private static ItemDefinition CreateItem(string name)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("displayName").stringValue = name;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static LootTable CreateTable(LootTable.Entry[] entries)
    {
        var table = ScriptableObject.CreateInstance<LootTable>();
        table.Configure(entries);
        return table;
    }
}
