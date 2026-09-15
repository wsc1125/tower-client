using NUnit.Framework;
using UnityEngine;

public sealed class InventoryHolderTests
{
    [Test]
    public void Model_IsCreatedOnFirstAccess()
    {
        var root = new GameObject("Holder");
        var holder = root.AddComponent<InventoryHolder>();

        Assert.That(holder.Model, Is.Not.Null);

        Object.DestroyImmediate(root);
    }

    [Test]
    public void Model_ReturnsSameInstanceOnRepeatedAccess()
    {
        var root = new GameObject("Holder");
        var holder = root.AddComponent<InventoryHolder>();

        Assert.That(holder.Model, Is.SameAs(holder.Model));

        Object.DestroyImmediate(root);
    }

    [Test]
    public void Model_KeepsStateAcrossAccesses()
    {
        var root = new GameObject("Holder");
        var holder = root.AddComponent<InventoryHolder>();
        var item = ScriptableObject.CreateInstance<ItemDefinition>();

        Assert.That(holder.Model.TryAdd(item), Is.True);
        Assert.That(holder.Model.GetBagItem(0), Is.SameAs(item));

        Object.DestroyImmediate(item);
        Object.DestroyImmediate(root);
    }
}
