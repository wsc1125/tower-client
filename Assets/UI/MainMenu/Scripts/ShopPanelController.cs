using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主界面「商店」面板：以商品卡片网格展示可售的武器与护甲，底部为选中商品的详情与购买按钮。
/// 目录来自 <see cref="InventoryHolder.Database"/> 中所有可装备物品（排除消耗品）；
/// 购买成功后物品进入背包并扣除星空币。所有美术素材按原图直接使用，不做染色。
/// </summary>
public sealed class ShopPanelController : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalGroup;
    [SerializeField] private RectTransform panelBody;
    [SerializeField] private Button maskButton;
    [SerializeField] private Button closeButton;

    [Header("商品网格")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private ShopSlotView[] slots;

    [Header("底部详情")]
    [SerializeField] private Image selectedIcon;
    [SerializeField] private TMP_Text selectedName;
    [SerializeField] private TMP_Text selectedPrice;
    [SerializeField] private Button buyButton;
    [SerializeField] private string emptyNameText = "未选择";
    [SerializeField] private string emptyPriceText = "";

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
    [SerializeField] private Vector2 designSize = new Vector2(1533f, 1026f);

    private Coroutine animationRoutine;
    private float fitScale = 1f;
    private int selectedIndex = -1;
    private List<ItemDefinition> catalog;

    /// <summary>当前读取的玩家库存；未接上 <see cref="InventoryHolder"/> 时为 null。</summary>
    public Inventory Model { get; private set; }

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    public int SelectedIndex => selectedIndex;

    private void Awake()
    {
        ResolveModel();
        LoadCatalog();
        BindSlots();
        BindButtons();
        UpdateFitScale();

        if (currency != null)
            currency.Changed += OnCurrencyChanged;

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

    private void OnDestroy()
    {
        if (Model != null)
            Model.Changed -= Refresh;

        if (currency != null)
            currency.Changed -= OnCurrencyChanged;
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

        if (panelBody != null && animationRoutine == null)
            panelBody.localScale = Vector3.one * fitScale;
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

        // 每次打开都从「未选择」开始，避免残留上一次的详情与购买状态。
        SelectSlot(-1);
        modalRoot.SetActive(true);
        ResetScroll();
        PlayAnimation(open: true);
    }

    public void Close()
    {
        if (modalRoot == null || !IsOpen)
            return;

        SelectSlot(-1);
        PlayAnimation(open: false);
    }

    /// <summary>把商品列表滚回最左端；面板刚被激活时需先强制刷新布局，否则归一化坐标不生效。</summary>
    private void ResetScroll()
    {
        if (scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        scrollRect.StopMovement();
        scrollRect.horizontalNormalizedPosition = 0f;
    }

    /// <summary>选中某个商品卡片并刷新底部详情；传 -1 表示清空选中。</summary>
    public void SelectSlot(int index)
    {
        selectedIndex = index;

        if (slots != null)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    slots[i].SetSelected(i == index);
            }
        }

        ShowDetail(GetCatalogItem(index));
    }

    /// <summary>购买当前选中的商品：余额充足且背包有空位时扣款并入库；否则不生效。</summary>
    public void BuySelected()
    {
        if (selectedIndex < 0 || Model == null)
            return;

        var item = GetCatalogItem(selectedIndex);
        if (item == null)
            return;

        if (currency == null)
        {
            Debug.LogError("商店缺少 PlayerCurrency 引用，已取消购买，避免扣款与入库不同步。", this);
            return;
        }

        // 纯逻辑校验：余额不足 / 背包满 / 未定价都会在此失败。
        if (!ShopService.TryPurchase(item, Model, currency.Balance, out var remaining))
            return;

        // 提交余额变化（差额即花费）并落盘库存。
        currency.TrySpend(currency.Balance - remaining);
        PlayerInventoryStore.Save(Model, holder != null ? holder.Database : null);
    }

    private void ResolveModel()
    {
        if (Model != null)
            return;

        var source = holder != null ? holder : FindObjectOfType<InventoryHolder>(true);
        if (source == null)
        {
            Debug.LogError("场景中没有 InventoryHolder，商店将无法读取玩家库存。", this);
            return;
        }

        holder = source;
        Model = source.Model;
        Model.Changed += Refresh;
    }

    private void LoadCatalog()
    {
        catalog = ShopService.GetCatalog(holder != null ? holder.Database : null);
    }

    private ItemDefinition GetCatalogItem(int index)
    {
        return index >= 0 && catalog != null && index < catalog.Count ? catalog[index] : null;
    }

    private void BindButtons()
    {
        if (maskButton != null)
            maskButton.onClick.AddListener(Close);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (buyButton != null)
            buyButton.onClick.AddListener(BuySelected);
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
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].SetItem(GetCatalogItem(i));
        }

        // 选中格的内容可能已被外部改动，同步刷新底部详情。
        if (selectedIndex >= 0)
            ShowDetail(GetCatalogItem(selectedIndex));
    }

    private void OnCurrencyChanged(int balance)
    {
        // 余额变化影响购买按钮可用状态，刷新选中项的详情。
        if (selectedIndex >= 0)
            ShowDetail(GetCatalogItem(selectedIndex));
    }

    private void ShowDetail(ItemDefinition item)
    {
        var hasItem = item != null;

        if (selectedIcon != null)
        {
            selectedIcon.sprite = hasItem ? item.Icon : null;
            selectedIcon.enabled = hasItem && item.Icon != null;
        }

        if (selectedName != null)
            selectedName.text = hasItem ? item.DisplayName : emptyNameText;

        if (selectedPrice != null)
            selectedPrice.text = hasItem ? PriceFormatter.Format(item.BuyPrice) : emptyPriceText;

        if (buyButton != null)
        {
            // 购买按钮的可用外观由 SpriteState 切换，这里只控制可交互性。
            buyButton.interactable = hasItem && item.BuyPrice > 0
                && currency != null && currency.Balance >= item.BuyPrice;
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
