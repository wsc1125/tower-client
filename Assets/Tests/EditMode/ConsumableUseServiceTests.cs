using NUnit.Framework;

/// <summary>
/// <see cref="ConsumableUseService"/> 的纯逻辑测试。
/// 用假目标（<see cref="FakeTarget"/>）替代 PlayerHealth2D，避免依赖 MonoBehaviour 与场景。
/// </summary>
public sealed class ConsumableUseServiceTests
{
    /// <summary>测试用作用目标：血量与护甲耐久都是普通字段，可直接摆布。</summary>
    private class FakeTarget : IConsumableTarget
    {
        public float MaximumHealth = 100f;
        public float CurrentHealth = 100f;
        public bool Dead;
        public bool CombatLock;

        /// <summary>槽 → (当前耐久, 上限)。只放防具。</summary>
        public readonly System.Collections.Generic.Dictionary<EquipmentSlotType, (int Current, int Max)> Armor =
            new System.Collections.Generic.Dictionary<EquipmentSlotType, (int Current, int Max)>();

        /// <summary>槽 → 品质。未登记时按普通处理。</summary>
        public readonly System.Collections.Generic.Dictionary<EquipmentSlotType, ItemRarity> Rarities =
            new System.Collections.Generic.Dictionary<EquipmentSlotType, ItemRarity>();

        public bool IsDead => Dead;
        public bool IsInCombatLock => CombatLock;

        public float MissingHealth() => Dead ? 0f : UnityEngine.Mathf.Max(0f, MaximumHealth - CurrentHealth);

        public float Heal(float amount)
        {
            if (Dead || amount <= 0f)
                return 0f;

            var before = CurrentHealth;
            CurrentHealth = UnityEngine.Mathf.Min(MaximumHealth, CurrentHealth + amount);
            return CurrentHealth - before;
        }

        public int RepairArmor(int amount)
        {
            var remaining = amount;
            var repaired = 0;
            foreach (var slot in new System.Collections.Generic.List<EquipmentSlotType>(Armor.Keys))
            {
                if (remaining <= 0)
                    break;

                var restored = RepairArmorSlot(slot, remaining);
                remaining -= restored;
                repaired += restored;
            }

            return repaired;
        }

        public int RepairArmorSlot(EquipmentSlotType slot, int amount)
        {
            if (amount <= 0 || !Armor.TryGetValue(slot, out var entry))
                return 0;

            var restored = UnityEngine.Mathf.Min(amount, entry.Max - entry.Current);
            Armor[slot] = (entry.Current + restored, entry.Max);
            return restored;
        }

        public int MissingDurabilityAt(EquipmentSlotType slot)
        {
            return Armor.TryGetValue(slot, out var entry)
                ? UnityEngine.Mathf.Max(0, entry.Max - entry.Current)
                : 0;
        }

        public ItemRarity RarityAt(EquipmentSlotType slot)
        {
            return Rarities.TryGetValue(slot, out var rarity) ? rarity : ItemRarity.Common;
        }
    }

    // ==================== 补血路径 ====================

    [Test]
    public void CanHeal_WhenHurtAndHasPool_IsTrue()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget { CurrentHealth = 40f };

