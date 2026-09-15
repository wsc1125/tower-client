using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class AttackSwing2DTests
{
    private const float Duration = 0.3f;

    private GameObject root;
    private GameObject buttonRoot;
    private AttackSwing2D attack;
    private Transform pivot;
    private WeaponMountSlot mountSlot;
    private HoldButton attackButton;
    private SpriteRenderer weaponRenderer;
    private Texture2D texture;
    private Sprite sprite;

    [SetUp]
    public void SetUp()
    {
        texture = new Texture2D(4, 4);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

        root = new GameObject("Character");

        var pivotGo = new GameObject("MainHand");
        pivotGo.transform.SetParent(root.transform);
        pivot = pivotGo.transform;
        mountSlot = pivotGo.AddComponent<WeaponMountSlot>();

        var weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(pivot);
        weaponRenderer = weaponGo.AddComponent<SpriteRenderer>();

        buttonRoot = new GameObject("AttackButton");
        attackButton = buttonRoot.AddComponent<HoldButton>();

        attack = root.AddComponent<AttackSwing2D>();

        var attackSerialized = new SerializedObject(attack);
        attackSerialized.FindProperty("weaponPivot").objectReferenceValue = pivot;
        attackSerialized.FindProperty("mountSlot").objectReferenceValue = mountSlot;
        attackSerialized.FindProperty("attackButton").objectReferenceValue = attackButton;
        attackSerialized.FindProperty("duration").floatValue = Duration;
        attackSerialized.FindProperty("startAngle").floatValue = 60f;
        attackSerialized.FindProperty("endAngle").floatValue = -60f;
        attackSerialized.ApplyModifiedPropertiesWithoutUndo();

        var slotSerialized = new SerializedObject(mountSlot);
        slotSerialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
        slotSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(buttonRoot);
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);
    }

    [Test]
    public void Step_NotHeld_StaysAtRestRotation()
    {
        GiveWeapon();

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.False);
        Assert.That(Quaternion.Angle(pivot.localRotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [Test]
    public void Step_HeldWithoutWeapon_DoesNotSwing()
    {
        attackButton.OnPointerDown(null);

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.False);
    }

    [Test]
    public void Step_HeldWithWeapon_StartsAtStartAngle()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.True);
        Assert.That(pivot.localEulerAngles.z, Is.EqualTo(60f).Within(0.01f));
    }

    [Test]
    public void Step_HalfDuration_ReachesMidAngle()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);

        attack.Step(Duration * 0.5f);

        Assert.That(attack.IsSwinging, Is.True);
        Assert.That(Mathf.DeltaAngle(pivot.localEulerAngles.z, 0f), Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void Step_BeyondDuration_EndsAndRestoresRestRotation()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);

        attack.Step(Duration * 2f);

        Assert.That(attack.IsSwinging, Is.False);
        Assert.That(Quaternion.Angle(pivot.localRotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [Test]
    public void Step_StillHeldAfterFinish_RestartsSwing()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);
        attack.Step(Duration * 2f);

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.True);
        Assert.That(pivot.localEulerAngles.z, Is.EqualTo(60f).Within(0.01f));
    }

    [Test]
    public void Step_ReleasedMidSwing_FinishesCurrentSwing()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);
        attackButton.OnPointerUp(null);

        attack.Step(Duration * 2f);

        Assert.That(attack.IsSwinging, Is.False);
        Assert.That(Quaternion.Angle(pivot.localRotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [Test]
    public void Step_WithoutWiredReferences_DoesNotThrow()
    {
        var bare = new GameObject("Bare");
        var bareAttack = bare.AddComponent<AttackSwing2D>();

        Assert.DoesNotThrow(() => bareAttack.Step(0.016f));
        Assert.That(bareAttack.IsSwinging, Is.False);

        Object.DestroyImmediate(bare);
    }

    [Test]
    public void Step_PreservesRestRotationOffset()
    {
        GiveWeapon();
        pivot.localRotation = Quaternion.Euler(0f, 0f, 15f);
        attackButton.OnPointerDown(null);

        attack.Step(0.016f);

        Assert.That(Mathf.DeltaAngle(pivot.localEulerAngles.z, 75f), Is.EqualTo(0f).Within(0.01f));
    }

    private void GiveWeapon()
    {
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = true;
    }
}
