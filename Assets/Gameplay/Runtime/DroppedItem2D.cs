using UnityEngine;

public sealed class DroppedItem2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private SpriteRenderer glow;
    [SerializeField] private CircleCollider2D pickupZone;

    /// <summary>光圈宽度 ÷ 物品本体实际宽度。光圈素材是「宽 > 高」的地面椭圆，所以按宽定尺寸。</summary>
    [SerializeField] private float glowWidthRatio = 1.6f;

    /// <summary>地面上的这件物品实例。拾取时整个实例进背包，耐久随之带走。</summary>
    public ItemInstance Item { get; private set; }

    /// <summary>物品模板。便捷访问，等价于 <c>Item.Definition</c>。</summary>
    public ItemDefinition Definition => Item != null ? Item.Definition : null;

    private void Awake()
    {
        WorldRegistry.Register(this);
    }

    private void OnEnable()
    {
        WorldRegistry.Register(this);
    }

    private void OnDisable()
    {
        WorldRegistry.Unregister(this);
    }

    /// 渲染贴图取 worldSprite，缺失时回退 icon；体量按「最大边」等比缩放到 item.WorldSize。
    /// 只调整体量、绝不改变比例 —— 细长的剑保持细长，竖立的箱盒保持竖立。
    public void Initialize(ItemInstance item)
    {
        Item = item;

        var definition = Definition;
        if (definition == null)
        {
            DisableVisual();
            return;
        }

        var sprite = definition.WorldSprite != null ? definition.WorldSprite : definition.Icon;
        if (visual == null || sprite == null)
        {
            DisableVisual();
            return;
        }

        visual.sprite = sprite;
        visual.enabled = true;

        var bounds = sprite.bounds.size;
        var maxSide = Mathf.Max(bounds.x, bounds.y);
        var scale = 1f;
        if (maxSide > 0f && definition.WorldSize > 0f)
        {
            scale = definition.WorldSize / maxSide;
            visual.transform.localScale = Vector3.one * scale;
        }

        if (pickupZone != null)
            pickupZone.radius = definition.WorldSize * 0.5f;

        // 物品贴图可能用自定义轴点（如武器 pivot.x = 0.15），缩放后视觉中心会偏离 transform 原点。
        // 光圈自身是中心轴点，必须按物品贴图的真实中心与底边对齐，否则会「套不住」物品。
        var pivot = sprite.pivot;
        var rect = sprite.rect.size;
        var centerOffset = new Vector2(
            (0.5f - pivot.x / rect.x) * bounds.x * scale,
            (0.5f - pivot.y / rect.y) * bounds.y * scale);

        // 光圈宽度按物品本体「实际宽度」算，而非最大边 worldSize —— 竖长贴图（剑、立箱）的
        // 本体宽度小于 worldSize，若按 worldSize 算光圈会明显宽于物品。
        ApplyGlow(bounds.x * scale, centerOffset, bounds.y * scale);
    }

    /// <summary>光圈贴在物品底边（读起来像「地上的一滩光」），宽度跟随物品本体宽度，自身比例不变。</summary>
    private void ApplyGlow(float itemWorldWidth, Vector2 itemCenterOffset, float itemWorldHeight)
    {
        if (glow == null)
            return;

        var glowSprite = glow.sprite;
        if (glowSprite == null || itemWorldWidth <= 0f)
        {
            glow.enabled = false;
            return;
        }

        glow.enabled = true;

        var glowBounds = glowSprite.bounds.size;
        if (glowBounds.x > 0f)
            glow.transform.localScale = Vector3.one * (itemWorldWidth * glowWidthRatio / glowBounds.x);

        // 光圈中心对齐物品贴图的水平中心，纵向贴到物品底边。
        glow.transform.localPosition = new Vector3(
            itemCenterOffset.x,
            itemCenterOffset.y - itemWorldHeight * 0.5f,
            0f);
    }

    private void DisableVisual()
    {
        if (visual != null)
            visual.enabled = false;

        if (glow != null)
            glow.enabled = false;
    }
}
