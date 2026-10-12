using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Tower.Netcode;

public class PlayerHealth2DTests
{
    [Test]
    public void HudReady_BindsLocalHealthAfterNetworkSpawn()
    {
        var player = new GameObject("Player");
        var panelObject = new GameObject("StatusPanel");
        var fillObject = new GameObject("HpFill");
        fillObject.transform.SetParent(panelObject.transform);
        var fill = fillObject.AddComponent<Image>();
        var panel = panelObject.AddComponent<StatusPanelController>();
        typeof(StatusPanelController).GetField("hpFillImage", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(panel, fill);

        try
        {
            var health = player.AddComponent<PlayerHealth2D>();
            var sync = player.AddComponent<PlayerNetworkSync>();
            health.ApplyAuthoritativeHealth(40f, 100f, false);
            Assert.That(fill.fillAmount, Is.Not.EqualTo(0.4f).Within(0.001f));

            typeof(PlayerNetworkSync).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(sync, health);
            typeof(PlayerNetworkSync).GetMethod("HandleHudReady", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sync, null);

            Assert.That(fill.fillAmount, Is.EqualTo(0.4f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void HealRequest_ServerChecksOwnerAliveCombatAndAmount()
    {
        var player = new GameObject("Player");
        try
        {
            var health = player.AddComponent<PlayerHealth2D>();
            health.ApplyAuthoritativeHealth(40f, 100f, false);
            Assert.That(PlayerNetworkSync.ShouldProcessHealRequest(2, 1, 99, 0), Is.False);
            Assert.That(PlayerNetworkSync.ShouldProcessHealRequest(1, 1, 5, 5), Is.False);
            Assert.That(PlayerNetworkSync.ShouldProcessHealRequest(1, 1, 6, 5), Is.True);
            Assert.That(PlayerNetworkSync.CanApplyHealRequest(1, 2, health, 20f), Is.False);
            Assert.That(PlayerNetworkSync.CanApplyHealRequest(1, 1, health, float.PositiveInfinity), Is.False);
            Assert.That(PlayerNetworkSync.CanApplyHealRequest(1, 1, health, 20f), Is.True);
            health.TakeDamage(1f);
            Assert.That(PlayerNetworkSync.CanApplyHealRequest(1, 1, health, 20f), Is.False);
            health.ApplyAuthoritativeHealth(100f, 100f, false);
            Assert.That(PlayerNetworkSync.CanApplyHealRequest(1, 1, health, 20f), Is.False);
        }
        finally { Object.DestroyImmediate(player); }
    }

    private sealed class BagTargetStub : MonoBehaviour, IConsumableTarget, ILocalConsumableTarget
    {
        public bool Local;
        public bool IsLocalConsumableTarget => Local;
        public bool IsDead => false;
        public bool IsInCombatLock => false;
        public float MissingHealth() => 50f;
        public float Heal(float amount) => amount;
        public int RepairArmor(int amount) => 0;
        public int RepairArmorSlot(EquipmentSlotType slot, int amount) => 0;
        public int MissingDurabilityAt(EquipmentSlotType slot) => 0;
        public ItemRarity RarityAt(EquipmentSlotType slot) => ItemRarity.Common;
    }

    [Test]
    public void BagTarget_DoesNotUseConfiguredRemotePlayer()
    {
        var remote = new GameObject("Remote");
        var local = new GameObject("Local");
        var bagObject = new GameObject("Bag");
        try
        {
            var wrong = remote.AddComponent<BagTargetStub>();
            var correct = local.AddComponent<BagTargetStub>();
            correct.Local = true;
            var bag = bagObject.AddComponent<BagPanelController>();
            typeof(BagPanelController).GetField("consumableTargetBehaviour", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bag, wrong);
            Assert.That(bag.Target, Is.SameAs(correct));
        }
        finally
        {
            Object.DestroyImmediate(bagObject);
            Object.DestroyImmediate(local);
            Object.DestroyImmediate(remote);
        }
    }

    [Test]
    public void LocalConsumableTarget_RejectsRemoteNetworkCopy()
    {
        var player = new GameObject("Player");
        try
        {
            var health = player.AddComponent<PlayerHealth2D>();
            Assert.That(health.IsLocalConsumableTarget, Is.True);
            player.AddComponent<PlayerNetworkSync>();
            Assert.That(PlayerNetworkSync.IsLocalConsumableOwner(true, false, false), Is.False);
            Assert.That(PlayerNetworkSync.IsLocalConsumableOwner(true, true, false), Is.True);
            Assert.That(PlayerNetworkSync.IsLocalConsumableOwner(false, false, false), Is.True);
        }
        finally { Object.DestroyImmediate(player); }
    }

    [Test]
    public void LocalConsumableTarget_OnlineRequiresSpawnedOwner()
    {
        Assert.That(PlayerNetworkSync.IsLocalConsumableOwner(false, false, true), Is.False);
        Assert.That(PlayerNetworkSync.IsLocalConsumableOwner(false, true, true), Is.False);
        Assert.That(PlayerNetworkSync.IsLocalConsumableOwner(true, false, true), Is.False);
        Assert.That(PlayerNetworkSync.IsLocalConsumableOwner(true, true, true), Is.True);
    }

    [Test]
    public void TakeDamage_FiresDiedExactlyOnce()
    {
        var player = new GameObject("Player");
        var health = player.AddComponent<PlayerHealth2D>();
        var count = 0;
        health.Died += () => count++;

        health.TakeDamage(150f);
        health.TakeDamage(50f);

        Assert.That(health.IsDead, Is.True);
        Assert.That(count, Is.EqualTo(1));
        Object.DestroyImmediate(player);
    }

    [Test]
    public void TakeDamage_DoesNotFireDiedWhenSurviving()
    {
        var player = new GameObject("Player");
        var health = player.AddComponent<PlayerHealth2D>();
        var count = 0;
        health.Died += () => count++;

        health.TakeDamage(30f);

        Assert.That(health.IsDead, Is.False);
        Assert.That(count, Is.Zero);
        Object.DestroyImmediate(player);
    }

    [Test]
    public void TakeDamage_RaisesDamagedWithActualAmount()
    {
        var player = new GameObject("Player");
        var health = player.AddComponent<PlayerHealth2D>();
        var reported = 0f;
        health.Damaged += amount => reported += amount;

        health.TakeDamage(30f);

        Assert.That(reported, Is.EqualTo(30f).Within(0.001f));
        Object.DestroyImmediate(player);
    }

    [Test]
    public void TakeDamage_DoesNotRaiseDamagedAfterDeath()
    {
        var player = new GameObject("Player");
        var health = player.AddComponent<PlayerHealth2D>();
        var count = 0;
        health.Damaged += _ => count++;

        health.TakeDamage(150f);
        health.TakeDamage(50f);

        // 只有第一次致命伤广播，死亡后的补刀不再广播。
        Assert.That(count, Is.EqualTo(1));
        Object.DestroyImmediate(player);
    }

    [Test]
    public void TakeDamage_RaisesArmorChangedWithoutReachingIntoGameSession()
    {
        var player = new GameObject("Player");
        var health = player.AddComponent<PlayerHealth2D>();
        var count = 0;
        health.ArmorChanged += () => count++;

        health.TakeDamage(10f);

        Assert.That(count, Is.EqualTo(1));
        Object.DestroyImmediate(player);
    }

    [Test]
    public void TakeDamage_DoesNotRaiseDamagedWhenAmountIsZero()
    {
        var player = new GameObject("Player");
        var health = player.AddComponent<PlayerHealth2D>();
        var count = 0;
        health.Damaged += _ => count++;

        health.TakeDamage(0f);

        Assert.That(count, Is.Zero);
        Object.DestroyImmediate(player);
    }

    [Test]
    public void CharacterVisual_FlashHitTintsRedAndExpires()
    {
        var go = new GameObject("Visual");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        var visual = go.AddComponent<CharacterVisual2D>();

        // Awake 记录本色后，受击应把颜色染成红色（本色是白，染白等于没效果）。
        visual.FlashHit();
        Assert.That(visual.IsHitFlashing, Is.True);
        Assert.That(renderer.color.r, Is.EqualTo(1f).Within(0.001f));
        Assert.That(renderer.color.g, Is.LessThan(0.5f));

        // 推进超过闪烁时长后应还原为本色。
        visual.AdvanceHitFlash(1f);
        Assert.That(visual.IsHitFlashing, Is.False);
        Assert.That(renderer.color.r, Is.EqualTo(0.9f).Within(0.001f));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void TakeDamage_IsIgnoredWhileHitFlashing()
    {
        var player = new GameObject("Player");
        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(player.transform);
        visualGo.AddComponent<SpriteRenderer>();
        var visual = visualGo.AddComponent<CharacterVisual2D>();
        var health = player.AddComponent<PlayerHealth2D>();

        // 第一次受击进入闪烁（免疫窗口）。
        health.TakeDamage(10f);
        var afterFirst = health.CurrentHealth;

        // 闪烁期间再受击应被完全忽略。
        health.TakeDamage(10f);
        Assert.That(health.CurrentHealth, Is.EqualTo(afterFirst).Within(0.001f));

        // 闪烁结束后恢复可受伤。
        visual.AdvanceHitFlash(1f);
        health.TakeDamage(10f);
        Assert.That(health.CurrentHealth, Is.EqualTo(afterFirst - 10f).Within(0.001f));

        Object.DestroyImmediate(player);
    }

    [Test]
    public void DamageVignette_FlashRaisesAlphaAndFadesOut()
    {
        var go = new GameObject("Vignette");
        var vignette = go.AddComponent<DamageVignetteController>();

        Assert.That(vignette.CurrentAlpha, Is.Zero);

        vignette.Flash();
        Assert.That(vignette.CurrentAlpha, Is.GreaterThan(0f));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void ArmorPiece_ReducesDamageWhileDurable()
    {
        var piece = new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.25f, durability: 10));

        Assert.That(piece.EffectiveDamageReduction, Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(piece.IsBroken, Is.False);
    }

    [Test]
    public void ArmorPiece_StopsReducingWhenDurabilityDepleted()
    {
        var piece = new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.25f, durability: 2));

        piece.ConsumeDurability();
        piece.ConsumeDurability();

        Assert.That(piece.CurrentDurability, Is.Zero);
        Assert.That(piece.IsBroken, Is.True);
        Assert.That(piece.EffectiveDamageReduction, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void ArmorPiece_DurabilityDoesNotGoBelowZero()
    {
        var piece = new ArmorPiece(EquipmentSlotType.Helmet, TestItems.ArmorInstance(EquipmentSlotType.Helmet, damageReduction: 0.1f, durability: 1));

        piece.ConsumeDurability();
        piece.ConsumeDurability();
        piece.ConsumeDurability();

        Assert.That(piece.CurrentDurability, Is.Zero);
    }

    [Test]
    public void PlayerArmor_StacksReductionDiminishingly()
    {
        var armor = new PlayerArmor();
        armor.Rebuild(new[]
        {
            new ArmorPiece(EquipmentSlotType.Helmet, TestItems.ArmorInstance(EquipmentSlotType.Helmet, damageReduction: 0.2f, durability: 10)),
            new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.2f, durability: 10)),
            new ArmorPiece(EquipmentSlotType.Boots, TestItems.ArmorInstance(EquipmentSlotType.Boots, damageReduction: 0.2f, durability: 10)),
        });

        // 1 - (0.8 * 0.8 * 0.8) = 0.488
        Assert.That(armor.DamageReduction, Is.EqualTo(0.488f).Within(0.001f));
    }

    [Test]
    public void PlayerArmor_AbsorbAppliesReductionAndConsumesDurability()
    {
        var armor = new PlayerArmor();
        armor.Rebuild(new[] { new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.25f, durability: 10)) });

        var taken = armor.Absorb(40f);

        Assert.That(taken, Is.EqualTo(30f).Within(0.001f));
        Assert.That(armor.CurrentDurability, Is.EqualTo(9));
    }

    [Test]
    public void PlayerArmor_ConsumesDurabilityOnEveryPiece()
    {
        var armor = new PlayerArmor();
        armor.Rebuild(new[]
        {
            new ArmorPiece(EquipmentSlotType.Helmet, TestItems.ArmorInstance(EquipmentSlotType.Helmet, damageReduction: 0.1f, durability: 5)),
            new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.1f, durability: 5)),
        });

        armor.Absorb(10f);

        Assert.That(armor.CurrentDurability, Is.EqualTo(8));
    }

