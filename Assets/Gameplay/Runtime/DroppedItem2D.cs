using UnityEngine;

public sealed class DroppedItem2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private CircleCollider2D pickupZone;

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
        if (maxSide > 0f && item.WorldSize > 0f)
            visual.transform.localScale = Vector3.one * (item.WorldSize / maxSide);

        if (pickupZone != null)
            pickupZone.radius = item.WorldSize * 0.5f;
    }

    private void DisableVisual()
    {
        if (visual != null)
            visual.enabled = false;
    }
}
