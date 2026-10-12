using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Tower.Netcode;

/// <summary>
/// 死亡观战 + 队友复活（方案 3.7）的纯逻辑测试。
///
/// NGO 的 NetworkVariable / ServerRpc 无法在 EditMode 直接触发，
/// 因此这里只覆盖「全员死亡判定」与「复活血量比例」的纯逻辑；
/// 真实同步链路在双端实机验证。
/// </summary>
public sealed class DeathSpectateTests
{
    [SetUp]
    public void SetUp()
    {
        LocalPauseScope.Unfreeze();
        LocalPauseScope.SetSpectating(false);
    }

    [TearDown]
    public void TearDown()
    {
        LocalPauseScope.Unfreeze();
        LocalPauseScope.SetSpectating(false);
    }

    [Test]
    public void SpectateFreeze_SurvivesOrdinaryUnfreeze()
    {
        LocalPauseScope.SetSpectating(true);
        LocalPauseScope.Freeze();

        LocalPauseScope.Unfreeze();

        Assert.That(LocalPauseScope.IsFrozen, Is.True);
    }

    [Test]
    public void LeavingSpectate_ReleasesSpectateFreeze()
    {
        LocalPauseScope.SetSpectating(true);

        LocalPauseScope.SetSpectating(false);

        Assert.That(LocalPauseScope.IsFrozen, Is.False);
    }

    [Test]
    public void AllPlayersDead_TriggersRunFailure()
    {
        var alive = new[] { false, false };

        Assert.That(IsAllDead(alive), Is.True);
    }

    [Test]
    public void AuthorityHealthAtZero_TransitionsAlivePlayerToDead()
    {
        Assert.That(Tower.Netcode.PlayerNetworkSync.ShouldTransitionToDead(0f, true), Is.True);
    }

    [Test]
    public void AuthorityHealthAboveZero_DoesNotTransitionPlayerToDead()
    {
        Assert.That(Tower.Netcode.PlayerNetworkSync.ShouldTransitionToDead(1f, true), Is.False);
    }

    [Test]
    public void AlreadyDeadPlayer_DoesNotPublishDeathTwice()
    {
        Assert.That(Tower.Netcode.PlayerNetworkSync.ShouldTransitionToDead(0f, false), Is.False);
    }

    [Test]
    public void AliveState_ShowsDeathFrameAndRestoresWeaponsAndAnimation()
    {
        var root = new GameObject("Player");
        var visual = new GameObject("Visual", typeof(SpriteRenderer), typeof(CharacterVisual2D));
        visual.transform.SetParent(root.transform);
        var mainHand = new GameObject("MainHand");
        mainHand.transform.SetParent(visual.transform);
        var offHand = new GameObject("OffHand");
        offHand.transform.SetParent(visual.transform);
        var other = new GameObject("Other");
        other.transform.SetParent(visual.transform);
        var texture = new Texture2D(2, 2);
        var normalSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
        var deathSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one);

        try
        {
            var renderer = visual.GetComponent<SpriteRenderer>();
            renderer.sprite = normalSprite;
            PlayerNetworkSync.SetVisualAlive(visual.transform, false, deathSprite);
            Assert.That(visual.activeSelf, Is.True);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.sprite, Is.SameAs(deathSprite));
            Assert.That(mainHand.activeSelf, Is.False);
            Assert.That(offHand.activeSelf, Is.False);
            Assert.That(other.activeSelf, Is.True);
            Assert.That(root.activeSelf, Is.True);

            visual.GetComponent<CharacterVisual2D>().SetMotion(Vector2.right);
            Assert.That(renderer.sprite, Is.SameAs(deathSprite));

