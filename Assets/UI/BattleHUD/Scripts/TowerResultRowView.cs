using UnityEngine;
using UnityEngine.UI;

/// <summary>结算面板的一行：图标 + 名称 + 价值。</summary>
public sealed class TowerResultRowView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Text nameText;
    [SerializeField] private Text valueText;

    public void SetEntry(TowerResultEntry entry, bool visible)
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
