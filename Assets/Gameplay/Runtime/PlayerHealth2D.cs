using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerHealth
{
    public PlayerHealth(float maximum)
    {
        Maximum = Mathf.Max(1f, maximum);
        Current = Maximum;
    }

    public float Maximum { get; }
    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        Current = Mathf.Max(0f, Current - amount);
    }

    /// <summary>恢复血量，返回实际恢复量。已死亡或已满时不生效。</summary>
    public float Restore(float amount)
    {
        if (IsDead || amount <= 0f)
            return 0f;

        var before = Current;
        Current = Mathf.Min(Maximum, Current + amount);
        return Current - before;
    }
}

/// <summary>
/// 单件护甲的运行时视图：直接持有物品实例，耐久读写都落到实例上。
/// 因此耐久只有一份真相 —— 不再有「影子耐久」与实例脱节的问题。
/// </summary>
public sealed class ArmorPiece
{
    public ArmorPiece(EquipmentSlotType slot, ItemInstance instance)
    {
        Slot = slot;
        Instance = instance;
    }

    public EquipmentSlotType Slot { get; }

    /// <summary>该槽上的物品实例。耐久读写都走它。</summary>
    public ItemInstance Instance { get; }

    public float DamageReduction =>
        Instance != null && Instance.Definition != null ? Instance.Definition.DamageReduction : 0f;

    public int MaximumDurability => Instance != null ? Instance.MaximumDurability : 0;
    public int CurrentDurability => Instance != null ? Instance.CurrentDurability : 0;

    /// <summary>耐久归零即失效，不再提供减伤。</summary>
    public bool IsBroken => Instance != null && Instance.IsBroken;

    /// <summary>该件当前实际生效的减伤率；已失效时为 0。</summary>
    public float EffectiveDamageReduction => IsBroken ? 0f : DamageReduction;

    /// <summary>消耗 1 点耐久。已归零时不再下降。</summary>
    public void ConsumeDurability() => Instance?.ConsumeDurability();
}

/// <summary>
/// 护甲组：按已装备防具逐件维护耐久，减伤率按「1-(1-a)(1-b)(1-c)」递减叠加。
/// 每次受击只有**参与减伤**（未失效）的护甲各扣 1 点耐久。
/// </summary>
public sealed class PlayerArmor
{
    private readonly List<ArmorPiece> pieces = new List<ArmorPiece>();

    public IReadOnlyList<ArmorPiece> Pieces => pieces;

    /// <summary>当前生效的总减伤率（0~1）。已失效的护甲不参与计算。</summary>
    public float DamageReduction
    {
        get
        {
            var remaining = 1f;
            foreach (var piece in pieces)
                remaining *= 1f - piece.EffectiveDamageReduction;

            return 1f - remaining;
        }
    }

    public int CurrentDurability
    {
        get
        {
            var total = 0;
            foreach (var piece in pieces)
                total += piece.CurrentDurability;

            return total;
        }
    }

    public int MaximumDurability
    {
        get
        {
            var total = 0;
            foreach (var piece in pieces)
                total += piece.MaximumDurability;

            return total;
        }
    }

    /// <summary>
    /// 按当前装备重建护甲组。耐久直接读物品实例，因此换装 / 搬运后天然正确，
    /// 不需要像旧实现那样「同槽且上限未变才沿用旧耐久」。
    /// </summary>
    public void Rebuild(IReadOnlyList<ArmorPiece> equipped)
    {
        pieces.Clear();
        if (equipped == null)
            return;

        foreach (var piece in equipped)
            pieces.Add(piece);
    }

    /// <summary>
    /// 按减伤率结算伤害，返回实际扣血量。
    /// 只有未失效（仍在提供减伤）的护甲才消耗耐久 —— 已破损的护甲不再继续磨损。
    /// </summary>
    public float Absorb(float amount)
    {
        if (amount <= 0f)
            return 0f;

        var reduced = amount * (1f - DamageReduction);

        foreach (var piece in pieces)
        {
            if (!piece.IsBroken)
                piece.ConsumeDurability();
        }

        return reduced;
    }
}

public sealed class PlayerHealth2D : MonoBehaviour, IConsumableTarget
{
    [SerializeField] private float maximumHealth = 100f;
    [SerializeField] private StatusPanelController statusPanel;

    [Tooltip("受击后禁止使用消耗品的时长（秒）。参考「脱战才能打药」的规则。")]
    [SerializeField] private float combatLockDuration = 3f;

    [Tooltip("受击闪烁的目标。留空则自动取子物体上的 CharacterVisual2D。")]
    [SerializeField] private CharacterVisual2D visual;

    [Tooltip("受击屏幕红边。留空则不做屏幕反馈。")]
    [SerializeField] private DamageVignetteController damageVignette;