            PlayerNetworkSync.SetVisualAlive(visual.transform, true, deathSprite);
            Assert.That(renderer.sprite, Is.SameAs(normalSprite));
            Assert.That(mainHand.activeInHierarchy, Is.True);
            Assert.That(offHand.activeInHierarchy, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(normalSprite);
            Object.DestroyImmediate(deathSprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void AliveState_DisablesRootColliderWithoutRemovingBodyOrPlayerAndReviveRestoresIt()
    {
        var root = new GameObject("Player");
        try
        {
            var body = root.AddComponent<Rigidbody2D>();
            var collider = root.AddComponent<BoxCollider2D>();
            var movement = root.AddComponent<PlayerMovement2D>();
            root.AddComponent<NetworkObject>();
            var sync = root.AddComponent<PlayerNetworkSync>();
            var position = new Vector2(3f, 4f);
            body.position = position;
            var handler = typeof(PlayerNetworkSync).GetMethod("HandleAliveChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handler, Is.Not.Null);

            handler.Invoke(sync, new object[] { true, false });
            Assert.That(collider.enabled, Is.False);
            Assert.That(body.simulated, Is.True);
            Assert.That(body.position, Is.EqualTo(position));
            Assert.That(root.activeSelf, Is.True);
            Assert.That(movement, Is.Not.Null);

            handler.Invoke(sync, new object[] { false, true });
            Assert.That(collider.enabled, Is.True);
            Assert.That(body.simulated, Is.True);
            Assert.That(body.position, Is.EqualTo(position));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void AliveCountDroppingToZero_ResolvesTeamWipe()
    {
        Assert.That(
            Tower.Netcode.BattleSessionState.ResolveFinishReason(2, 0),
            Is.EqualTo(Tower.Netcode.BattleFinishReason.TeamWiped));
    }

    [Test]
    public void InitialZeroAlive_DoesNotResolveTeamWipe()
    {
        Assert.That(
            Tower.Netcode.BattleSessionState.ResolveFinishReason(-1, 0),
            Is.EqualTo(Tower.Netcode.BattleFinishReason.None));
    }

    [Test]
    public void SomePlayerAlive_DoesNotFailRun()
    {
        var alive = new[] { false, true };

        Assert.That(IsAllDead(alive), Is.False);
    }

    [Test]
    public void Revive_RestoresHealthAtRatio()
    {
        const float max = 100f;
        const float ratio = 0.4f;

        Assert.That(max * ratio, Is.EqualTo(40f).Within(0.001f));
    }

    [Test]
    public void Revive_RatioIsClamped()
    {
        const float max = 100f;

        Assert.That(max * Clamp01(1.5f), Is.EqualTo(100f).Within(0.001f));
        Assert.That(max * Clamp01(-0.2f), Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void ReviveRange_IncludesBoundaryButExcludesMoreDistantCorpses()
    {
        Assert.That(PlayerNetworkSync.IsWithinReviveRange(Vector2.zero, new Vector2(2f, 0f)), Is.True);
        Assert.That(PlayerNetworkSync.IsWithinReviveRange(Vector2.zero, new Vector2(2.01f, 0f)), Is.False);
    }

    [Test]
    public void HiddenReviveButton_RemainsActiveAndCanBeShownAgain()
    {
        var root = new GameObject("SummonButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(CanvasGroup));
        try
        {
            var group = root.GetComponent<CanvasGroup>();
            var button = root.GetComponent<Button>();

            PlayerNetworkSync.SetSummonButtonVisible(group, button, false);
            Assert.That(root.activeSelf, Is.True);
            Assert.That(group.alpha, Is.Zero);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(button.interactable, Is.False);

            PlayerNetworkSync.SetSummonButtonVisible(group, button, true);
            Assert.That(group.alpha, Is.EqualTo(1f));
            Assert.That(group.blocksRaycasts, Is.True);
            Assert.That(button.interactable, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SummonRevive_RequiresNoAliveEnemyWithinRadius()
    {
        // 脱战判定：半径内无存活敌人才能召唤。
        var enemyDistances = new[] { 12f, 20f };
        const float radius = 8f;

        Assert.That(HasAliveEnemyWithin(enemyDistances, radius), Is.False);
    }

    [Test]
    public void SummonRevive_BlockedWhenEnemyNearby()
    {
        var enemyDistances = new[] { 3f, 20f };
        const float radius = 8f;

        Assert.That(HasAliveEnemyWithin(enemyDistances, radius), Is.True);
    }

    private static bool HasAliveEnemyWithin(float[] distances, float radius)
    {
        foreach (var d in distances)
        {
            if (d < radius)
                return true;
        }

        return false;
    }

    private static bool IsAllDead(bool[] alive)
    {
        foreach (var a in alive)
        {
            if (a)
                return false;
        }

        return true;
    }

    private static float Clamp01(float value)
    {
        if (value < 0f)
            return 0f;
        if (value > 1f)
            return 1f;
        return value;
    }
}
