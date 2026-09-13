using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BagSlotViewTests
{
    private static ItemDefinition CreateItem(EquipmentSlotType slot)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)slot;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    [Test]
    public void EmptyBagSlot_HasNoDragSource()
    {
        var view = new GameObject("BagSlot").AddComponent<BagItemView>();
        view.Bind(null, 2);
        view.SetItem(null);

        Assert.That(view.TryGetDragSource(out _), Is.False);
        Object.DestroyImmediate(view.gameObject);
    }

    [Test]
    public void PopulatedBagSlot_ExposesItsIndex()
    {
        var view = new GameObject("BagSlot").AddComponent<BagItemView>();
        view.Bind(null, 4);
        view.SetItem(CreateItem(EquipmentSlotType.MeleeWeapon));

        Assert.That(view.TryGetDragSource(out var source), Is.True);
        Assert.That(source.Kind, Is.EqualTo(BagLocationKind.Bag));
        Assert.That(source.BagIndex, Is.EqualTo(4));
        Object.DestroyImmediate(view.gameObject);
    }

    [Test]
    public void ArmorSlot_RejectsEverySource()
    {
        var view = new GameObject("Helmet").AddComponent<EquipmentSlotView>();
        view.Bind(null, EquipmentSlotType.Helmet);
        var source = BagDragSource.FromBag(0, CreateItem(EquipmentSlotType.Helmet));

        Assert.That(view.CanAccept(source, null), Is.False);
        Object.DestroyImmediate(view.gameObject);
    }

    [Test]
    public void MeleeSlot_AcceptsOnlyMatchingBagItemWhenEmpty()
    {
        var inventory = new Inventory();
        var view = new GameObject("Melee").AddComponent<EquipmentSlotView>();
        view.Bind(null, EquipmentSlotType.MeleeWeapon);

        Assert.That(view.CanAccept(BagDragSource.FromBag(0, CreateItem(EquipmentSlotType.MeleeWeapon)), inventory), Is.True);
        Assert.That(view.CanAccept(BagDragSource.FromBag(0, CreateItem(EquipmentSlotType.RangedWeapon)), inventory), Is.False);
        Object.DestroyImmediate(view.gameObject);
    }

    [Test]
    public void BagTarget_AcceptsEquippedItemOnlyWhenEmpty()
    {
        var inventory = new Inventory();
        var view = new GameObject("BagSlot").AddComponent<BagItemView>();
        view.Bind(null, 3);
        var source = BagDragSource.FromEquipment(EquipmentSlotType.MeleeWeapon, CreateItem(EquipmentSlotType.MeleeWeapon));

        Assert.That(view.CanAccept(source, inventory), Is.True);
        inventory.TryAdd(CreateItem(EquipmentSlotType.RangedWeapon));
        inventory.TrySwapInBag(0, 3);
        Assert.That(view.CanAccept(source, inventory), Is.False);
        Object.DestroyImmediate(view.gameObject);
    }
}