    private PlayerHealth health;
    private PlayerArmor armor;
    private bool deathAnnounced;
    private float lastHitTime = float.NegativeInfinity;

    /// <summary>血量归零的那一刻触发一次；已死亡后再受伤不会重复触发。</summary>
    public event Action Died;

    /// <summary>
    /// 每次实际扣血后触发一次，参数为本次实际扣掉的血量（已计入护甲减伤）。
    /// 供表现层做受击反馈（闪白、屏幕红边等）。已死亡后的补刀不会触发。
    /// </summary>
    public event Action<float> Damaged;

    /// <summary>
    /// 护甲耐久发生变化时触发。由上层决定是否同步到玩家资产、存档或网络状态。
    /// PlayerHealth2D 不直接写入 GameSession。
    /// </summary>
    public event Action ArmorChanged;

    public float CurrentHealth => health != null ? health.Current : maximumHealth;
    public float MaximumHealth => health != null ? health.Maximum : maximumHealth;
    public bool IsDead => health != null && health.IsDead;

    /// <summary>受击闪烁期间免疫伤害。闪烁时长由 CharacterVisual2D 的 hitFlashDuration 决定。</summary>
    public bool IsInvulnerable
    {
        get
        {
            if (visual == null)
                visual = GetComponentInChildren<CharacterVisual2D>();

            return visual != null && visual.IsHitFlashing;
        }
    }

    /// <summary>当前生效的总减伤率（0~1）。</summary>
    public float DamageReduction => armor != null ? armor.DamageReduction : 0f;

    public int CurrentArmorDurability => armor != null ? armor.CurrentDurability : 0;
    public int MaximumArmorDurability => armor != null ? armor.MaximumDurability : 0;

    private void Awake()
    {
        // 先解析表现层引用：它不依赖局内数据，且必须早于可能抛异常的 RebuildArmor，
        // 否则一旦局内数据不可用（如测试环境），visual 会一直是 null，受击反馈与无敌帧全部失效。
        if (visual == null)
            visual = GetComponentInChildren<CharacterVisual2D>();

        health = new PlayerHealth(maximumHealth);
        armor = new PlayerArmor();
        RebuildArmor();
        SubscribeRaid();
        RefreshView();
    }

    private void OnDestroy()
    {
        GameSession.LoadoutChanged -= RefreshArmor;
    }

    public void TakeDamage(float amount)
    {
        health ??= new PlayerHealth(maximumHealth);
        armor ??= new PlayerArmor();

        // 已死亡就不再结算：否则死亡到切场景之间，尸体每被击中一次都会白扣一轮护甲耐久。
        if (health.IsDead)
            return;

        // 受击闪烁期间免疫伤害：否则被虫群/激光连续命中会瞬间掉光血，且闪烁表现会被反复重置。
        if (IsInvulnerable)
            return;

        // 护甲按减伤率削减伤害，同时消耗耐久。
        var reduced = armor.Absorb(amount);
        var before = health.Current;
        health.TakeDamage(reduced);
        RefreshView();

        // 记下受击时刻：此后 combatLockDuration 秒内禁止使用消耗品。
        lastHitTime = Time.time;

        // 耐久变了，通知上层（由上层决定是否写入资产 / 存档 / 网络状态）。
        ArmorChanged?.Invoke();

        // 只有真正扣掉血才广播，避免「护甲全挡下」也触发受击反馈。
        var actualDamage = before - health.Current;
        if (actualDamage > 0f)
        {
            Damaged?.Invoke(actualDamage);
            PlayHitFeedback();
        }

        if (health.IsDead && !deathAnnounced)
        {
            deathAnnounced = true;
            Died?.Invoke();
        }
    }

    /// <summary>当前缺多少血（上限 - 当前）。满血时返回 0。</summary>
    public float MissingHealth()
    {
        health ??= new PlayerHealth(maximumHealth);
        return Mathf.Max(0f, health.Maximum - health.Current);
    }

    /// <summary>恢复血量，返回实际恢复量。已死亡时不生效。</summary>
    public float Heal(float amount)
    {
        health ??= new PlayerHealth(maximumHealth);
        if (health.IsDead)
            return 0f;

        var restored = health.Restore(amount);
        if (restored > 0f)
        {
            RefreshView();
            GameSession.LocalPlayer.Raid.NotifyChanged();
        }

        return restored;
    }

