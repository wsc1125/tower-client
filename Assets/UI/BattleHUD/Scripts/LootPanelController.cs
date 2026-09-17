using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 附近掉落物品列表。只负责展示与把「点行」转成行号事件，
/// 具体拾取由 <c>LootInteractor</c> 处理，避免 BattleHUD 反向依赖 Gameplay。
/// </summary>
public sealed class LootPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private LootRowView[] rows;
    [SerializeField] private Text hintText;
    [SerializeField] private float hintDuration = 1.5f;

    private float hintRemaining;

    public event Action<int> ItemSelected;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        Hide();
    }

    private void Update()
    {
        if (hintRemaining <= 0f)
            return;

        hintRemaining = Mathf.Max(0f, hintRemaining - Time.unscaledDeltaTime);
        if (hintRemaining <= 0f && hintText != null)
            hintText.text = string.Empty;
    }

    /// <summary>按传入顺序填充行，多余的行隐藏。传空列表等价于「只显示面板外框、无行」。</summary>
    public void Show(IReadOnlyList<LootEntry> entries)
    {
        if (panelRoot == null)
            return;

        BindRows();
        panelRoot.SetActive(true);

        var count = entries != null ? entries.Count : 0;
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null)
                continue;

            if (i < count)
                rows[i].SetEntry(entries[i], true);
            else
                rows[i].SetEntry(default, false);
        }
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>显示一条短提示（如「背包已满」），到时自动清空。</summary>
    public void ShowHint(string message)
    {
        if (hintText == null)
            return;

        hintText.text = message;
        hintRemaining = Mathf.Max(0f, hintDuration);
    }

    private void BindRows()
    {
        if (rows == null)
            return;

        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null)
                rows[i].Bind(i, RaiseSelected);
        }
    }

    private void RaiseSelected(int index)
    {
        ItemSelected?.Invoke(index);
    }
}
