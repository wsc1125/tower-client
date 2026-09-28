using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 受击红边：玩家掉血时屏幕四周泛起一圈红色，随后淡出。
/// 与 <see cref="StatusPanelController"/> 同属 BattleHUD，只负责表现，不参与任何数值结算。
///
/// 贴图在运行时程序化生成（中心透明、边缘红），因此不依赖任何美术素材，
/// 也避免了「一张纯色图 + 拉伸」在横屏下边缘厚度不均的问题。
/// </summary>
public sealed class DamageVignetteController : MonoBehaviour
{
    [Tooltip("承载红边的 Image。留空则自动在本对象上创建一张全屏 Image。")]
    [SerializeField] private Image vignetteImage;

    [Tooltip("受击瞬间红边的最高不透明度。")]
    [Range(0f, 1f)]
    [SerializeField] private float peakAlpha = 0.55f;

    [Tooltip("红边从峰值淡出到 0 所需时间（秒）。")]
    [SerializeField] private float fadeDuration = 0.35f;

    [Tooltip("红边颜色。")]
    [SerializeField] private Color vignetteColor = new Color(0.75f, 0.05f, 0.05f, 1f);

    [Tooltip("生成贴图的分辨率。越低越省内存，越高边缘越平滑。")]
    [SerializeField] private int textureResolution = 128;

    private float currentAlpha;
    private float fadeSpeed;

    /// <summary>当前红边不透明度。供测试查询。</summary>
    public float CurrentAlpha => currentAlpha;

    private void Awake()
    {
        EnsureImage();
        ApplyAlpha(0f);
    }

    private void Update()
    {
        if (currentAlpha <= 0f)
            return;

        currentAlpha = Mathf.Max(0f, currentAlpha - fadeSpeed * Time.deltaTime);
        ApplyAlpha(currentAlpha);
    }

    /// <summary>
    /// 触发一次受击红边。重复调用会重置到峰值（连续受击时红边不中断），
    /// 因此不需要额外的「已在播放中」判断。
    /// </summary>
    public void Flash()
    {
        EnsureImage();

        currentAlpha = Mathf.Clamp01(peakAlpha);
        fadeSpeed = currentAlpha / Mathf.Max(0.01f, fadeDuration);
        ApplyAlpha(currentAlpha);
    }

    private void EnsureImage()
    {
        if (vignetteImage == null)
            vignetteImage = GetComponent<Image>();

        if (vignetteImage == null)
            vignetteImage = gameObject.AddComponent<Image>();

        if (vignetteImage.sprite == null)
            vignetteImage.sprite = BuildVignetteSprite();

        // 红边必须铺满全屏且不拦截点击，否则会挡住摇杆与攻击按钮。
        vignetteImage.raycastTarget = false;
        vignetteImage.color = vignetteColor;
    }

    private void ApplyAlpha(float alpha)
    {
        if (vignetteImage == null)
            return;

        var color = vignetteColor;
        color.a = alpha;
        vignetteImage.color = color;
    }

    /// <summary>
    /// 程序化生成一张「中心透明、边缘红」的方形贴图。
    /// 用径向距离的平方做衰减，比线性衰减更集中，边缘过渡也更自然。
    /// </summary>
    private Sprite BuildVignetteSprite()
    {
        var size = Mathf.Clamp(textureResolution, 16, 512);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var center = (size - 1) * 0.5f;
        var maxDistance = center;
        var pixels = new Color32[size * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x - center) / maxDistance;
                var dy = (y - center) / maxDistance;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);

                // 中心 45% 半径内完全透明，之后向外平滑加深到边缘全不透明。
                var t = Mathf.InverseLerp(0.45f, 1f, distance);
                var alpha = t * t;

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f);
    }
}
