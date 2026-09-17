using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class RifleShooter2DTests
{
    private GameObject root;
    private GameObject buttonRoot;
    private RifleShooter2D shooter;
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

        var pivotGo = new GameObject("RangedHand");
        pivotGo.transform.SetParent(root.transform);
        pivot = pivotGo.transform;
        mountSlot = pivotGo.AddComponent<WeaponMountSlot>();

        var weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(pivot);
        weaponRenderer = weaponGo.AddComponent<SpriteRenderer>();

        buttonRoot = new GameObject("AttackButton");
        attackButton = buttonRoot.AddComponent<HoldButton>();

        shooter = root.AddComponent<RifleShooter2D>();

        var shooterSerialized = new SerializedObject(shooter);
        shooterSerialized.FindProperty("weaponPivot").objectReferenceValue = pivot;
        shooterSerialized.FindProperty("mountSlot").objectReferenceValue = mountSlot;
        shooterSerialized.FindProperty("attackButton").objectReferenceValue = attackButton;
        shooterSerialized.ApplyModifiedPropertiesWithoutUndo();

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
    public void Step_WithoutWeaponInHand_CannotFire()
    {
        attackButton.OnPointerDown(null);

        shooter.Step(0.016f);

        Assert.That(shooter.CanFire, Is.False);
        Assert.That(shooter.IsRecoiling, Is.False);
    }

    [Test]
    public void Step_AfterWeaponLeavesHand_StopsRecoilAndRestoresRestPose()
    {
        var restPosition = new Vector3(0.3f, 0.1f, 0f);
        pivot.localPosition = restPosition;
        GiveWeapon();
        shooter.Fire();
        shooter.Step(0.016f);
        Assert.That(pivot.localPosition, Is.Not.EqualTo(restPosition), "持枪时应当有后坐位移");

        // 切成近战：枪离开手上
        weaponRenderer.enabled = false;

        shooter.Step(0.016f);

        Assert.That(shooter.IsRecoiling, Is.False);
        Assert.That(pivot.localPosition, Is.EqualTo(restPosition));
    }

    private void GiveWeapon()
    {
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = true;
    }
}
