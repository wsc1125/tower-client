using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 局内背包里的单个格子：底图（frame）+ 品质底板 + 物品图标。
/// 品质底板仅在格内有物品时显示，颜色按物品品质取，与局外装备配置面板同一套规则。
/// 支持点击选中（详情区据此刷新）与拖拽搬运。
/// </summary>
public sealed class BagItemView : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image frame;

    [Header("品质底板")]
    [Tooltip("底板根节点；格内有物品时显示。")]
    [SerializeField] private GameObject qualityPlate;
    [Tooltip("品质描边层，按品质着色。")]
    [SerializeField] private Image qualityBorder;
    [Tooltip("底板内层，深底与品质色的混合。")]
    [SerializeField] private Image qualityFill;

    private BagPanelController controller;
    private ItemDefinition item;
    private int bagIndex;
    private bool selected;

    public int BagIndex => bagIndex;

    /// <summary>当前格内的物品；空槽时为 null。</summary>
    public ItemDefinition Item => item;

    public void Bind(BagPanelController owner, int index)
    {
        controller = owner;
        bagIndex = index;
        SetSelected(false);
    }

    public void SetItem(ItemDefinition value)
    {
        item = value;
        ApplyQualityPlate(value);

        if (icon == null)
            return;

        icon.sprite = value != null ? value.Icon : null;
        icon.enabled = value != null && value.Icon != null;
    }

    /// <summary>有物品才显示品质底板；颜色按物品品质取。</summary>
    private void ApplyQualityPlate(ItemDefinition value)
    {
        if (qualityPlate == null)
            return;

        var hasItem = value != null;
        qualityPlate.SetActive(hasItem);

        if (!hasItem)
            return;

        var rarityColor = UiTokens.RarityColor(value.Rarity);
        if (qualityBorder != null)
            qualityBorder.color = rarityColor;

        if (qualityFill != null)
            qualityFill.color = Color.Lerp(UiTokens.RarityPlateBackground, rarityColor, UiTokens.RarityPlateTintAlpha);
    }

    public void SetSelected(bool value)
    {
        selected = value;
        if (frame != null)
            frame.color = value ? SelectedTint : Color.white;
    }

    /// <summary>拖拽悬停反馈；离开时调用 false 会恢复选中态。</summary>
    public void SetDropHighlight(bool value)
    {
        if (frame == null)
            return;

        frame.color = value ? DropTint : (selected ? SelectedTint : Color.white);
    }

    public bool TryGetDragSource(out BagDragSource source)
    {
        source = BagDragSource.FromBag(bagIndex, item);
        return item != null;
    }

    public bool CanAccept(BagDragSource source, Inventory inventory)
    {
        if (inventory == null)
            return false;
        return source.Kind == BagLocationKind.Bag || inventory.GetCarryItem(bagIndex) == null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        controller?.SelectBag(bagIndex);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (TryGetDragSource(out var source))
            controller?.BeginDrag(source, icon);
    }

    public void OnDrag(PointerEventData eventData) => controller?.UpdateDrag(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => controller?.EndDrag();
    public void OnDrop(PointerEventData eventData) => controller?.Drop(BagDropTarget.Bag(bagIndex));
    public void OnPointerEnter(PointerEventData eventData) => controller?.SetTargetHighlight(this, BagDropTarget.Bag(bagIndex), true);
    public void OnPointerExit(PointerEventData eventData) => controller?.SetTargetHighlight(this, BagDropTarget.Bag(bagIndex), false);

    /// <summary>选中态着色：青蓝，与局外装备配置面板一致。</summary>
    private static readonly Color SelectedTint = new Color(0.55f, 0.92f, 1f, 1f);

    /// <summary>拖拽悬停时的高亮色。刻意用暖金，与任何品质色都不重叠。</summary>
    private static readonly Color DropTint = new Color(1f, 0.82f, 0.42f, 1f);
}
