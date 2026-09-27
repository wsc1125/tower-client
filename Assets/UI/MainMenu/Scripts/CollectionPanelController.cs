using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 收藏展馆面板：4×4 图标网格 + 文字进度 + 全收集奖励状态。
///
/// 无格子底框、无进度条（进度用文字呈现）。超出 16 件时靠 ScrollRect 纵向滑动。
/// 奖励发放：订阅 <see cref="CollectionService.RewardGranted"/> 后转交 <see cref="PlayerCurrency.Add"/>。
/// </summary>
public sealed class CollectionPanelController : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalGroup;
    [SerializeField] private RectTransform panelBody;
    [SerializeField] private Button maskButton;
    [SerializeField] private Button closeButton;

    [Header("图标")]
    [Tooltip("收藏图标视图，数量等于一屏格数（16）。")]
    [SerializeField] private CollectionSlotView[] slots;

    [Header("文本")]
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text allCollectedText;

    [Header("全收集弹窗")]
    [Tooltip("全收集达成时弹出的提示窗；可为空。")]
    [SerializeField] private GameObject allCollectedPopup;
    [SerializeField] private TMP_Text allCollectedPopupText;
    [SerializeField] private Button allCollectedPopupCloseButton;

    [Header("数据")]
    [SerializeField] private ItemDatabase database;
    [SerializeField] private CollectionCatalog catalog;
    [SerializeField] private PlayerCurrency currency;

    [Header("动画")]
    [SerializeField] private float openDuration = 0.15f;
    [SerializeField] private float closeDuration = 0.1f;

    private Coroutine animationRoutine;

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    private void Awake()
    {
        CollectionService.Configure(database, catalog);
        BindButtons();

        if (modalRoot != null)
            modalRoot.SetActive(false);

        // 全收集弹窗默认隐藏。
        if (allCollectedPopup != null)
            allCollectedPopup.SetActive(false);
    }

    private void OnEnable()
    {
        CollectionService.RewardGranted += HandleRewardGranted;
    }

    private void OnDisable()
    {
        CollectionService.RewardGranted -= HandleRewardGranted;
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

        modalRoot.SetActive(true);
        PlayAnimation(open: true);
        Refresh();
    }

    public void Close()
    {
        if (modalRoot == null || !IsOpen)
            return;

        PlayAnimation(open: false);
    }

    /// <summary>按当前进度刷新全部图标与文本。</summary>
    public void Refresh()
    {
        var items = CollectionService.Collectibles;

        if (slots != null)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    continue;

                if (i < items.Count)
                {
                    var item = items[i];
                    slots[i].gameObject.SetActive(true);
                    slots[i].Bind(item, CollectionService.StateOf(item.Id), HandleSlotClicked);
                }
                else
                {
                    // 图鉴不足一屏时隐藏多余格子。
                    slots[i].gameObject.SetActive(false);
                }
            }
        }

        if (progressText != null)
        {
            var lit = CollectionService.LitCount;
            var total = CollectionService.TotalCount;
            var pending = CollectionService.ObtainedCount - lit;
            progressText.text = $"已点亮 {lit} / {total} · 待点亮 {pending} 件";
        }

        if (allCollectedText != null)
        {
            var done = CollectionService.Progress.AllCollectedRewarded;
            var amount = catalog != null ? catalog.AllCollectedReward : 0;
            allCollectedText.text = done
                ? $"全收集奖励：{amount} 星币  [已达成]"
                : $"全收集奖励：{amount} 星币  [未达成]";
        }
    }

    private void HandleSlotClicked(string id)
    {
        if (!CollectionService.TryLight(id, out _))
            return;

        Refresh();
    }

    private void HandleRewardGranted(int amount)
    {
        if (currency != null)
            currency.Add(amount);

        // 区分「单件奖励」与「全收集奖励」：CollectionService 已把二者分两次广播
        // （先发单件奖励，再单独发全收集奖励），因此这里用金额相等来判断。
        // 该判断可行的依据：全收集奖励（默认 3000）远大于任何单件奖励（30~1000），
        // 且单件奖励按品质表发放，不会恰好等于全收集奖励额。
        if (catalog != null && amount == catalog.AllCollectedReward)
            ShowAllCollectedPopup();
    }

    /// <summary>显示全收集达成弹窗。</summary>
    private void ShowAllCollectedPopup()
    {
        if (allCollectedPopup == null)
            return;

        if (allCollectedPopupText != null)
        {
            var amount = catalog != null ? catalog.AllCollectedReward : 0;
            allCollectedPopupText.text = $"全收集达成！获得 {amount} 星币";
        }

        allCollectedPopup.SetActive(true);
    }

    private void BindButtons()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (maskButton != null)
            maskButton.onClick.AddListener(Close);

        if (allCollectedPopupCloseButton != null)
            allCollectedPopupCloseButton.onClick.AddListener(CloseAllCollectedPopup);
    }

    /// <summary>关闭全收集弹窗。</summary>
    private void CloseAllCollectedPopup()
    {
        if (allCollectedPopup != null)
            allCollectedPopup.SetActive(false);
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
        var from = open ? 0f : 1f;
        var to = open ? 1f : 0f;

        if (modalGroup != null)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                modalGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            modalGroup.alpha = to;
        }

        if (!open && modalRoot != null)
            modalRoot.SetActive(false);

        animationRoutine = null;
    }
}
