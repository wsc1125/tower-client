using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在玩家身上：周期性扫描附近的掉落物，在「附近没有存活敌人」时把最近的几件推给
/// <see cref="LootPanelController"/> 展示，并负责把「点列表某行」翻译成实际拾取。
/// </summary>
public sealed class LootInteractor : MonoBehaviour
{
    [SerializeField] private float pickupRadius = 1.6f;
    [SerializeField] private float combatRadius = 8f;
    [SerializeField] private int maxEntries = 4;
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private LootPanelController panel;

    private readonly List<DroppedItem2D> current = new List<DroppedItem2D>();
    private readonly List<DroppedItem2D> previous = new List<DroppedItem2D>();
    private readonly List<LootEntry> entries = new List<LootEntry>();
    private float timer;

    private void Awake()
    {
        if (panel != null)
            panel.ItemSelected += HandleItemSelected;
    }

    private void OnDestroy()
    {
        if (panel != null)
            panel.ItemSelected -= HandleItemSelected;
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        timer = Mathf.Max(0.01f, scanInterval);
        Scan();
    }

    /// <summary>
    /// 一轮扫描：先判附近有没有存活敌人，没有才收集掉落物并刷新面板。
    /// 采用定时轮询而非进出触发器，是为了让中途加入 / 重连的客户端也能从当前世界状态自洽地重建列表。
    /// </summary>
    public void Scan()
    {
        if (panel == null)
            return;

        var damageables = CollectDamageables();
        if (LootScan.HasAliveEnemyWithin(damageables, transform.position, combatRadius))
        {
            HidePanel();
            return;
        }

        current.Clear();
        var hits = Physics2D.OverlapCircleAll(transform.position, pickupRadius);
        foreach (var hit in hits)
        {
            var dropped = hit.GetComponentInParent<DroppedItem2D>();
            if (dropped == null || current.Contains(dropped))
                continue;

            current.Add(dropped);
        }

        var nearest = LootScan.SelectNearest(current.ToArray(), transform.position, maxEntries);
        current.Clear();
        current.AddRange(nearest);

        if (current.Count == 0)
        {
            HidePanel();
            return;
        }

        if (!SameAsPrevious())
        {
            BuildEntries();
            previous.Clear();
            previous.AddRange(current);
            panel.Show(entries);
            return;
        }

        // 物品集合没变时也要确保面板处于显示状态，
        // 否则「外部隐藏了面板」会让列表再也出不来。
        if (!panel.IsVisible)
            panel.Show(entries);
    }

    /// <summary>
    /// 收集场上所有可受伤目标。必须覆盖全部敌人类型（含虫群与 Boss），
    /// 否则「附近有敌人」判定会漏，战斗中点开拾取列表。
    /// 与 RifleShooter2D.FindNearestEnemy 同构，避免 FindObjectsOfType&lt;MonoBehaviour&gt; 的全量扫描。
    /// </summary>
    public static IDamageable[] CollectDamageables()
    {
        var registered = WorldRegistry.Damageables;
        var result = new IDamageable[registered.Count];
        var index = 0;
        foreach (var damageable in registered)
            result[index++] = damageable;

        return result;
    }

    /// <summary>拾取的唯一入口。将来接联机时只改这个方法内部：发请求 + 服务器校验后再发货。</summary>
    public bool TryPickup(DroppedItem2D dropped)
    {
        if (dropped == null || dropped.Item == null)
            return false;

        // 局内拾取进「携带」，与局内背包面板展示的是同一份数据；
        // 仓库只在主菜单里管理，战斗内不展示。
        if (!GameSession.LocalPlayer.Raid.TryAddToCarry(dropped.Item))
        {
            if (panel != null)
                panel.ShowHint("背包已满");

            return false;
        }

        // 记入本局战利品清单，撤退结算面板据此展示。
        GameSession.LocalPlayer.Raid.RecordLoot(dropped.Item);

        if (Application.isPlaying)
            Destroy(dropped.gameObject);
        else
            DestroyImmediate(dropped.gameObject);

        previous.Clear();
        Scan();
        return true;
    }

    private void HandleItemSelected(int index)
    {
        if (index < 0 || index >= current.Count)
            return;

        TryPickup(current[index]);
    }

    private bool SameAsPrevious()
    {
        if (previous.Count != current.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            if (previous[i] != current[i])
                return false;
        }

        return true;
    }

    private void BuildEntries()
    {
        entries.Clear();
        foreach (var dropped in current)
        {
            var definition = dropped.Definition;
            entries.Add(new LootEntry
            {
                DisplayName = definition != null ? definition.DisplayName : string.Empty,
                Value = definition != null ? definition.Value : 0,
                Icon = definition != null ? definition.Icon : null,
            });
        }
    }

    private void HidePanel()
    {
        previous.Clear();
        panel.Hide();
    }
}
