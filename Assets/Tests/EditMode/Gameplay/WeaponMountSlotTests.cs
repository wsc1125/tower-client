using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class WeaponMountSlotTests
{
    private readonly List<GameObject> spawned = new List<GameObject>();

    private GameObject slotRoot;
    private GameObject weaponRoot;
    private WeaponMountSlot slot;
    private SpriteRenderer weaponRenderer;
    private Texture2D texture;
    private Sprite sprite;

    private InventoryHolder holder;
    private PlayerWeaponSelector selector;
    private ItemDefinition sword;
    private ItemDefinition rifle;

    [SetUp]
    public void SetUp()
    {
        texture = new Texture2D(4, 4);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

        slotRoot = new GameObject("Slot");
        slot = slotRoot.AddComponent<WeaponMountSlot>();

        weaponRoot = new GameObject("Weapon");
        weaponRenderer = weaponRoot.AddComponent<SpriteRenderer>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(slotRoot);
        Object.DestroyImmediate(weaponRoot);
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);

        foreach (var go in spawned)
            Object.DestroyImmediate(go);

        spawned.Clear();

        Object.DestroyImmediate(sword);
        Object.DestroyImmediate(rifle);
    }

    [Test]
    public void HasWeapon_IsFalse_WhenRendererNotWired()
    {
        Assert.That(slot.HasWeapon, Is.False);
    }

    [Test]
    public void HasWeapon_IsFalse_WhenSpriteMissing()
    {
        WireRenderer();

        Assert.That(slot.HasWeapon, Is.False);
    }

    [Test]
    public void HasWeapon_IsFalse_WhenRendererDisabled()
    {
        WireRenderer();
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = false;

        Assert.That(slot.HasWeapon, Is.False);
    }

    [Test]
    public void HasWeapon_IsTrue_WhenSpriteVisible()
    {
        WireRenderer();
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = true;

        Assert.That(slot.HasWeapon, Is.True);
    }

    [Test]
    public void Refresh_HidesWeapon_WhenAnotherSlotIsSelected()
    {
        EquipBothWeapons();
        var rangedRenderer = CreateMount(EquipmentSlotType.RangedWeapon);

        // 两把都装备时当前武器是近战，远程那只手不应该显示枪。
        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MeleeWeapon));
        Assert.That(rangedRenderer.enabled, Is.False);
    }

    [Test]
    public void Refresh_ShowsWeapon_OnSelectedSlotOnly()
    {
        EquipBothWeapons();
        var meleeRenderer = CreateMount(EquipmentSlotType.MeleeWeapon);
        var rangedRenderer = CreateMount(EquipmentSlotType.RangedWeapon);

        Assert.That(meleeRenderer.enabled, Is.True);
        Assert.That(meleeRenderer.sprite, Is.Not.Null);
        Assert.That(rangedRenderer.enabled, Is.False);
    }

    [Test]
    public void SwitchingWeapon_SwapsWhichMountShowsItsWeapon()
    {
        EquipBothWeapons();
        var meleeRenderer = CreateMount(EquipmentSlotType.MeleeWeapon);
        var rangedRenderer = CreateMount(EquipmentSlotType.RangedWeapon);

        selector.Toggle();

        Assert.That(meleeRenderer.enabled, Is.False);
        Assert.That(rangedRenderer.enabled, Is.True);
    }

    [Test]
    public void UnequippingWeapon_HidesItsMount()
    {
        EquipBothWeapons();
        var meleeRenderer = CreateMount(EquipmentSlotType.MeleeWeapon);

        holder.Model.TryUnequipToBag(EquipmentSlotType.MeleeWeapon, 19);

        Assert.That(meleeRenderer.enabled, Is.False);
    }

    private void WireRenderer()
    {
        var serialized = new SerializedObject(slot);
        serialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>装备上刀和枪两种武器（等价于玩家在背包里把两把都装上）。</summary>
    private void EquipBothWeapons()
    {
        var contextRoot = new GameObject("PlayerContext");
        spawned.Add(contextRoot);
        holder = contextRoot.AddComponent<InventoryHolder>();

        var selectorRoot = new GameObject("Selector");
        spawned.Add(selectorRoot);
        selector = selectorRoot.AddComponent<PlayerWeaponSelector>();

        var serialized = new SerializedObject(selector);
        serialized.FindProperty("holder").objectReferenceValue = holder;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditModeLifecycle.Invoke(selector, "OnEnable");

        sword = ItemDefinitionFactory.Create(null, sprite, 0.4f, EquipmentSlotType.MeleeWeapon);
        rifle = ItemDefinitionFactory.Create(null, sprite, 0.4f, EquipmentSlotType.RangedWeapon);

        Equip(sword);
        Equip(rifle);
    }

    /// <summary>建一只手的挂点，返回它的武器渲染器；OnEnable 里才会做订阅与首次刷新。</summary>
    private SpriteRenderer CreateMount(EquipmentSlotType mountedSlot)
    {
        var mountRoot = new GameObject(mountedSlot.ToString());
        spawned.Add(mountRoot);
        var mount = mountRoot.AddComponent<WeaponMountSlot>();

        var weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(mountRoot.transform);
        var renderer = weaponGo.AddComponent<SpriteRenderer>();

        var serialized = new SerializedObject(mount);
        serialized.FindProperty("weaponRenderer").objectReferenceValue = renderer;
        serialized.FindProperty("holder").objectReferenceValue = holder;
        serialized.FindProperty("selector").objectReferenceValue = selector;
        serialized.FindProperty("mountedSlot").enumValueIndex = (int)mountedSlot;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditModeLifecycle.Invoke(mount, "OnEnable");
        return renderer;
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
}
