using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerWeaponSelectorTests
{
    private GameObject selectorRoot;
    private GameObject switchRoot;
    private PlayerWeaponSelector selector;
    private AttackSwitchButton switchButton;
    private ItemInstance sword;
    private ItemInstance rifle;

    [SetUp]
    public void SetUp()
    {
        GameSession.ResetForTests();

        switchRoot = new GameObject("AttackSwitchButton");
        switchButton = switchRoot.AddComponent<AttackSwitchButton>();
        EditModeLifecycle.Invoke(switchButton, "Awake");

        selectorRoot = new GameObject("Selector");
        selector = selectorRoot.AddComponent<PlayerWeaponSelector>();
        Wire(selector, "switchButton", switchButton);
        EditModeLifecycle.Invoke(selector, "OnEnable");

        sword = ItemInstance.Create(ItemDefinitionFactory.Create(null, null, 0.4f, EquipmentSlotType.MainHand));
        rifle = ItemInstance.Create(ItemDefinitionFactory.Create(null, null, 0.4f, EquipmentSlotType.OffHand));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(selectorRoot);
        Object.DestroyImmediate(switchRoot);
        Object.DestroyImmediate(sword.Definition);
        Object.DestroyImmediate(rifle.Definition);
        GameSession.ResetForTests();
    }

    [Test]
    public void ActiveSlot_IsOffHand_WhenOnlyOffHandWeaponEquipped()
    {
        EquipTo(rifle, EquipmentSlotType.OffHand);

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));
    }

    [Test]
    public void ActiveSlot_IsMainHand_WhenBothWeaponsEquipped()
    {
        Equip(sword);
        Equip(rifle);

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MainHand));
        Assert.That(selector.IsActive(EquipmentSlotType.MainHand), Is.True);
        Assert.That(selector.IsActive(EquipmentSlotType.OffHand), Is.False);
    }

    [Test]
    public void Toggle_SwitchesToOtherWeapon_WhenBothEquipped()
    {
        Equip(sword);
        Equip(rifle);

        selector.Toggle();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));

        selector.Toggle();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MainHand));
    }

    [Test]
    public void Toggle_KeepsCurrentWeapon_WhenOtherSlotIsEmpty()
    {
        EquipTo(rifle, EquipmentSlotType.OffHand);

        selector.Toggle();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));
    }

    [Test]
    public void EquippingAnotherWeapon_KeepsCurrentWeapon()
    {
        EquipTo(rifle, EquipmentSlotType.OffHand);
        // 先确立当前武器，再装第二把 —— 只有「已确立」的当前武器才谈得上不被改变。
        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));

        Equip(sword);

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));
    }

    [Test]
    public void UnequippingActiveWeapon_FallsBackToOtherWeapon()
    {
        Equip(sword);
        Equip(rifle);
        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MainHand));

        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, null);
        GameSession.Raid.NotifyChanged();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));
    }

    [Test]
    public void ClickingSwitchButton_TogglesActiveWeapon()
    {
        Equip(sword);
        Equip(rifle);

        switchButton.GetComponent<Button>().onClick.Invoke();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));
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

    /// <summary>把武器装到「主手优先」的第一个空武器槽，模拟玩家在局内装备。</summary>
    private void Equip(ItemInstance item)
    {
        foreach (var slot in EquipmentSlots.WeaponSlots)
        {
            if (EquipTo(item, slot))
                return;
        }
    }

    /// <summary>把武器装到指定武器槽；槽已占用时返回 false。</summary>
    private static bool EquipTo(ItemInstance item, EquipmentSlotType slot)
    {
        if (GameSession.Loadout.Equipment.Get(slot) != null)
            return false;

        GameSession.Loadout.Equipment.Set(slot, item);
        GameSession.Raid.NotifyChanged();
        return true;
    }

    private static void Wire(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
