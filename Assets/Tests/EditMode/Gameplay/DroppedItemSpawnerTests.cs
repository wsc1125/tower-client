using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="DroppedItemSpawner"/> 的测试：验证「丢弃 → 地面掉落物」这条路径。
/// 与 <see cref="LootDropperTests"/> 同一套夹具写法（模板保持 active，用 Item 是否为 null 区分模板与实例）。
/// </summary>
public sealed class DroppedItemSpawnerTests
{
    private GameObject spawnerRoot;
    private GameObject prefabRoot;
    private DroppedItem2D dropPrefab;
    private DroppedItemSpawner spawner;
    private ItemDefinition item;

    [SetUp]
    public void SetUp()
    {
        item = ItemDefinitionFactory.Create(null, null, 0.4f);

        prefabRoot = new GameObject("DroppedItemPrefab");
        prefabRoot.AddComponent<CircleCollider2D>().isTrigger = true;
        dropPrefab = prefabRoot.AddComponent<DroppedItem2D>();

        spawnerRoot = new GameObject("Spawner");
        spawner = spawnerRoot.AddComponent<DroppedItemSpawner>();

        var serialized = new SerializedObject(spawner);
        serialized.FindProperty("dropPrefab").objectReferenceValue = dropPrefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        // 这个循环会连模板一起销毁，因此后面不再单独销毁 prefabRoot。
        foreach (var found in Object.FindObjectsOfType<DroppedItem2D>())
            Object.DestroyImmediate(found.gameObject);

        if (spawnerRoot != null)
            Object.DestroyImmediate(spawnerRoot);

        if (item != null)
            Object.DestroyImmediate(item);
    }

    [Test]
    public void Spawn_CreatesOneDropAtGivenPosition()
    {
        var ok = spawner.Spawn(ItemInstance.Create(item), new Vector2(3f, -2f));

        Assert.That(ok, Is.True);
        var spawned = FirstSpawned();
        Assert.That(spawned, Is.Not.Null);
        Assert.That((Vector2)spawned.transform.position, Is.EqualTo(new Vector2(3f, -2f)));
    }

    [Test]
    public void Spawn_KeepsTheSameInstanceSoDurabilitySurvives()
    {
        // 丢弃必须带走原实例（含耐久），否则「丢出再捡回」会变成一件满耐久的新装备。
        var armor = ItemDefinitionFactory.Create(null, null, 0.4f, EquipmentSlotType.BodyArmor);
        var serialized = new SerializedObject(armor);
        serialized.FindProperty("durability").intValue = 50;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var instance = ItemInstance.Create(armor);
        instance.ConsumeDurability(7);

        spawner.Spawn(instance, Vector2.zero);

        var spawned = FirstSpawned();
        Assert.That(spawned.Item, Is.SameAs(instance));
        Assert.That(spawned.Item.CurrentDurability, Is.EqualTo(43));

        Object.DestroyImmediate(armor);
    }

    [Test]
    public void Spawn_ReturnsFalseForNullItem()
    {
        Assert.That(spawner.Spawn(null, Vector2.zero), Is.False);
        Assert.That(SpawnedCount(), Is.Zero);
    }

    [Test]
    public void Spawn_ReturnsFalseWhenPrefabMissing()
    {
        var serialized = new SerializedObject(spawner);
        serialized.FindProperty("dropPrefab").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 场景里只剩模板自身（Item 为 null），ResolvePrefab 会把它当模板用，
        // 因此这里先销毁模板，制造「真的没有模板」的场景。
        Object.DestroyImmediate(prefabRoot);
        prefabRoot = null;

        Assert.That(spawner.Spawn(ItemInstance.Create(item), Vector2.zero), Is.False);
    }

    [Test]
    public void Spawn_FallsBackToSceneTemplateWhenNotWired()
    {
        var serialized = new SerializedObject(spawner);
        serialized.FindProperty("dropPrefab").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 场景里已有模板（prefabRoot），未接线时应自动采用它。
        var ok = spawner.Spawn(ItemInstance.Create(item), Vector2.zero);

        Assert.That(ok, Is.True);
        Assert.That(spawner.DropPrefab, Is.SameAs(dropPrefab));
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