        Assert.That(ConsumableUseService.CanHeal(med, target), Is.True);
    }

    [Test]
    public void CanHeal_WhenFullHealth_IsFalse()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget { CurrentHealth = 100f };

        Assert.That(ConsumableUseService.CanHeal(med, target), Is.False);
    }

    [Test]
    public void CanHeal_WhenPoolEmpty_IsFalse()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        med.ConsumeDurability(100);
        var target = new FakeTarget { CurrentHealth = 40f };

        Assert.That(ConsumableUseService.CanHeal(med, target), Is.False);
    }

    [Test]
    public void CanHeal_WhenInCombatLock_IsFalse()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget { CurrentHealth = 40f, CombatLock = true };

        Assert.That(ConsumableUseService.CanHeal(med, target), Is.False);
    }

    [Test]
    public void CanHeal_WhenDead_IsFalse()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget { CurrentHealth = 0f, Dead = true };

        Assert.That(ConsumableUseService.CanHeal(med, target), Is.False);
    }

    [Test]
    public void CanHeal_ForRepairOnlyItem_IsFalse()
    {
        var kit = TestItems.RepairInstance(repairAmount: 30, pool: 60);
        var target = new FakeTarget { CurrentHealth = 40f };

        Assert.That(ConsumableUseService.CanHeal(kit, target), Is.False);
    }

    [Test]
    public void TryHeal_HealsAndSpendsExactlyTheHealedAmount()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget { CurrentHealth = 40f };

        var ok = ConsumableUseService.TryHeal(med, target, out var result);

        Assert.That(ok, Is.True);
        Assert.That(result.Healed, Is.EqualTo(50f).Within(0.001f));
        Assert.That(target.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
        // 耐久池按实际回血量扣减。
        Assert.That(med.CurrentDurability, Is.EqualTo(50));
        Assert.That(result.DurabilitySpent, Is.EqualTo(50));
    }

    [Test]
    public void TryHeal_WhenOnlyPartiallyNeeded_SpendsOnlyWhatWasUsed()
    {
        // 只差 10 血，血包能回 50 —— 应只回 10、只扣 10，不浪费。
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget { CurrentHealth = 90f };

        var ok = ConsumableUseService.TryHeal(med, target, out var result);

        Assert.That(ok, Is.True);
        Assert.That(result.Healed, Is.EqualTo(10f).Within(0.001f));
        Assert.That(med.CurrentDurability, Is.EqualTo(90));
    }

    [Test]
    public void TryHeal_WhenPoolSmallerThanNeed_HealsOnlyWhatPoolAllows()
    {
        // 缺 80 血，但耐久池只剩 30 —— 只能回 30。
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        med.ConsumeDurability(70);
        var target = new FakeTarget { CurrentHealth = 20f };

        var ok = ConsumableUseService.TryHeal(med, target, out var result);

        Assert.That(ok, Is.True);
        Assert.That(result.Healed, Is.EqualTo(30f).Within(0.001f));
        Assert.That(med.CurrentDurability, Is.Zero);
        Assert.That(med.IsBroken, Is.True);
    }

    [Test]
    public void TryHeal_WhenFullHealth_DoesNotSpendDurability()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget { CurrentHealth = 100f };

        var ok = ConsumableUseService.TryHeal(med, target, out _);

        Assert.That(ok, Is.False);
        Assert.That(med.CurrentDurability, Is.EqualTo(100));
    }

    private sealed class DeferredHealTarget : FakeTarget, IRemoteHealingTarget
    {
        public System.Action<float> Reply;
        public bool Accept = true;
        public bool RequiresRemoteHeal => true;
        public float Requested;

        public bool RequestHeal(float amount, System.Action<float> completed)
        {
            Requested = amount;
            Reply = completed;
            return Accept;
        }
    }

    [Test]
    public void TryHealRemote_WaitsForConfirmedAmountAndIgnoresDuplicateReply()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new DeferredHealTarget { CurrentHealth = 40f };
        var notifications = 0;

        Assert.That(ConsumableUseService.TryHealRemote(med, target, _ => notifications++), Is.True);
        Assert.That(target.Requested, Is.EqualTo(50f));
        Assert.That(med.CurrentDurability, Is.EqualTo(100));
        Assert.That(ConsumableUseService.TryHealRemote(med, target, _ => notifications++), Is.False);
        target.Reply(15f);
        target.Reply(15f);

        Assert.That(med.CurrentDurability, Is.EqualTo(85));
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(target.CurrentHealth, Is.EqualTo(40f));
    }

    [Test]
    public void TryHealRemote_RejectionDoesNotConsumeDurability()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new DeferredHealTarget { CurrentHealth = 40f };
        Assert.That(ConsumableUseService.TryHealRemote(med, target, _ => { }), Is.True);
        target.Reply(0f);
        Assert.That(med.CurrentDurability, Is.EqualTo(100));
        Assert.That(ConsumableUseService.TryHealRemote(med, target, _ => { }), Is.True);
    }

    // ==================== 修甲路径 ====================

    [Test]
    public void CanRepair_WhenSlotDamagedAndHasPool_IsTrue()
    {
        var kit = TestItems.RepairInstance(repairAmount: 30, pool: 60);
        var target = new FakeTarget();
        target.Armor[EquipmentSlotType.BodyArmor] = (10, 50);

        Assert.That(ConsumableUseService.CanRepair(kit, target, EquipmentSlotType.BodyArmor), Is.True);
    }

    [Test]
    public void CanRepair_WhenSlotFull_IsFalse()
    {
        var kit = TestItems.RepairInstance(repairAmount: 30, pool: 60);
        var target = new FakeTarget();
        target.Armor[EquipmentSlotType.BodyArmor] = (50, 50);

        Assert.That(ConsumableUseService.CanRepair(kit, target, EquipmentSlotType.BodyArmor), Is.False);
    }

    [Test]
    public void CanRepair_WhenSlotEmpty_IsFalse()
    {
        var kit = TestItems.RepairInstance(repairAmount: 30, pool: 60);
        var target = new FakeTarget();

        Assert.That(ConsumableUseService.CanRepair(kit, target, EquipmentSlotType.BodyArmor), Is.False);
    }

    [Test]
    public void CanRepair_ForWeaponSlot_IsFalse()
    {
        var kit = TestItems.RepairInstance(repairAmount: 30, pool: 60);
        var target = new FakeTarget();

        Assert.That(ConsumableUseService.CanRepair(kit, target, EquipmentSlotType.MainHand), Is.False);
    }

    [Test]
    public void CanRepair_ForHealOnlyItem_IsFalse()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        var target = new FakeTarget();
        target.Armor[EquipmentSlotType.BodyArmor] = (10, 50);

        Assert.That(ConsumableUseService.CanRepair(med, target, EquipmentSlotType.BodyArmor), Is.False);
    }

    [Test]
    public void TryRepair_RepairsOnlyTheTargetSlot()
    {
        var kit = TestItems.RepairInstance(repairAmount: 30, pool: 60);
        var target = new FakeTarget();
        target.Armor[EquipmentSlotType.Helmet] = (10, 50);
        target.Armor[EquipmentSlotType.BodyArmor] = (20, 50);

        var ok = ConsumableUseService.TryRepair(kit, target, EquipmentSlotType.BodyArmor, out var result);

        Assert.That(ok, Is.True);
        Assert.That(result.Repaired, Is.EqualTo(30));
        // 只修了胸甲，头盔原样不动。
        Assert.That(target.Armor[EquipmentSlotType.BodyArmor].Current, Is.EqualTo(50));
        Assert.That(target.Armor[EquipmentSlotType.Helmet].Current, Is.EqualTo(10));
        Assert.That(kit.CurrentDurability, Is.EqualTo(30));
    }

    [Test]
    public void TryRepair_WhenPoolSmallerThanMissing_RepairsOnlyWhatPoolAllows()
    {
        // 胸甲缺 40，但甲修池只剩 25 —— 只能修 25。
        var kit = TestItems.RepairInstance(repairAmount: 60, pool: 60);
        kit.ConsumeDurability(35);
        var target = new FakeTarget();
        target.Armor[EquipmentSlotType.BodyArmor] = (10, 50);

        var ok = ConsumableUseService.TryRepair(kit, target, EquipmentSlotType.BodyArmor, out var result);

        Assert.That(ok, Is.True);
        Assert.That(result.Repaired, Is.EqualTo(25));
        Assert.That(target.Armor[EquipmentSlotType.BodyArmor].Current, Is.EqualTo(35));
        Assert.That(kit.CurrentDurability, Is.Zero);
    }

    [Test]
    public void TryRepair_WhenInCombatLock_DoesNothing()
    {
        var kit = TestItems.RepairInstance(repairAmount: 30, pool: 60);
        var target = new FakeTarget { CombatLock = true };
        target.Armor[EquipmentSlotType.BodyArmor] = (10, 50);

        var ok = ConsumableUseService.TryRepair(kit, target, EquipmentSlotType.BodyArmor, out _);

        Assert.That(ok, Is.False);
        Assert.That(kit.CurrentDurability, Is.EqualTo(60));
        Assert.That(target.Armor[EquipmentSlotType.BodyArmor].Current, Is.EqualTo(10));
    }

    // ==================== 修甲成本按品质折算 ====================

    [Test]
    public void TryRepair_CommonArmor_SpendsOnePerPoint()
    {
        var kit = TestItems.RepairInstance(repairAmount: 60, pool: 60);
        var target = new FakeTarget();
        target.Armor[EquipmentSlotType.BodyArmor] = (10, 50);
        target.Rarities[EquipmentSlotType.BodyArmor] = ItemRarity.Common;

        var ok = ConsumableUseService.TryRepair(kit, target, EquipmentSlotType.BodyArmor, out var result);

        Assert.That(ok, Is.True);
        Assert.That(result.Repaired, Is.EqualTo(40));
        // 普通倍率 1.0：修 40 点扣 40。
        Assert.That(result.DurabilitySpent, Is.EqualTo(40));
        Assert.That(kit.CurrentDurability, Is.EqualTo(20));
    }

    [Test]
    public void TryRepair_LegendaryArmor_SpendsMorePerPoint()
    {
        var kit = TestItems.RepairInstance(repairAmount: 60, pool: 60);
        var target = new FakeTarget();
        target.Armor[EquipmentSlotType.BodyArmor] = (10, 50);
        target.Rarities[EquipmentSlotType.BodyArmor] = ItemRarity.Legendary;

        var ok = ConsumableUseService.TryRepair(kit, target, EquipmentSlotType.BodyArmor, out var result);

        Assert.That(ok, Is.True);
        // 传说倍率 2.2：池 60 只能修 floor(60/2.2)=27 点。
        Assert.That(result.Repaired, Is.EqualTo(27));
        Assert.That(result.DurabilitySpent, Is.EqualTo(60));
        Assert.That(kit.CurrentDurability, Is.Zero);
    }

    [Test]
    public void TryRepair_HigherRaritySpendsMoreForSameRepair()
    {
        var commonKit = TestItems.RepairInstance(repairAmount: 60, pool: 60);
        var commonTarget = new FakeTarget();
        commonTarget.Armor[EquipmentSlotType.BodyArmor] = (0, 20);
        commonTarget.Rarities[EquipmentSlotType.BodyArmor] = ItemRarity.Common;

        var rareKit = TestItems.RepairInstance(repairAmount: 60, pool: 60);
        var rareTarget = new FakeTarget();
        rareTarget.Armor[EquipmentSlotType.BodyArmor] = (0, 20);
        rareTarget.Rarities[EquipmentSlotType.BodyArmor] = ItemRarity.Rare;

        ConsumableUseService.TryRepair(commonKit, commonTarget, EquipmentSlotType.BodyArmor, out var commonResult);
        ConsumableUseService.TryRepair(rareKit, rareTarget, EquipmentSlotType.BodyArmor, out var rareResult);

        // 同样修满 20 点，稀有（1.5）比普通（1.0）多花 50%。
        Assert.That(commonResult.Repaired, Is.EqualTo(20));
        Assert.That(rareResult.Repaired, Is.EqualTo(20));
        Assert.That(commonResult.DurabilitySpent, Is.EqualTo(20));
        Assert.That(rareResult.DurabilitySpent, Is.EqualTo(30));
    }

    [Test]
    public void RepairCostMultiplier_IncreasesWithRarity()
    {
        var common = ItemRarityRules.RepairCostMultiplier(ItemRarity.Common);
        var fine = ItemRarityRules.RepairCostMultiplier(ItemRarity.Fine);
        var rare = ItemRarityRules.RepairCostMultiplier(ItemRarity.Rare);
        var epic = ItemRarityRules.RepairCostMultiplier(ItemRarity.Epic);
        var legendary = ItemRarityRules.RepairCostMultiplier(ItemRarity.Legendary);

        Assert.That(fine, Is.GreaterThan(common));
        Assert.That(rare, Is.GreaterThan(fine));
        Assert.That(epic, Is.GreaterThan(rare));
        Assert.That(legendary, Is.GreaterThan(epic));
    }

    // ==================== 耐久不可恢复 ====================

    [Test]
    public void ConsumableDurability_CannotBeRestored()
    {
        var med = TestItems.HealInstance(healAmount: 50f, pool: 100);
        med.ConsumeDurability(40);

        // 甲修也修不了消耗品自己 —— 消耗品耐久是「剩余量池」，用掉即永久减少。
        var restored = med.AddDurability(100);

        Assert.That(restored, Is.Zero);
        Assert.That(med.CurrentDurability, Is.EqualTo(60));
        Assert.That(med.IsRepairable, Is.False);
    }

    [Test]
    public void ArmorDurability_CanStillBeRestored()
    {
        var armor = TestItems.ArmorInstance(EquipmentSlotType.BodyArmor, durability: 50);
        armor.ConsumeDurability(20);

        var restored = armor.AddDurability(100);

        Assert.That(restored, Is.EqualTo(20));
        Assert.That(armor.CurrentDurability, Is.EqualTo(50));
        Assert.That(armor.IsRepairable, Is.True);
    }
}
