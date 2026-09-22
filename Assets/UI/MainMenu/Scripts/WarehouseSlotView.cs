using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 【已弃用】旧仓库面板的单个物品格（新仓库已改用 LoadoutSlotView）。
/// 原职责：展示物品图标，被点击时通知 <see cref="WarehousePanelController"/>。
/// 格子底图与选中高亮分别使用「仓库格」「仓库格-高亮」两张素材，均保持原色。
/// </summary>
public sealed class WarehouseSlotView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image frame;
    [SerializeField] private Image icon;
    [SerializeField] private Image highlight;

    private WarehousePanelController controller;
    private int slotIndex;

    public int SlotIndex => slotIndex;

    /// <summary>由 <see cref="WarehousePanelController"/> 在初始化时调用。</summary>
    public void Bind(WarehousePanelController owner, int index)
    {
        controller = owner;
        slotIndex = index;
        SetSelected(false);
    }

    public void SetItem(ItemDefinition value)
    {
        if (icon == null)
            return;

        icon.sprite = value != null ? value.Icon : null;
        icon.enabled = value != null && value.Icon != null;
    }

    public void SetSelected(bool selected)
    {
        if (highlight != null)
            highlight.enabled = selected;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller == null)
            return;

        // 只有左键（或触屏）才切换选中，避免右键误触。
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        controller.SelectSlot(slotIndex);
    }
}
