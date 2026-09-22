using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 拖拽搬运依赖的三组互换操作：仓库↔携带、仓库↔装备槽、携带↔装备槽。
/// 共同语义：任意一侧为空时退化为单向移动，两侧都有物品时真正互换。
/// </summary>
public sealed class InventorySwapTests
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
    public void TrySwapBagAndCarry_SwapsOccupiedCells()
    {
        var inventory = new Inventory();
        var inBag = CreateItem(EquipmentSlotType.MeleeWeapon);
        var inCarry = CreateItem(EquipmentSlotType.RangedWeapon);
        inventory.TryAdd(inBag);
        inventory.TryAddToCarry(inCarry);

        Assert.That(inventory.TrySwapBagAndCarry(0, 0), Is.True);
        Assert.That(inventory.GetBagItem(0), Is.SameAs(inCarry));
        Assert.That(inventory.GetCarryItem(0), Is.SameAs(inBag));
    }

    [Test]
    public void TrySwapBagAndCarry_MovesIntoEmptyCell()
    {
        var inventory = new Inventory();
        var item = CreateItem(EquipmentSlotType.MeleeWeapon);
        inventory.TryAdd(item);

        Assert.That(inventory.TrySwapBagAndCarry(0, 3), Is.True);
        Assert.That(inventory.GetBagItem(0), Is.Null);
        Assert.That(inventory.GetCarryItem(3), Is.SameAs(item));
    }

    [Test]
    public void TrySwapBagAndCarry_RejectsEmptyPairAndBadIndexes()
    {
        var inventory = new Inventory();
        var changes = 0;
        inventory.Changed += () => changes++;

        Assert.That(inventory.TrySwapBagAndCarry(0, 0), Is.False);
        Assert.That(inventory.TrySwapBagAndCarry(-1, 0), Is.False);
        Assert.That(inventory.TrySwapBagAndCarry(0, Inventory.CarryCapacity), Is.False);
        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void TrySwapBagAndEquipment_EquipsAndReturnsPrevious()
    {
        var inventory = new Inventory();
        var first = CreateItem(EquipmentSlotType.MeleeWeapon);
        var second = CreateItem(EquipmentSlotType.MeleeWeapon);
        inventory.TryAdd(first);
        inventory.TryEquipFromBag(0, EquipmentSlotType.MeleeWeapon);
        inventory.TryAdd(second);

        Assert.That(inventory.TrySwapBagAndEquipment(0, EquipmentSlotType.MeleeWeapon), Is.True);
        Assert.That(inventory.GetEquippedItem(EquipmentSlotType.MeleeWeapon), Is.SameAs(second));
        Assert.That(inventory.GetBagItem(0), Is.SameAs(first));
    }

    [Test]
    public void TrySwapBagAndEquipment_UnequipsIntoEmptyCell()
    {
        var inventory = new Inventory();
        var sword = CreateItem(EquipmentSlotType.MeleeWeapon);
        inventory.TryAdd(sword);
        inventory.TryEquipFromBag(0, EquipmentSlotType.MeleeWeapon);

        Assert.That(inventory.TrySwapBagAndEquipment(5, EquipmentSlotType.MeleeWeapon), Is.True);
        Assert.That(inventory.GetEquippedItem(EquipmentSlotType.MeleeWeapon), Is.Null);
        Assert.That(inventory.GetBagItem(5), Is.SameAs(sword));
    }

    [Test]
    public void TrySwapBagAndEquipment_RejectsMismatchedOrUnusableSlot()
    {
        var inventory = new Inventory();
        var helmet = CreateItem(EquipmentSlotType.Helmet);
        var sword = CreateItem(EquipmentSlotType.MeleeWeapon);
        inventory.TryAdd(helmet);
        inventory.TryAdd(sword);

        // 头盔不能放进近战槽；防具槽尚未启用。
        Assert.That(inventory.TrySwapBagAndEquipment(0, EquipmentSlotType.MeleeWeapon), Is.False);
        Assert.That(inventory.TrySwapBagAndEquipment(0, EquipmentSlotType.Helmet), Is.False);
        Assert.That(inventory.TrySwapBagAndEquipment(1, EquipmentSlotType.None), Is.False);
    }

    [Test]
    public void TrySwapCarryAndEquipment_EquipsFromCarry()
    {
        var inventory = new Inventory();
        var bow = CreateItem(EquipmentSlotType.RangedWeapon);
        inventory.TryAddToCarry(bow);

        Assert.That(inventory.TrySwapCarryAndEquipment(0, EquipmentSlotType.RangedWeapon), Is.True);
        Assert.That(inventory.GetEquippedItem(EquipmentSlotType.RangedWeapon), Is.SameAs(bow));
        Assert.That(inventory.GetCarryItem(0), Is.Null);
    }

    [Test]
    public void TrySwapCarryAndEquipment_UnequipsIntoCarryCell()
    {
        var inventory = new Inventory();
        var bow = CreateItem(EquipmentSlotType.RangedWeapon);
        inventory.TryAddToCarry(bow);
        inventory.TrySwapCarryAndEquipment(0, EquipmentSlotType.RangedWeapon);

        Assert.That(inventory.TrySwapCarryAndEquipment(4, EquipmentSlotType.RangedWeapon), Is.True);
        Assert.That(inventory.GetEquippedItem(EquipmentSlotType.RangedWeapon), Is.Null);
        Assert.That(inventory.GetCarryItem(4), Is.SameAs(bow));
    }

    [Test]
    public void TrySwapCarryAndEquipment_RejectsEmptyPairAndBadIndexes()
    {
        var inventory = new Inventory();
        var changes = 0;
        inventory.Changed += () => changes++;

        Assert.That(inventory.TrySwapCarryAndEquipment(0, EquipmentSlotType.RangedWeapon), Is.False);
        Assert.That(inventory.TrySwapCarryAndEquipment(-1, EquipmentSlotType.RangedWeapon), Is.False);
        Assert.That(changes, Is.Zero);
    }
}
