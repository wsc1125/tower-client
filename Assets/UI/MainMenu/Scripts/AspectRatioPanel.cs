using UnityEngine;

/// <summary>
/// 让面板主体（Window）始终保持设计尺寸的宽高比，并按父级尺寸等比缩放。
///
/// 实现方式：**保持 sizeDelta 为设计尺寸不变，改 localScale**。
/// 这样所有子元素（固定像素坐标）会随父级一起缩放，相对位置永远不变，
/// 不会出现「父级 rect 变小、子元素还按原像素定位」的错位。
///
/// 为什么不用 AspectRatioFitter：实测在该 prefab 上 FitInParent 模式不生效
/// （比例始终等于画布比例），因为 ARF 依赖布局系统回调，而父级也是 stretch 锚点，
/// 两者互相干扰。这里改为直接监听尺寸变化并显式赋值。
///
/// 注意：**不要加 [ExecuteAlways]**。本脚本会修改自身的缩放，
/// 若在编辑模式下运行，这些运行时值会被写进场景实例的 override，
/// 导致「Scene 视图看 prefab 正常、Play 模式看却不一样」。
///
/// 用法：挂在 Window 上，设置 designSize 为底图的像素尺寸。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class AspectRatioPanel : MonoBehaviour
{
    [Tooltip("设计尺寸（底图像素尺寸），用于确定宽高比。")]
    [SerializeField] private Vector2 designSize = new Vector2(1672f, 941f);

    [Tooltip("相对父级的内边距（像素），避免面板贴到屏幕边缘。")]
    [SerializeField] private float padding = 0f;

    private RectTransform rect;
    private RectTransform parentRect;

    private void OnEnable()
    {
        rect = (RectTransform)transform;
        parentRect = rect.parent as RectTransform;
        Apply();
    }

    private void OnRectTransformDimensionsChange()
    {
        Apply();
    }

    private void Apply()
    {
        if (rect == null) rect = (RectTransform)transform;
        if (parentRect == null) parentRect = rect.parent as RectTransform;
        if (parentRect == null || designSize.x <= 0f || designSize.y <= 0f)
            return;

        var available = parentRect.rect.size - Vector2.one * (padding * 2f);
        if (available.x <= 0f || available.y <= 0f)
            return;

        // 尺寸固定为设计尺寸，只改缩放：子元素随父级一起缩放，相对位置不变。
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = designSize;

        var scale = Mathf.Min(available.x / designSize.x, available.y / designSize.y);
        rect.localScale = Vector3.one * scale;
    }
}
