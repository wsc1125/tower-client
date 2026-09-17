using UnityEngine;

public sealed class DroppedItem2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private SpriteRenderer glow;
    [SerializeField] private CircleCollider2D pickupZone;

    /// <summary>光圈宽度 ÷ 物品体量。光圈素材是「宽 > 高」的地面椭圆，所以按宽定尺寸。</summary>
    [SerializeField] private float glowWidthRatio = 1.6f;

    public ItemDefinition Item { get; private set; }

    /// 渲染贴图取 worldSprite，缺失时回退 icon；体量按「最大边」等比缩放到 item.WorldSize。
    /// 只调整体量、绝不改变比例 —— 细长的剑保持细长，竖立的箱盒保持竖立。
    public void Initialize(ItemDefinition item)
    {
        Item = item;

        if (item == null)
        {
            DisableVisual();
            return;
        }

        var sprite = item.WorldSprite != null ? item.WorldSprite : item.Icon;
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
        if (maxSide > 0f && item.WorldSize > 0f)
        {
            scale = item.WorldSize / maxSide;
            visual.transform.localScale = Vector3.one * scale;
        }

        if (pickupZone != null)
            pickupZone.radius = item.WorldSize * 0.5f;

        ApplyGlow(item.WorldSize, bounds.y * scale);
    }

    /// <summary>光圈贴在物品底边（读起来像「地上的一滩光」），宽度跟随物品体量，自身比例不变。</summary>
    private void ApplyGlow(float worldSize, float itemWorldHeight)
    {
        if (glow == null)
            return;

        var glowSprite = glow.sprite;
        if (glowSprite == null || worldSize <= 0f)
        {
            glow.enabled = false;
            return;
        }

        glow.enabled = true;

        var glowBounds = glowSprite.bounds.size;
        if (glowBounds.x > 0f)
            glow.transform.localScale = Vector3.one * (worldSize * glowWidthRatio / glowBounds.x);

        glow.transform.localPosition = new Vector3(0f, -itemWorldHeight * 0.5f, 0f);
    }

    private void DisableVisual()
    {
        if (visual != null)
            visual.enabled = false;

        if (glow != null)
            glow.enabled = false;
    }
}
