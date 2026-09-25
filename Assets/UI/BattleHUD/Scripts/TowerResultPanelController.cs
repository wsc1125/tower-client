using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 撤退结算面板：列出本局拾取的全部战利品与总价值，玩家确认后回主菜单。
/// 只负责展示与把「点确认」转成事件，具体切场景由 TowerRunManager 处理，
/// 避免 BattleHUD 反向依赖 Gameplay。
/// </summary>
public sealed class TowerResultPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TowerResultRowView[] rows;
    [SerializeField] private Text totalValueText;
    [SerializeField] private TMP_Text floorText;
    [SerializeField] private Button confirmButton;

    /// <summary>玩家点了确认按钮。</summary>
    public event Action Confirmed;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        BindConfirmButton();
        Hide();
    }

    private void OnDestroy()
    {
        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(RaiseConfirmed);
    }

    /// <summary>
    /// 幂等绑定确认按钮。拆成公开方法是因为 Awake 跑在 AddComponent 那一刻，
    /// 此时序列化引用可能还没赋值（测试与运行时接线都会遇到），需要能补绑一次。
    /// </summary>
    public void BindConfirmButton()
    {
        if (confirmButton == null)
            return;

        confirmButton.onClick.RemoveListener(RaiseConfirmed);
        confirmButton.onClick.AddListener(RaiseConfirmed);
    }

    /// <summary>显示结算面板。entries 为 null 或空时只显示面板外框与总价值。</summary>
    public void Show(IReadOnlyList<TowerResultEntry> entries, int totalValue, int floor)
    {
        if (panelRoot == null)
            return;

        panelRoot.SetActive(true);

        var count = entries != null ? entries.Count : 0;
        if (rows != null)
        {
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

        if (totalValueText != null)
            totalValueText.text = "总价值 " + totalValue;

        if (floorText != null)
            floorText.text = "抵达第 " + Mathf.Max(1, floor) + " 层";
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void RaiseConfirmed()
    {
        Confirmed?.Invoke();
    }
}
