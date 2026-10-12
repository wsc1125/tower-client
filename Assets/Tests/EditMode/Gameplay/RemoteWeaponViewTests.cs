using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 远端玩家武器视图与客户端索敌的纯逻辑测试。
///
/// NGO 的 NetworkVariable / ServerRpc 无法在 EditMode 直接触发，
/// 因此这里只覆盖「远端挂点落地显示」与「同步死亡目标跳过」的纯逻辑；
/// 真实同步链路在双端实机验证。
/// </summary>
public sealed class RemoteWeaponViewTests
{
    [TearDown]
    public void TearDown()
    {
        foreach (var mount in Object.FindObjectsOfType<WeaponMountSlot>(true))
            Object.DestroyImmediate(mount.gameObject);
    }

    [Test]
    public void ApplyRemoteState_ShowsSpriteWhenSelected()
    {
        var mount = CreateMount(out var renderer);
        var definition = CreateDefinition("Rifle", true);

        mount.ApplyRemoteState(definition, true);

        Assert.That(renderer.sprite, Is.EqualTo(definition.WorldSprite));
        Assert.That(renderer.enabled, Is.True);
    }

    [Test]
    public void ApplyRemoteState_HidesSpriteWhenNotSelected()
    {
        var mount = CreateMount(out var renderer);
        var definition = CreateDefinition("Rifle", true);

        mount.ApplyRemoteState(definition, false);

        Assert.That(renderer.enabled, Is.False);
    }

    [Test]
    public void ApplyRemoteState_HidesWhenNoWeapon()
    {
        var mount = CreateMount(out var renderer);

        mount.ApplyRemoteState(null, true);

        Assert.That(renderer.sprite, Is.Null);
        Assert.That(renderer.enabled, Is.False);
    }

    [Test]
    public void RemoteDrivenMount_IgnoresLocalRefresh()
    {
        var mount = CreateMount(out var renderer);
        var definition = CreateDefinition("Rifle", true);
        mount.ApplyRemoteState(definition, true);

        mount.SetRemoteDriven(true);
        mount.RefreshFromLocal();

        Assert.That(renderer.sprite, Is.EqualTo(definition.WorldSprite));
        Assert.That(renderer.enabled, Is.True);
    }

    [Test]
    public void NetworkPuppet_KeepsEnemyRegisteredAsTarget()
    {
        WorldRegistry.ClearForTests();
        var enemyObject = new GameObject("Enemy");
        enemyObject.AddComponent<Rigidbody2D>();
        var brain = enemyObject.AddComponent<EnemyBrain2D>();

        brain.NetworkPuppet = true;

        Assert.That(WorldRegistry.Damageables, Does.Contain(brain));
        Object.DestroyImmediate(enemyObject);
        WorldRegistry.ClearForTests();
    }

    [Test]
    public void NetworkPuppet_KeepsEnemyRegisteredEvenWhenDisabled()
    {
        WorldRegistry.ClearForTests();
        var enemyObject = new GameObject("Enemy");
        enemyObject.AddComponent<Rigidbody2D>();
        var brain = enemyObject.AddComponent<EnemyBrain2D>();

        // 傀儡门方案：不依赖 enabled，注册状态与 AI 开关解耦。
        brain.NetworkPuppet = true;
        brain.enabled = false;

        Assert.That(WorldRegistry.Damageables, Does.Contain(brain));
        Object.DestroyImmediate(enemyObject);
        WorldRegistry.ClearForTests();
    }

    [Test]
    public void ApplyRemoteState_ScalesSpriteToTargetWidth()
    {
        var mount = CreateMount(out var renderer);
        var definition = CreateDefinition("Rifle", true);

        mount.ApplyRemoteState(definition, true);

        Assert.That(renderer.transform.localScale.x, Is.GreaterThan(0f));
    }

    private static bool ContainsDamageable(EnemyBrain2D brain)
    {
        foreach (var target in WorldRegistry.Damageables)
        {
            if (ReferenceEquals(target, brain))
                return true;
        }

        return false;
    }

    private static WeaponMountSlot CreateMount(out SpriteRenderer renderer)
    {
        var root = new GameObject("Mount");
        var weaponObject = new GameObject("Weapon");
        weaponObject.transform.SetParent(root.transform);
        renderer = weaponObject.AddComponent<SpriteRenderer>();
        var mount = root.AddComponent<WeaponMountSlot>();
        mount.BindRendererForTests(renderer);
        return mount;
    }

    private static ItemDefinition CreateDefinition(string id, bool withSprite)
    {
        var definition = ScriptableObject.CreateInstance<ItemDefinition>();
        definition.name = id;
        if (withSprite)
        {
            var texture = new Texture2D(4, 4);
            definition.SetWorldSpriteForTests(
                Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f)));
        }

        return definition;
    }
}
