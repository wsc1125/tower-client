using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerWeaponSelectorTests
{
    private GameObject contextRoot;
    private GameObject selectorRoot;
    private GameObject switchRoot;
    private InventoryHolder holder;
    private PlayerWeaponSelector selector;
    private AttackSwitchButton switchButton;
    private ItemDefinition sword;
    private ItemDefinition rifle;

    [SetUp]
    public void SetUp()
    {
        contextRoot = new GameObject("PlayerContext");
        holder = contextRoot.AddComponent<InventoryHolder>();

        switchRoot = new GameObject("AttackSwitchButton");
        switchButton = switchRoot.AddComponent<AttackSwitchButton>();
        EditModeLifecycle.Invoke(switchButton, "Awake");

        selectorRoot = new GameObject("Selector");
        selector = selectorRoot.AddComponent<PlayerWeaponSelector>();
        Wire(selector, "holder", holder);
        Wire(selector, "switchButton", switchButton);
        EditModeLifecycle.Invoke(selector, "OnEnable");

        sword = ItemDefinitionFactory.Create(null, null, 0.4f, EquipmentSlotType.MeleeWeapon);
        rifle = ItemDefinitionFactory.Create(null, null, 0.4f, EquipmentSlotType.RangedWeapon);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(selectorRoot);
        Object.DestroyImmediate(switchRoot);
        Object.DestroyImmediate(contextRoot);
        Object.DestroyImmediate(sword);
        Object.DestroyImmediate(rifle);
    }

    [Test]
    public void ActiveSlot_IsRanged_WhenOnlyRifleEquipped()
    {
        Equip(rifle);

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.RangedWeapon));
    }

    [Test]
    public void ActiveSlot_IsMelee_WhenBothWeaponsEquipped()
    {
        Equip(sword);
        Equip(rifle);

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MeleeWeapon));
        Assert.That(selector.IsActive(EquipmentSlotType.MeleeWeapon), Is.True);
        Assert.That(selector.IsActive(EquipmentSlotType.RangedWeapon), Is.False);
    }

    [Test]
    public void Toggle_SwitchesToOtherWeapon_WhenBothEquipped()
    {
        Equip(sword);
        Equip(rifle);

        selector.Toggle();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.RangedWeapon));

        selector.Toggle();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MeleeWeapon));
    }

    [Test]
    public void Toggle_KeepsCurrentWeapon_WhenOtherSlotIsEmpty()
    {
        Equip(rifle);

        selector.Toggle();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.RangedWeapon));
    }

    [Test]
    public void EquippingAnotherWeapon_KeepsCurrentWeapon()
    {
        Equip(rifle);

        Equip(sword);

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.RangedWeapon));
    }

    [Test]
    public void UnequippingActiveWeapon_FallsBackToOtherWeapon()
    {
        Equip(sword);
        Equip(rifle);
        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MeleeWeapon));

        holder.Model.TryUnequipToBag(EquipmentSlotType.MeleeWeapon, 19);

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.RangedWeapon));
    }

    [Test]
    public void ClickingSwitchButton_TogglesActiveWeapon()
    {
        Equip(sword);
        Equip(rifle);

        switchButton.GetComponent<Button>().onClick.Invoke();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.RangedWeapon));
    }

    [Test]
    public void Changed_FiresOncePerSwitch()
    {
        Equip(sword);
        Equip(rifle);
        var fired = 0;
        selector.Changed += () => fired++;

        selector.Toggle();
        selector.Toggle();

        Assert.That(fired, Is.EqualTo(2));
    }

    private void Equip(ItemDefinition item)
    {
        holder.Model.TryAdd(item);

        for (var i = 0; i < Inventory.BagCapacity; i++)
        {
            if (holder.Model.GetBagItem(i) != item)
                continue;

            holder.Model.TryEquipFromBag(i, item.EquipSlotType);
            return;
        }
    }

    private static void Wire(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
