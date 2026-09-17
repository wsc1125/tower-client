using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>掉落列表的一行：图标 + 名称 + 价值，点击后把行号回传给面板。</summary>
public sealed class LootRowView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Text nameText;
    [SerializeField] private Text valueText;
    [SerializeField] private Button button;

    private Action<int> onSelected;
    private int rowIndex;

    public void Bind(int index, Action<int> handler)
    {
        if (button == null)
            button = GetComponent<Button>();

        if (onSelected == handler && rowIndex == index && button != null)
            return;

        rowIndex = index;
        onSelected = handler;

        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onSelected?.Invoke(rowIndex));
    }

    public void SetEntry(LootEntry entry, bool visible)
    {
        gameObject.SetActive(visible);
        if (!visible)
            return;

        if (icon != null)
        {
            icon.sprite = entry.Icon;
            icon.enabled = entry.Icon != null;
        }

        if (nameText != null)
            nameText.text = entry.DisplayName ?? string.Empty;

        if (valueText != null)
            valueText.text = "价值 " + entry.Value;
    }
}
