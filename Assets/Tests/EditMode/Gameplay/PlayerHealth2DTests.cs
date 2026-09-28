using NUnit.Framework;
using UnityEngine;

public class PlayerHealth2DTests
{
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
}
