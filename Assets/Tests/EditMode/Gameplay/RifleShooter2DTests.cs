using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class RifleShooter2DTests
{
    private GameObject root;
    private GameObject buttonRoot;
    private RifleShooter2D shooter;
    private Transform pivot;
    private Transform weaponPivot;
    private Transform muzzle;
    private WeaponMountSlot mountSlot;
    private HoldButton attackButton;
    private SpriteRenderer weaponRenderer;
    private Texture2D texture;
    private Sprite sprite;

    [SetUp]
    public void SetUp()
    {
        GameSession.ResetForTests();
        texture = new Texture2D(4, 4);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

        root = new GameObject("Character");

        var pivotGo = new GameObject("RangedHand");
        pivotGo.transform.SetParent(root.transform);
        pivot = pivotGo.transform;
        mountSlot = pivotGo.AddComponent<WeaponMountSlot>();

        // 武器渲染器挂在挂点下，枪口是它的子物体（与真实 prefab 层级一致）。
        var weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(pivot);
        weaponPivot = weaponGo.transform;
        weaponRenderer = weaponGo.AddComponent<SpriteRenderer>();

        var muzzleGo = new GameObject("Muzzle");
        muzzleGo.transform.SetParent(weaponPivot);
        muzzle = muzzleGo.transform;

        buttonRoot = new GameObject("AttackButton");
        attackButton = buttonRoot.AddComponent<HoldButton>();

        shooter = root.AddComponent<RifleShooter2D>();

        var shooterSerialized = new SerializedObject(shooter);
        shooterSerialized.FindProperty("mainHandSlot").objectReferenceValue = mountSlot;
        shooterSerialized.FindProperty("attackButton").objectReferenceValue = attackButton;
        shooterSerialized.ApplyModifiedPropertiesWithoutUndo();

        var slotSerialized = new SerializedObject(mountSlot);
        slotSerialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
        slotSerialized.FindProperty("aimPivot").objectReferenceValue = pivot;
        slotSerialized.FindProperty("muzzle").objectReferenceValue = muzzle;
        slotSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        // 清掉本用例生成的弹丸，避免残留对象污染下一个用例的 FindObjectsOfType 计数。
        // 注意：DestroyImmediate 对带 Rigidbody2D 的对象在 EditMode 下可能延迟生效，
        // 因此这里循环清理直到确实清空。
        for (var guard = 0; guard < 5; guard++)
        {
            var leftovers = Object.FindObjectsOfType<Projectile2D>();
            if (leftovers.Length == 0)
                break;

            foreach (var projectile in leftovers)
                Object.DestroyImmediate(projectile.gameObject);
        }

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(buttonRoot);
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);
        GameSession.ResetForTests();
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
    public void Step_AfterWeaponLeavesHand_StopsRecoil()
    {
        GiveWeapon();
        shooter.Fire();
        shooter.Step(0.016f);
        Assert.That(shooter.IsRecoiling, Is.True, "持枪开火后应处于后坐状态");

        // 切成近战：枪离开手上
        weaponRenderer.enabled = false;

        shooter.Step(0.016f);

        Assert.That(shooter.IsRecoiling, Is.False);
    }

    private void GiveWeapon()
    {
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = true;
    }

    /// <summary>
    /// 霰弹枪（多发 + 散射）一次开火应生成 6 发弹丸，且方向在散射角内均匀铺开。
    /// 单发武器（步枪等）仍只生成 1 发。
    /// </summary>
    [Test]
    public void Fire_WithMultiShotWeapon_SpawnsSpreadProjectiles()
    {
        var shotgun = CreateWeapon(projectilesPerShot: 6, spreadAngle: 22f);
        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, shotgun);
        GameSession.Raid.NotifyChanged();
        GiveWeapon();

        var prefabGo = new GameObject("ProjectilePrefab");
        var prefab = prefabGo.AddComponent<Projectile2D>();
        var shooterSerialized = new SerializedObject(shooter);
        shooterSerialized.FindProperty("projectilePrefab").objectReferenceValue = prefab;
        shooterSerialized.ApplyModifiedPropertiesWithoutUndo();

        shooter.Fire();

        var angles = new System.Collections.Generic.List<float>();
        foreach (var projectile in Object.FindObjectsOfType<Projectile2D>())
        {
            if (projectile == prefab)
                continue;

            var angle = Vector2.Angle(Vector2.right, projectile.Direction);
            if (projectile.Direction.y < 0f)
                angle = -angle;

            angles.Add(angle);
        }

        Assert.That(angles.Count, Is.EqualTo(6), "霰弹枪一次开火应生成 6 发弹丸");

        // FindObjectsOfType 返回顺序不确定，先排序再逐发比对。
        // 6 发应均匀铺满 ±11°（22° 总散射角），首尾各占一半角度。
        angles.Sort();
        var expected = new[] { -11f, -6.6f, -2.2f, 2.2f, 6.6f, 11f };
        for (var i = 0; i < angles.Count; i++)
            Assert.That(angles[i], Is.EqualTo(expected[i]).Within(0.1f), "第 " + i + " 发弹丸角度不符");

        Object.DestroyImmediate(prefabGo);
    }

    /// <summary>单发武器（未配置散射）一次开火只生成 1 发弹丸，且沿枪口方向。</summary>
    [Test]
    public void Fire_WithSingleShotWeapon_SpawnsOneProjectile()
    {
        var rifle = CreateWeapon(projectilesPerShot: 1, spreadAngle: 0f);
        GameSession.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);
        GameSession.Raid.NotifyChanged();
        GiveWeapon();

        var prefabGo = new GameObject("ProjectilePrefab");
        var prefab = prefabGo.AddComponent<Projectile2D>();
        var shooterSerialized = new SerializedObject(shooter);
        shooterSerialized.FindProperty("projectilePrefab").objectReferenceValue = prefab;
        shooterSerialized.ApplyModifiedPropertiesWithoutUndo();

        var before = Object.FindObjectsOfType<Projectile2D>().Length;

        shooter.Fire();

        var after = Object.FindObjectsOfType<Projectile2D>().Length;

        Assert.That(after - before, Is.EqualTo(1), "单发武器一次开火只应生成 1 发弹丸");

        Object.DestroyImmediate(prefabGo);
    }

    /// <summary>
    /// 瞄准方向必须落到枪械挂点上：枪口朝向与瞄准方向一致；
    /// 朝左（|角度|&gt;90°）时枪身贴图垂直翻转，避免倒扣。
    /// </summary>
    [Test]
    public void AimAt_RotatesMuzzleTowardAimDirection()
    {
        GiveWeapon();

        shooter.AimAt(Vector2.up);

        Assert.That(muzzle.right.x, Is.EqualTo(0f).Within(0.001f), "朝上瞄准时枪口 x 分量应为 0");
        Assert.That(muzzle.right.y, Is.EqualTo(1f).Within(0.001f), "朝上瞄准时枪口应指向上方");
        Assert.That(weaponRenderer.flipY, Is.False, "朝右半平面瞄准时不翻转");

        shooter.AimAt(Vector2.left);

        Assert.That(muzzle.right.x, Is.EqualTo(-1f).Within(0.001f), "朝左瞄准时枪口应指向左方");
        Assert.That(muzzle.right.y, Is.EqualTo(0f).Within(0.001f), "朝左瞄准时枪口 y 分量应为 0");
        Assert.That(weaponRenderer.flipY, Is.True, "朝左瞄准时应垂直翻转枪身贴图");
    }

    /// <summary>
    /// 子弹必须沿枪口当前朝向飞出（而不是旧的瞄准缓存），
    /// 保证「武器朝向」与「子弹发射方向」永远一致。
    /// </summary>
    [Test]
    public void Fire_ProjectileDirectionMatchesMuzzle()
    {
        GiveWeapon();

        var prefabGo = new GameObject("ProjectilePrefab");
        var prefab = prefabGo.AddComponent<Projectile2D>();
        var shooterSerialized = new SerializedObject(shooter);
        shooterSerialized.FindProperty("projectilePrefab").objectReferenceValue = prefab;
        shooterSerialized.ApplyModifiedPropertiesWithoutUndo();

        shooter.AimAt(Vector2.up);
        shooter.Fire();

        Projectile2D spawned = null;
        foreach (var projectile in Object.FindObjectsOfType<Projectile2D>())
        {
            if (projectile != prefab)
                spawned = projectile;
        }

        Assert.That(spawned, Is.Not.Null, "开火后应生成弹丸");
        Assert.That(spawned.Direction.x, Is.EqualTo(0f).Within(0.001f), "子弹 x 分量应与枪口朝向一致");
        Assert.That(spawned.Direction.y, Is.EqualTo(1f).Within(0.001f), "子弹应沿枪口朝向（向上）飞行");

        Object.DestroyImmediate(prefabGo);
    }

    private static ItemInstance CreateWeapon(int projectilesPerShot, float spreadAngle)
    {
        var weapon = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(weapon);
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)EquipmentSlotType.MainHand;
        serialized.FindProperty("projectilesPerShot").intValue = projectilesPerShot;
        serialized.FindProperty("spreadAngle").floatValue = spreadAngle;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return ItemInstance.Create(weapon);
    }
}
