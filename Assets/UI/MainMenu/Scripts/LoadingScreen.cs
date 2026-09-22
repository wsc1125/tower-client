using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 加载页面：全屏遮罩 + 进度条 + 状态文案 + 百分比。
/// 只负责展示，不认识任何场景；加载流程由 <see cref="DepartFlow"/> 驱动。
/// </summary>
public sealed class LoadingScreen : MonoBehaviour
{
    [Header("显隐")]
    [Tooltip("被整块显隐的遮罩层，默认关闭。")]
    [SerializeField] private GameObject overlay;

    [Header("进度")]
    [Tooltip("进度填充条，用 fillAmount 表示 0~1。")]
    [SerializeField] private Image progressFill;

    [Tooltip("百分比文字。")]
    [SerializeField] private TMP_Text percentText;

    [Tooltip("状态文案，如「正在出发…」。")]
    [SerializeField] private TMP_Text statusText;

    private void Awake()
    {
        // 加载页只能由出发流程唤起，任何情况下都不该在启动时露出来。
        Hide();
    }

    /// <summary>显示加载页并清零进度。状态文案传空则保留预制体里的默认文案。</summary>
    public void Show(string status = null)
    {
        if (overlay != null)
            overlay.SetActive(true);

        SetProgress(0f);
        SetStatus(status);
    }

    /// <summary>隐藏加载页。</summary>
    public void Hide()
    {
        if (overlay != null)
            overlay.SetActive(false);
    }

    /// <summary>设置进度，入参自动钳制到 0~1。</summary>
    public void SetProgress(float value)
    {
        var progress = Mathf.Clamp01(value);

        if (progressFill != null)
            progressFill.fillAmount = progress;

        if (percentText != null)
            percentText.text = Mathf.RoundToInt(progress * 100f) + "%";
    }

    /// <summary>设置状态文案，传空则不改动当前文案。</summary>
    public void SetStatus(string status)
    {
        if (statusText != null && !string.IsNullOrEmpty(status))
            statusText.text = status;
    }
}