    /// <summary>
    /// 修复护甲耐久：按「最破损优先」顺序逐件恢复，返回实际修复总量。
    /// 未穿防具或全部满耐久时返回 0。
    /// </summary>
    public int RepairArmor(int amount)
    {
        armor ??= new PlayerArmor();
        if (amount <= 0)
            return 0;

        var remaining = amount;
        var repaired = 0;

        // 先修最破损的，避免「修了一半、另一件还是破的」。
        var ordered = new List<ArmorPiece>(armor.Pieces);
        ordered.Sort((a, b) => a.CurrentDurability.CompareTo(b.CurrentDurability));

        foreach (var piece in ordered)
        {
            if (remaining <= 0)
                break;

            if (piece.Instance == null)
                continue;

            var restored = piece.Instance.AddDurability(remaining);
            remaining -= restored;
            repaired += restored;
        }

        if (repaired > 0)
        {
            RefreshView();
            ArmorChanged?.Invoke();
        }

        return repaired;
    }

    /// <summary>
    /// 修复指定防具槽那一件的耐久，返回实际修复量。
    /// 甲修拖到具体防具槽上时走这里 —— 玩家可精确选择修哪件。
    /// </summary>
    public int RepairArmorSlot(EquipmentSlotType slot, int amount)
    {
        if (amount <= 0 || !EquipmentSlots.IsArmorSlot(slot))
            return 0;

        var instance = GameSession.LocalPlayer.Loadout.Equipment.Get(slot);
        if (instance == null)
            return 0;

        var repaired = instance.AddDurability(amount);
        if (repaired > 0)
        {
            RefreshArmor();
            ArmorChanged?.Invoke();
        }

        return repaired;
    }

    /// <summary>指定防具槽当前缺多少耐久（上限 - 当前）。该槽无防具或非防具时返回 0。</summary>
    public int MissingDurabilityAt(EquipmentSlotType slot)
    {
        if (!EquipmentSlots.IsArmorSlot(slot))
            return 0;

        var instance = GameSession.LocalPlayer.Loadout.Equipment.Get(slot);
        if (instance == null || !instance.IsRepairable)
            return 0;

        return Mathf.Max(0, instance.MaximumDurability - instance.CurrentDurability);
    }

    /// <summary>指定防具槽那件装备的品质。用于折算修甲成本；该槽无防具时返回普通。</summary>
    public ItemRarity RarityAt(EquipmentSlotType slot)
    {
        if (!EquipmentSlots.IsArmorSlot(slot))
            return ItemRarity.Common;

        var instance = GameSession.LocalPlayer.Loadout.Equipment.Get(slot);
        return instance != null && instance.Definition != null
            ? instance.Definition.Rarity
            : ItemRarity.Common;
    }

    /// <summary>受击后 combatLockDuration 秒内禁止使用消耗品。</summary>
    public bool IsInCombatLock => Time.time - lastHitTime < combatLockDuration;

    /// <summary>剩余禁止使用消耗品的秒数；已脱战时为 0。</summary>
    public float CombatLockRemaining => Mathf.Max(0f, combatLockDuration - (Time.time - lastHitTime));

    /// <summary>重新按当前装备重建护甲组。装备变更时由局内数据事件触发。</summary>
    public void RefreshArmor()
    {
        armor ??= new PlayerArmor();
        RebuildArmor();
        RefreshView();
    }

    /// <summary>
    /// 运行时绑定 HUD 表现层。联机动态生成的玩家来自 prefab，prefab 内不含场景 HUD 组件，
    /// 需要在生成后把场景里的血量面板 / 受击红边绑给本地玩家，否则血量不显示、受击无反馈。
    /// </summary>
    public void BindHud(StatusPanelController panel, DamageVignetteController vignette)
    {
        if (panel != null)
            statusPanel = panel;
        if (vignette != null)
            damageVignette = vignette;
        RefreshView();
    }

    /// <summary>护甲取自局内装备槽 —— 局内换装会立刻反映到减伤上。</summary>
    private void RebuildArmor()
    {
        var equipped = new List<ArmorPiece>();
        foreach (var slot in EquipmentSlots.ArmorSlots)
        {
            var instance = GameSession.LocalPlayer.Loadout.Equipment.Get(slot);
            if (instance == null || instance.MaximumDurability <= 0)
                continue;

            equipped.Add(new ArmorPiece(slot, instance));
        }

        armor.Rebuild(equipped);
    }

    private void SubscribeRaid()
    {
        // 装备槽与局外是同一份，因此换装 / 死亡清空都通过 LoadoutChanged 通知。
        GameSession.LoadoutChanged += RefreshArmor;
    }

    private void RefreshView()
    {
        if (statusPanel == null)
            return;

        statusPanel.SetHealth(health.Current, health.Maximum);
    }

    /// <summary>
    /// 受击表现：角色闪红 + 屏幕红边。
    /// 两者都做成「可缺省」——预制体上没接就不做，方便在测试场景里只挂血量组件。
    /// </summary>
    private void PlayHitFeedback()
    {
        if (visual == null)
            visual = GetComponentInChildren<CharacterVisual2D>();

        if (visual != null)
            visual.FlashHit();

        if (damageVignette != null)
            damageVignette.Flash();
    }
}
