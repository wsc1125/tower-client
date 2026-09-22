using NUnit.Framework;
using UnityEditor;
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

    [Test]
    public void Holder_WithoutSaveEnabled_DoesNotWritePlayerPrefs()
    {
        PlayerInventoryStore.Delete();

        var root = new GameObject("Holder");
        var holder = root.AddComponent<InventoryHolder>();
        holder.Model.TryAdd(ScriptableObject.CreateInstance<ItemDefinition>());

        Assert.That(PlayerInventoryStore.HasSave, Is.False);

        Object.DestroyImmediate(root);
    }

    [Test]
    public void Holder_WithSaveEnabled_CreatesSaveWithInitialItemsOnce()
    {
        PlayerInventoryStore.Delete();
        var database = ScriptableObject.CreateInstance<ItemDatabase>();
        var item = CreateItem("Sword", database);
        var root = CreateHolder(true, database, item);

        try
        {
            var model = root.Model;

            Assert.That(PlayerInventoryStore.HasSave, Is.True);
            Assert.That(root.CreatedFresh, Is.True);
            Assert.That(model.GetBagItem(0), Is.SameAs(item));

            // 再有新的宿主读取同一份存档时，不应重复注入初始物品。
            var secondRoot = CreateHolder(true, database, item);
            try
            {
                var secondModel = secondRoot.Model;

                Assert.That(secondRoot.CreatedFresh, Is.False);
                Assert.That(secondModel.GetBagItem(0), Is.SameAs(item));
                Assert.That(secondModel.GetBagItem(1), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(secondRoot.gameObject);
            }
        }
        finally
        {
            Object.DestroyImmediate(root.gameObject);
            Object.DestroyImmediate(item);
            Object.DestroyImmediate(database);
            PlayerInventoryStore.Delete();
        }
    }

    private static InventoryHolder CreateHolder(bool loadFromSave, ItemDatabase database, ItemDefinition initialItem)
    {
        var root = new GameObject("Holder");
        var holder = root.AddComponent<InventoryHolder>();
        var serialized = new SerializedObject(holder);
        serialized.FindProperty("loadFromSave").boolValue = loadFromSave;
        serialized.FindProperty("database").objectReferenceValue = database;
        var items = serialized.FindProperty("initialItems");
        items.arraySize = 1;
        items.GetArrayElementAtIndex(0).objectReferenceValue = initialItem;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return holder;
    }

    private static ItemDefinition CreateItem(string id, ItemDatabase database)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.name = id;
        var serialized = new SerializedObject(database);
        var items = serialized.FindProperty("items");
        items.arraySize = 1;
        items.GetArrayElementAtIndex(0).objectReferenceValue = item;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }
}
