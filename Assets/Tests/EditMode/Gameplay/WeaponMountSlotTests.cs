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

    private PlayerWeaponSelector selector;
    private ItemInstance sword;
    private ItemInstance rifle;

    [SetUp]
    public void SetUp()
    {
        GameSession.ResetForTests();
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

        if (sword != null)
            Object.DestroyImmediate(sword.Definition);
        if (rifle != null)
            Object.DestroyImmediate(rifle.Definition);

        GameSession.ResetForTests();
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
        var offHandRenderer = CreateMount(EquipmentSlotType.OffHand);

        // 两把都装备时当前武器是主手，副手那只手不应该显示武器。
        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.MainHand));
        Assert.That(offHandRenderer.enabled, Is.False);
    }

    [Test]
    public void Refresh_ShowsWeapon_OnSelectedSlotOnly()
    {
        EquipBothWeapons();
        var mainHandRenderer = CreateMount(EquipmentSlotType.MainHand);
        var offHandRenderer = CreateMount(EquipmentSlotType.OffHand);

        Assert.That(mainHandRenderer.enabled, Is.True);
        Assert.That(mainHandRenderer.sprite, Is.Not.Null);
        Assert.That(offHandRenderer.enabled, Is.False);
    }

    [Test]
    public void SwitchingWeapon_SwapsWhichMountShowsItsWeapon()
    {
        EquipBothWeapons();
        var mainHandRenderer = CreateMount(EquipmentSlotType.MainHand);
        var offHandRenderer = CreateMount(EquipmentSlotType.OffHand);

        selector.Toggle();

        Assert.That(mainHandRenderer.enabled, Is.False);
        Assert.That(offHandRenderer.enabled, Is.True);
    }

    [Test]
    public void UnequippingWeapon_HidesItsMount()
    {
        EquipBothWeapons();
        var mainHandRenderer = CreateMount(EquipmentSlotType.MainHand);

        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, null);
        GameSession.Raid.NotifyChanged();

        Assert.That(mainHandRenderer.enabled, Is.False);
    }

    /// <summary>
    /// 挂点直接读 <see cref="GameSession.Raid"/>，因此不依赖 prefab 上的任何引用接线，
    /// 不会出现「漏接 holder 导致永远不显示武器」。
    /// </summary>
    [Test]
    public void Refresh_ReadsRaidEquipmentWithoutAnyWiring()
    {
        EquipBothWeapons();
        var offHandRenderer = CreateMount(EquipmentSlotType.OffHand);

        selector.Toggle();

        Assert.That(selector.ActiveSlot, Is.EqualTo(EquipmentSlotType.OffHand));
        Assert.That(offHandRenderer.enabled, Is.True);
        Assert.That(offHandRenderer.sprite, Is.Not.Null);
    }

    private void WireRenderer()
    {
        var serialized = new SerializedObject(slot);
        serialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>装备上刀和枪两种武器（等价于玩家在局内把两把都装上）。</summary>
    private void EquipBothWeapons()
    {
        var selectorRoot = new GameObject("Selector");
        spawned.Add(selectorRoot);
        selector = selectorRoot.AddComponent<PlayerWeaponSelector>();
        EditModeLifecycle.Invoke(selector, "OnEnable");

        sword = ItemInstance.Create(ItemDefinitionFactory.Create(null, sprite, 0.4f, EquipmentSlotType.MainHand));
        rifle = ItemInstance.Create(ItemDefinitionFactory.Create(null, sprite, 0.4f, EquipmentSlotType.OffHand));

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
        serialized.FindProperty("selector").objectReferenceValue = selector;
        serialized.FindProperty("mountedSlot").enumValueIndex = (int)mountedSlot;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditModeLifecycle.Invoke(mount, "OnEnable");
        return renderer;
    }

    /// <summary>把武器装到「主手优先」的第一个空武器槽，模拟玩家在局内装备。</summary>
    private static void Equip(ItemInstance item)
    {
        foreach (var slot in EquipmentSlots.WeaponSlots)
        {
            if (GameSession.Loadout.Equipment.Get(slot) != null)
                continue;

            GameSession.Loadout.Equipment.Set(slot, item);
            GameSession.Raid.NotifyChanged();
            return;
        }
    }
}
