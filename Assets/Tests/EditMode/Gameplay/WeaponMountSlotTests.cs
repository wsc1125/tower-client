using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class WeaponMountSlotTests
{
    private GameObject slotRoot;
    private GameObject weaponRoot;
    private WeaponMountSlot slot;
    private SpriteRenderer weaponRenderer;
    private Texture2D texture;
    private Sprite sprite;

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

    private void WireRenderer()
    {
        var serialized = new SerializedObject(slot);
        serialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
