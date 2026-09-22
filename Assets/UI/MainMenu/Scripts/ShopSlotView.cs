using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 商店面板中的单个商品卡片：展示商品图标、名称与价格，被点击时通知 <see cref="ShopPanelController"/>。
/// 卡片底图与选中高亮分别使用「商品卡片」「商品卡片高亮」两张素材，均保持原色。
/// </summary>
public sealed class ShopSlotView : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    /// <summary>按下到抬起的位移超过该像素值时视为滑动，不触发选中。</summary>
    private const float ClickDragThreshold = 20f;

    [SerializeField] private Image frame;
    [SerializeField] private Image highlight;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text itemName;
    [SerializeField] private TMP_Text priceText;

    private ShopPanelController controller;
    private int slotIndex;
    private Vector2 pointerDownPosition;

    public int SlotIndex => slotIndex;

    /// <summary>由 <see cref="ShopPanelController"/> 在初始化时调用。</summary>
    public void Bind(ShopPanelController owner, int index)
    {
        controller = owner;
        slotIndex = index;
        SetSelected(false);
    }

    public void SetItem(ItemDefinition value)
    {
        if (icon != null)
        {
            icon.sprite = value != null ? value.Icon : null;
            icon.enabled = value != null && value.Icon != null;
        }

        if (itemName != null)
            itemName.text = value != null ? value.DisplayName : string.Empty;

        if (priceText != null)
            priceText.text = value != null ? PriceFormatter.Format(value.BuyPrice) : string.Empty;
    }

    public void SetSelected(bool selected)
    {
        if (highlight != null)
            highlight.enabled = selected;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pointerDownPosition = eventData.position;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller == null)
            return;

        // 只有左键（或触屏）才切换选中，避免右键误触。
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        // 横向滑动列表时手指会落在卡片上，位移过大就不算点击。
        if (Vector2.Distance(eventData.position, pointerDownPosition) > ClickDragThreshold)
            return;

        controller.SelectSlot(slotIndex);
    }
}
