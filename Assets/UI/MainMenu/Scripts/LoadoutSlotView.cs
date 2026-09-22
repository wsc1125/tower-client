using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>装备配置面板的三个分区。</summary>
public enum LoadoutSection
{
    Warehouse = 0,
    Carry = 1,
    Equipment = 2,
}

/// <summary>
/// 装备配置面板里的单个格子：底图（frame）+ 品质底板 + 有效高亮 + 物品图标。
/// 品质底板仅在格内有物品时显示：装备槽靠它盖住底图上的类型劊影，
/// 仓库 / 背包格靠它表达物品品质，三类格子共用同一套规则。
/// 支持点击选中与拖拽搬运：拖出时通知控制器生成拖拽图标，
/// 拖入 / 悬停时询问控制器能否接受，用于高亮反馈。
/// </summary>
public sealed class LoadoutSlotView : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image frame;
    [SerializeField] private Image highlight;
    [SerializeField] private Image icon;

    [Header("品质底板")]
    [Tooltip("底板根节点；格内有物品时显示。")]
    [SerializeField] private GameObject qualityPlate;
    [Tooltip("品质描边层，按品质着色。")]
    [SerializeField] private Image qualityBorder;
    [Tooltip("底板内层，深底与品质色的混合。")]
    [SerializeField] private Image qualityFill;

    [Tooltip("该格对应的装备槽；仅 Equipment 分区使用。")]
    [SerializeField] private EquipmentSlotType equipmentSlot = EquipmentSlotType.None;

    private static readonly Color NormalTint = Color.white;
    private static readonly Color SelectedTint = new Color(0.55f, 0.92f, 1f, 1f);
    private static readonly Color DisabledTint = new Color(1f, 1f, 1f, 0.32f);

    /// <summary>拖拽悬停时的高亮色。刻意用暖金，与任何品质色都不重叠。</summary>
    private static readonly Color DropTint = new Color(1f, 0.82f, 0.42f, 1f);

    private LoadoutPanelController controller;
    private LoadoutSection section;
    private int index;
    private ItemDefinition item;
    private bool interactable = true;
    private bool selected;

    public LoadoutSection Section => section;
    public int Index => index;
    public EquipmentSlotType EquipmentSlot => equipmentSlot;

    /// <summary>当前格子里的物品；空槽时为 null。拖拽来源判定靠它。</summary>
    public ItemDefinition Item => item;

    public void Bind(LoadoutPanelController owner, LoadoutSection ownerSection, int slotIndex)
    {
        controller = owner;
        section = ownerSection;
        index = slotIndex;
        SetInteractable(true);
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

    public void SetInteractable(bool value)
    {
        interactable = value;
        ApplyVisual();
    }

    public void SetSelected(bool value)
    {
        selected = value;
        ApplyVisual();
    }

    /// <summary>拖拽悬停反馈；离开时调用 false 会恢复选中 / 禁用态。</summary>
    public void SetDropHighlight(bool value)
    {
        if (value && CanAcceptDrop())
        {
            if (highlight != null)
            {
                highlight.enabled = true;
                highlight.color = DropTint;
            }
            else if (frame != null)
            {
                frame.color = DropTint;
            }

            return;
        }

        if (highlight != null)
            highlight.color = Color.white;

        ApplyVisual();
    }

    /// <summary>本格能否作为拖拽落点：仓库格与背包格看格号是否有效，装备槽看是否启用。</summary>
    private bool CanAcceptDrop()
    {
        if (controller == null)
            return false;

        return section != LoadoutSection.Equipment || interactable;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller == null || eventData.button != PointerEventData.InputButton.Left)
            return;

        controller.Select(section, index);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (controller == null || item == null)
            return;

        if (section == LoadoutSection.Equipment && !interactable)
            return;

        controller.BeginDrag(BuildDragSource(), icon);
    }

    public void OnDrag(PointerEventData eventData)
    {
        controller?.UpdateDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        controller?.EndDrag();
    }

    public void OnDrop(PointerEventData eventData)
    {
        controller?.Drop(BuildDropTarget());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        controller?.SetTargetHighlight(this, BuildDropTarget(), true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        controller?.SetTargetHighlight(this, BuildDropTarget(), false);
    }

    private LoadoutDragSource BuildDragSource()
    {
        switch (section)
        {
            case LoadoutSection.Carry:
                return LoadoutDragSource.FromCarry(index, item);
            case LoadoutSection.Equipment:
                return LoadoutDragSource.FromEquipment(equipmentSlot, item);
            default:
                return LoadoutDragSource.FromWarehouse(index, item);
        }
    }

    private LoadoutDropTarget BuildDropTarget()
    {
        switch (section)
        {
            case LoadoutSection.Carry:
                return LoadoutDropTarget.Carry(index);
            case LoadoutSection.Equipment:
                return LoadoutDropTarget.Equipment(equipmentSlot);
            default:
                return LoadoutDropTarget.Warehouse(index);
        }
    }

    private void ApplyVisual()
    {
        if (frame == null)
            return;

        // 有独立高亮素材的格子（仓库格 / 背包格）用叠高亮表达选中；
        // 没有高亮素材的（装备格）退化为整体着色。
        if (highlight != null)
        {
            highlight.color = Color.white;
            highlight.enabled = selected && interactable;
            frame.color = interactable ? NormalTint : DisabledTint;
        }
        else
        {
            frame.color = !interactable ? DisabledTint : (selected ? SelectedTint : NormalTint);
        }
    }
}
