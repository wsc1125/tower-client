using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class InventoryTests
{
    private static ItemDefinition CreateItem(EquipmentSlotType slot)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("displayName").stringValue = slot.ToString();
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)slot;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    [Test]
    public void TryAdd_PlacesItemAndRaisesChangedOnce()
    {
        var inventory = new Inventory();
        var item = CreateItem(EquipmentSlotType.MeleeWeapon);
        var changedCount = 0;
        inventory.Changed += () => changedCount++;

        Assert.That(inventory.TryAdd(item), Is.True);
        Assert.That(inventory.GetBagItem(0), Is.SameAs(item));
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void TryAdd_RejectsItemWhenBagIsFull()
    {
        var inventory = new Inventory();
        Assert.That(Inventory.BagCapacity, Is.EqualTo(72));

        for (var i = 0; i < Inventory.BagCapacity; i++)
            Assert.That(inventory.TryAdd(CreateItem(EquipmentSlotType.MeleeWeapon)), Is.True);

        Assert.That(inventory.GetBagItem(Inventory.BagCapacity - 1), Is.Not.Null);
        Assert.That(inventory.GetBagItem(Inventory.BagCapacity), Is.Null);
        Assert.That(inventory.TryAdd(CreateItem(EquipmentSlotType.MeleeWeapon)), Is.False);
    }

    [TestCase(EquipmentSlotType.MeleeWeapon)]
    [TestCase(EquipmentSlotType.RangedWeapon)]
    public void TryEquipFromBag_MatchingWeaponMovesToEquipment(EquipmentSlotType slot)
    {
        var inventory = new Inventory();
        var item = CreateItem(slot);
        inventory.TryAdd(item);

        Assert.That(inventory.TryEquipFromBag(0, slot), Is.True);
        Assert.That(inventory.GetBagItem(0), Is.Null);
        Assert.That(inventory.GetEquippedItem(slot), Is.SameAs(item));
    }

    [Test]
    public void TryEquipFromBag_RejectsMismatchArmorAndOccupiedSlot()
    {
        var inventory = new Inventory();
        var sword = CreateItem(EquipmentSlotType.MeleeWeapon);
        var secondSword = CreateItem(EquipmentSlotType.MeleeWeapon);
        inventory.TryAdd(sword);
        inventory.TryAdd(secondSword);

        Assert.That(inventory.TryEquipFromBag(0, EquipmentSlotType.RangedWeapon), Is.False);
        Assert.That(inventory.TryEquipFromBag(0, EquipmentSlotType.Helmet), Is.False);
        Assert.That(inventory.TryEquipFromBag(0, EquipmentSlotType.MeleeWeapon), Is.True);
        Assert.That(inventory.TryEquipFromBag(1, EquipmentSlotType.MeleeWeapon), Is.False);
    }

    [Test]
    public void TryUnequipToBag_RequiresEmptyValidDestination()
    {
        var inventory = new Inventory();
        var sword = CreateItem(EquipmentSlotType.MeleeWeapon);
        var bow = CreateItem(EquipmentSlotType.RangedWeapon);
        inventory.TryAdd(sword);
        inventory.TryAdd(bow);
        inventory.TryEquipFromBag(0, EquipmentSlotType.MeleeWeapon);

        Assert.That(inventory.TryUnequipToBag(EquipmentSlotType.MeleeWeapon, 1), Is.False);
        Assert.That(inventory.TryUnequipToBag(EquipmentSlotType.MeleeWeapon, 3), Is.True);
        Assert.That(inventory.GetBagItem(3), Is.SameAs(sword));
        Assert.That(inventory.GetEquippedItem(EquipmentSlotType.MeleeWeapon), Is.Null);
    }

    [Test]
    public void TrySwapInBag_SwapsCells()
    {
        var inventory = new Inventory();
        var sword = CreateItem(EquipmentSlotType.MeleeWeapon);
        var bow = CreateItem(EquipmentSlotType.RangedWeapon);
        inventory.TryAdd(sword);
        inventory.TryAdd(bow);

        Assert.That(inventory.TrySwapInBag(0, 1), Is.True);
        Assert.That(inventory.GetBagItem(0), Is.SameAs(bow));
        Assert.That(inventory.GetBagItem(1), Is.SameAs(sword));
    }

    [Test]
    public void InvalidIndexes_AreRejectedWithoutEvents()
    {
        var inventory = new Inventory();
        var changes = 0;
        inventory.Changed += () => changes++;

        Assert.That(inventory.TryEquipFromBag(-1, EquipmentSlotType.MeleeWeapon), Is.False);
        Assert.That(inventory.TryUnequipToBag(EquipmentSlotType.MeleeWeapon, Inventory.BagCapacity), Is.False);
        Assert.That(inventory.TrySwapInBag(-1, Inventory.BagCapacity), Is.False);
        Assert.That(changes, Is.Zero);
    }
}