    [Test]
    public void PlayerArmor_BrokenPieceStopsReducingButOthersStillWork()
    {
        var armor = new PlayerArmor();
        armor.Rebuild(new[]
        {
            new ArmorPiece(EquipmentSlotType.Helmet, TestItems.ArmorInstance(EquipmentSlotType.Helmet, damageReduction: 0.5f, durability: 1)),
            new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.2f, durability: 10)),
        });

        // 第一次受击：头盔耐久归零，但本次仍按 1-(0.5*0.8)=0.6 结算。
        var first = armor.Absorb(100f);
        Assert.That(first, Is.EqualTo(40f).Within(0.001f));

        // 第二次受击：头盔已失效，只剩胸甲的 20%。
        var second = armor.Absorb(100f);
        Assert.That(second, Is.EqualTo(80f).Within(0.001f));
    }

    [Test]
    public void PlayerArmor_NoArmorMeansNoReduction()
    {
        var armor = new PlayerArmor();
        armor.Rebuild(null);

        Assert.That(armor.DamageReduction, Is.EqualTo(0f).Within(0.001f));
        Assert.That(armor.Absorb(30f), Is.EqualTo(30f).Within(0.001f));
    }

    [Test]
    public void PlayerArmor_RebuildKeepsDurabilityForSameInstance()
    {
        var armor = new PlayerArmor();
        var instance = TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.2f, durability: 10);
        armor.Rebuild(new[] { new ArmorPiece(EquipmentSlotType.BodyArmor, instance) });
        armor.Absorb(10f);
        armor.Absorb(10f);

        // 耐久挂在实例上，重建护甲组（同一实例）后剩余耐久自然保留。
        armor.Rebuild(new[] { new ArmorPiece(EquipmentSlotType.BodyArmor, instance) });

        Assert.That(armor.CurrentDurability, Is.EqualTo(8));
    }

    [Test]
    public void PlayerArmor_RebuildUsesNewInstanceDurabilityWhenPieceSwapped()
    {
        var armor = new PlayerArmor();
        armor.Rebuild(new[] { new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.2f, durability: 10)) });
        armor.Absorb(10f);

        // 换了一件新实例 → 读的是新实例自己的耐久（满耐久）。
        armor.Rebuild(new[] { new ArmorPiece(EquipmentSlotType.BodyArmor, TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, damageReduction: 0.3f, durability: 20)) });

        Assert.That(armor.CurrentDurability, Is.EqualTo(20));
    }

    [Test]
    public void SetRemoteCopyVisual_MarksCharacterVisualOnPlayer()
    {
        var root = new GameObject("Player");
        var visual = new GameObject("Visual", typeof(SpriteRenderer), typeof(CharacterVisual2D));
        visual.transform.SetParent(root.transform);

        try
        {
            PlayerNetworkSync.SetRemoteCopyVisual(visual.transform, true);
            Assert.That(visual.GetComponent<CharacterVisual2D>().IsRemoteCopy, Is.True);

            PlayerNetworkSync.SetRemoteCopyVisual(visual.transform, false);
            Assert.That(visual.GetComponent<CharacterVisual2D>().IsRemoteCopy, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RemoteCopyVisual_KeepsHitFlashRunningWhileLocalPlayerFrozen()
    {
        // 复现：主机玩家死亡进入观战（本机冻结）后，远端玩家的受击闪烁被卡住，
        // 主机端 IsInvulnerable 恒真 → 远端玩家永久免疫 → 血条不再变化、不触发死亡。
        LocalPauseScope.SetSpectating(true);
        var player = new GameObject("RemoteCopyPlayer");
        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(player.transform);
        visualGo.AddComponent<SpriteRenderer>();
        var visual = visualGo.AddComponent<CharacterVisual2D>();
        var health = player.AddComponent<PlayerHealth2D>();

        try
        {
            PlayerNetworkSync.SetRemoteCopyVisual(visualGo.transform, true);

            health.TakeDamage(10f);
            visual.AdvanceHitFlash(1f);

            // 远端副本的闪烁计时必须照常推进：它是主机权威无敌帧的来源。
            Assert.That(visual.IsHitFlashing, Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(90f).Within(0.001f));

            // 闪烁结束后第二击必须正常扣血。
            health.TakeDamage(10f);
            Assert.That(health.CurrentHealth, Is.EqualTo(80f).Within(0.001f));
        }
        finally
        {
            LocalPauseScope.SetSpectating(false);
            Object.DestroyImmediate(player);
        }
    }
}
