using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【已弃用】旧仓库面板：展示背包物品、查看详情并出售。
/// 已被 LoadoutPanel 取代（分页网格 + 装备槽 + 携带背包 + 拖拽配装 + 品质底板 + 出售），
/// Btn_Storage 现在接到 LoadoutPanelController。本文件与 prefab 暂时保留以便回滚，请勿再新增功能。
/// 数据来自场景中唯一的 <see cref="InventoryHolder"/>，与战斗背包共享同一份库存；
/// 界面上所有美术素材均按原图直接使用，不做染色。
/// </summary>
public sealed class WarehousePanelController : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalGroup;
    [SerializeField] private RectTransform panelBody;
    [SerializeField] private Button maskButton;
    [SerializeField] private Button closeButton;

    [Header("内容")]
    [SerializeField] private WarehouseSlotView[] slots;
    [SerializeField] private Image itemPreview;
    [SerializeField] private TMP_Text itemName;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private Button sellButton;
    [SerializeField] private string emptyNameText = "未选择";
    [SerializeField] private string emptyPriceText = "金币";

    [Header("分页")]
    [Tooltip("每页显示的格子数，必须与实际格子数量一致。")]
    [SerializeField] private int pageSlotCount = 20;
    [SerializeField] private Button prevPageButton;
    [SerializeField] private Button nextPageButton;
    [SerializeField] private TMP_Text prevPageLabel;
    [SerializeField] private TMP_Text nextPageLabel;
    [SerializeField] private TMP_Text pageLabel;
    [SerializeField] private Color pageDisabledColor = new Color(0.55f, 0.6f, 0.68f, 1f);

    [Header("数据")]
    [SerializeField] private InventoryHolder holder;
    [SerializeField] private PlayerCurrency currency;

    [Header("动画")]
    [SerializeField] private float openDuration = 0.22f;
    [SerializeField] private float closeDuration = 0.12f;
    [SerializeField] private float bodyStartScale = 0.88f;
    [SerializeField] private float bodyCloseScale = 0.92f;

    [Header("适配")]
    [Tooltip("面板设计尺寸；画布比它小时按比例整体缩小，避免超出屏幕。")]
    [SerializeField] private Vector2 designSize = new Vector2(1603f, 938f);

    [Header("外观")]
    [SerializeField] private Color sellDisabledColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    private Coroutine animationRoutine;
    private Image sellButtonImage;
    private float fitScale = 1f;
    private int selectedIndex = -1;

    /// <summary>当前读取的玩家库存；未接上 <see cref="InventoryHolder"/> 时为 null。</summary>
    public Inventory Model { get; private set; }

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    /// <summary>当前选中的格号；语义为「页内索引」，未选中时为 -1。</summary>
    public int SelectedIndex => selectedIndex;

    /// <summary>当前页，从 0 开始。</summary>
    public int CurrentPage { get; private set; }

    /// <summary>每页格子数；配置非法时退化为 1，避免除零。</summary>
    private int PageSlotCount => pageSlotCount > 0 ? pageSlotCount : 1;

    /// <summary>总页数，至少为 1。</summary>
    public int TotalPages => Mathf.Max(1, Mathf.CeilToInt(Inventory.BagCapacity / (float)PageSlotCount));

    private void Awake()
    {
        if (pageSlotCount <= 0)
            Debug.LogError("仓库的每页格子数配置为 0 或负数，分页将退化为单页显示。", this);

        ResolveModel();
        BindSlots();
        BindButtons();
        UpdateFitScale();

        // 面板默认隐藏：即使 prefab 里忘了关，也在首帧前收起来。
        if (modalRoot != null)
            modalRoot.SetActive(false);

        Refresh();
        SelectSlot(-1);
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdateFitScale();
    }

    /// <summary>画布比设计尺寸小时整体等比缩小，保证面板始终完整可见。</summary>
    private void UpdateFitScale()
    {
        var rect = transform as RectTransform;
        if (rect == null || rect.rect.width <= 0f || rect.rect.height <= 0f
            || designSize.x <= 0f || designSize.y <= 0f)
        {
            fitScale = 1f;
        }
        else
        {
            fitScale = Mathf.Min(1f, rect.rect.width / designSize.x, rect.rect.height / designSize.y);
        }

        // 动画进行中由动画控制缩放，避免两边互相覆盖。
        if (panelBody != null && animationRoutine == null)
            panelBody.localScale = Vector3.one * fitScale;
    }

    private void OnDestroy()
    {
        if (Model != null)
            Model.Changed -= Refresh;
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (modalRoot == null || IsOpen)
            return;

        // 每次打开都回到第一页且从「未选择」开始，避免残留上一次的页与详情。
        GoToPage(0);
        modalRoot.SetActive(true);
        PlayAnimation(open: true);
    }

    public void Close()
    {
        if (modalRoot == null || !IsOpen)
            return;

        SelectSlot(-1);
        PlayAnimation(open: false);
    }

    /// <summary>翻到指定页（从 0 开始）；越界会被夹到有效范围，切页后清空选中与详情。</summary>
    public void GoToPage(int page)
    {
        CurrentPage = Mathf.Clamp(page, 0, TotalPages - 1);
        SelectSlot(-1);
        Refresh();
    }

    public void PrevPage()
    {
        GoToPage(CurrentPage - 1);
    }

    public void NextPage()
    {
        GoToPage(CurrentPage + 1);
    }

    /// <summary>选中当前页的某个格子并刷新右侧详情；传 -1 表示清空选中。</summary>
    public void SelectSlot(int index)
    {
        // 最后一页可能多出空格，超出总容量的格号一律按「未选中」处理。
        if (index >= 0 && ToBagIndex(index) < 0)
            index = -1;

        selectedIndex = index;

        if (slots != null)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    slots[i].SetSelected(i == index);
            }
        }

        ShowDetail(index >= 0 && Model != null ? Model.GetBagItem(ToBagIndex(index)) : null);
    }

    /// <summary>出售当前选中的物品：先校验货币服务，再扣物品、存档并到账。</summary>
    public void SellSelected()
    {
        if (selectedIndex < 0 || Model == null)
            return;

        var bagIndex = ToBagIndex(selectedIndex);
        var item = Model.GetBagItem(bagIndex);
        if (item == null)
            return;

        if (currency == null)
        {
            Debug.LogError("仓库缺少 PlayerCurrency 引用，已取消出售，避免物品凭空消失。", this);
            return;
        }

        if (!Model.TryRemoveFromBag(bagIndex))
            return;

        // 立即写入存档，保证「扣物品」与「加货币」两侧都持久化。
        PlayerInventoryStore.Save(Model, holder != null ? holder.Database : null);
        currency.Add(item.Value);

        SelectSlot(-1);
    }

    /// <summary>页内格号换算为背包真实索引；超出总容量的格子返回 -1。</summary>
    private int ToBagIndex(int slotIndex)
    {
        if (slotIndex < 0)
            return -1;

        var index = CurrentPage * PageSlotCount + slotIndex;
        return index < Inventory.BagCapacity ? index : -1;
    }

    private void ResolveModel()
    {
        if (Model != null)
            return;

        var source = holder != null ? holder : FindObjectOfType<InventoryHolder>(true);
        if (source == null)
        {
            Debug.LogError("场景中没有 InventoryHolder，仓库将无法读取玩家库存。", this);
            return;
        }

        holder = source;
        Model = source.Model;
        Model.Changed += Refresh;
    }

    private void BindButtons()
    {
        if (maskButton != null)
            maskButton.onClick.AddListener(Close);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (prevPageButton != null)
            prevPageButton.onClick.AddListener(PrevPage);

        if (nextPageButton != null)
            nextPageButton.onClick.AddListener(NextPage);

        if (sellButton != null)
        {
            sellButtonImage = sellButton.targetGraphic as Image;
            if (sellButtonImage == null)
                sellButtonImage = sellButton.GetComponent<Image>();

            sellButton.onClick.AddListener(SellSelected);
        }
    }

    private void BindSlots()
    {
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].Bind(this, i);
        }
    }

    private void Refresh()
    {
        if (slots != null)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    slots[i].SetItem(Model != null ? Model.GetBagItem(ToBagIndex(i)) : null);
            }
        }

        // 选中格的内容可能已被外部改动，同步刷新右侧详情。
        if (selectedIndex >= 0)
            ShowDetail(Model != null ? Model.GetBagItem(ToBagIndex(selectedIndex)) : null);

        RefreshPager();
    }

    /// <summary>刷新页码文本与翻页按钮的可用状态。</summary>
    private void RefreshPager()
    {
        var hasPrev = CurrentPage > 0;
        var hasNext = CurrentPage < TotalPages - 1;

        if (prevPageButton != null)
            prevPageButton.interactable = hasPrev;

        if (nextPageButton != null)
            nextPageButton.interactable = hasNext;

        if (prevPageLabel != null)
            prevPageLabel.color = hasPrev ? Color.white : pageDisabledColor;

        if (nextPageLabel != null)
            nextPageLabel.color = hasNext ? Color.white : pageDisabledColor;

        if (pageLabel != null)
            pageLabel.text = (CurrentPage + 1) + " / " + TotalPages;
    }

    private void ShowDetail(ItemDefinition item)
    {
        var hasItem = item != null;

        if (itemPreview != null)
        {
            itemPreview.sprite = hasItem ? item.Icon : null;
            itemPreview.enabled = hasItem && item.Icon != null;
        }

        if (itemName != null)
            itemName.text = hasItem ? item.DisplayName : emptyNameText;

        if (priceText != null)
            priceText.text = hasItem ? item.Value.ToString() : emptyPriceText;

        if (sellButton != null)
        {
            sellButton.interactable = hasItem;
            if (sellButtonImage != null)
                sellButtonImage.color = hasItem ? Color.white : sellDisabledColor;
        }
    }

    private void PlayAnimation(bool open)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(Animate(open));
    }

    private IEnumerator Animate(bool open)
    {
        var duration = open ? openDuration : closeDuration;
        var fromAlpha = modalGroup != null ? modalGroup.alpha : 1f;
        var toAlpha = open ? 1f : 0f;
        var fromScale = panelBody != null ? panelBody.localScale.x : fitScale;
        var toScale = open ? fitScale : bodyCloseScale * fitScale;

        if (open)
        {
            fromAlpha = 0f;
            fromScale = bodyStartScale * fitScale;
            Apply(fromAlpha, fromScale);
        }

        if (duration <= 0f)
        {
            Apply(toAlpha, toScale);
            FinishAnimation(open);
            yield break;
        }

        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / duration);

            // 打开用 ease-out（收尾变慢，读起来像「弹入」）；关闭用线性收得干脆。
            var eased = open ? 1f - (1f - progress) * (1f - progress) : progress;
            Apply(Mathf.Lerp(fromAlpha, toAlpha, eased), Mathf.Lerp(fromScale, toScale, eased));
            yield return null;
        }

        Apply(toAlpha, toScale);
        FinishAnimation(open);
    }

    private void Apply(float alpha, float scale)
    {
        if (modalGroup != null)
            modalGroup.alpha = alpha;

        if (panelBody != null)
            panelBody.localScale = Vector3.one * scale;
    }

    private void FinishAnimation(bool open)
    {
        animationRoutine = null;

        if (!open && modalRoot != null)
            modalRoot.SetActive(false);
    }
}
