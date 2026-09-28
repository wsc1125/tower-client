using UnityEngine;

/// <summary>
/// 战斗房间里的实体掩体：挡住玩家与敌人的移动，也挡住子弹。
/// 只做「存在即阻挡」——本组件不参与任何数值结算，因此不需要 Update，
/// 也不需要知道谁撞了它。
/// 之所以单独建一个组件而不是裸碰撞体，是为了让生成方（ObstaclePlacer）
/// 能按类型查找并统一清理，同时给后续「可破坏掩体」留出扩展点。
///
/// 尺寸与碰撞体全部由 prefab 自己决定，本组件只负责「按贴图比例把碰撞体摆正」：
/// 不同掩体形状差异很大（方形补给箱、细长能量柱），共用一套碰撞体尺寸必然错位，
/// 因此碰撞体尺寸在运行时按贴图实际比例推导，新增 prefab 无需改代码。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class Obstacle2D : MonoBehaviour
{
    /// <summary>判定「不透明像素」的 alpha 阈值（0~255）。低于此值视为透明留白。</summary>
    private const byte AlphaThreshold = 16;

    [Tooltip("掩体在游戏里的目标世界宽度。贴图分辨率差异大时按此归一，避免大小失衡。")]
    [SerializeField] private float targetWorldWidth = 1.2f;

    [Tooltip("是否按目标世界宽度自动归一缩放。关掉则完全用 prefab 上的缩放。")]
    [SerializeField] private bool normalizeScale = true;

    [Tooltip("是否按贴图比例自动摆正碰撞体。关掉则完全用 prefab 上的碰撞体尺寸。")]
    [SerializeField] private bool fitColliderToSprite = true;

    [Tooltip("碰撞体相对贴图可见区域的收缩系数。1 表示完全贴合，略小于 1 可避免视觉上「没碰到却被挡」。")]
    [Range(0.1f, 1f)]
    [SerializeField] private float colliderFitRatio = 0.92f;

    [SerializeField] private SpriteRenderer spriteRenderer;

    private Collider2D bodyCollider;

    /// <summary>掩体的碰撞体，供生成方做重叠检测。</summary>
    public Collider2D BodyCollider => bodyCollider != null ? bodyCollider : bodyCollider = GetComponent<Collider2D>();

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (normalizeScale)
            ApplyScale();

        if (fitColliderToSprite)
            FitCollider();
    }

    /// <summary>
    /// 按「贴图世界宽 ÷ 目标世界宽」归一缩放。
    /// 与 WeaponMountSlot.ApplyScale 同构：素材像素宽差异很大时，
    /// 共用同一缩放会让大贴图变成巨物，必须逐张归一。
    /// </summary>
    private void ApplyScale()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        var worldWidth = spriteRenderer.sprite.bounds.size.x;
        if (worldWidth <= 0.0001f || targetWorldWidth <= 0f)
            return;

        var scale = targetWorldWidth / worldWidth;
        spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>
    /// 按贴图比例摆正碰撞体。贴图是「宽高比各异」的素材（方形箱子 / 细长柱子），
    /// 若碰撞体固定为正方形，细长柱子会出现大片「空气墙」。
    /// 这里读贴图的世界尺寸，按同一比例设置碰撞体，使碰撞范围与视觉一致。
    ///
    /// 关键：碰撞体必须与贴图处在**同一个缩放空间**。贴图缩放写在 Visual 子节点上，
    /// 而碰撞体挂在根节点（缩放为 1），若直接把「贴图世界尺寸」写进根节点的碰撞体，
    /// 会得到比视觉小一个数量级的碰撞体（实测能量柱 0.30 vs 视觉 0.70）。
    /// 因此这里把碰撞体尺寸换算回根节点局部空间：世界尺寸 ÷ 根节点缩放。
    /// 只处理 BoxCollider2D —— 其他形状（圆形 / 多边形）由 prefab 自行配置，不做覆盖。
    /// </summary>
    private void FitCollider()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        var box = GetComponent<BoxCollider2D>();
        if (box == null)
            return;

        var sprite = spriteRenderer.sprite;
        var spriteSize = sprite.bounds.size;
        if (spriteSize.x <= 0.0001f || spriteSize.y <= 0.0001f)
            return;

        // 素材四周常有大量透明留白（实测能量柱可见区域仅占贴图 47% 宽），
        // 直接按整张贴图算碰撞体会得到一大圈「空气墙」。
        // 因此优先按不透明像素的实际范围来定碰撞体，取不到时才退回整张贴图。
        if (TryGetOpaqueSize(sprite, out var opaqueSize))
        {
            spriteSize = opaqueSize;
        }

        var ratio = Mathf.Clamp(colliderFitRatio, 0.1f, 1f);

        // 贴图世界尺寸 = 贴图原始尺寸 × Visual 节点缩放。
        var visualScale = spriteRenderer.transform.localScale;
        var worldSize = new Vector2(spriteSize.x * visualScale.x, spriteSize.y * visualScale.y) * ratio;

        // 换算回根节点局部空间：根节点缩放为 1 时结果不变；若 prefab 根节点带缩放也能正确抵消。
        var rootScale = transform.localScale;
        var localX = Mathf.Abs(rootScale.x) > 0.0001f ? worldSize.x / Mathf.Abs(rootScale.x) : worldSize.x;
        var localY = Mathf.Abs(rootScale.y) > 0.0001f ? worldSize.y / Mathf.Abs(rootScale.y) : worldSize.y;

        box.size = new Vector2(localX, localY);
        box.offset = Vector2.zero;
    }

    /// <summary>
    /// 求贴图里不透明像素的实际世界尺寸。贴图不可读（未勾 Read/Write）时返回 false，
    /// 由调用方退回整张贴图尺寸。
    /// </summary>
    private static bool TryGetOpaqueSize(Sprite sprite, out Vector2 size)
    {
        size = Vector2.zero;

        var texture = sprite.texture;
        if (texture == null || !texture.isReadable)
            return false;

        var rect = sprite.textureRect;
        var xMin = (int)rect.xMin;
        var xMax = (int)rect.xMax;
        var yMin = (int)rect.yMin;
        var yMax = (int)rect.yMax;

        Color32[] pixels;
        try
        {
            pixels = texture.GetPixels32();
        }
        catch (UnityException)
        {
            // 贴图未开 Read/Write 时 GetPixels32 会抛异常，退回整张贴图。
            return false;
        }

        var textureWidth = texture.width;
        var minX = xMax;
        var maxX = xMin - 1;
        var minY = yMax;
        var maxY = yMin - 1;

        for (var y = yMin; y < yMax; y++)
        {
            var rowOffset = y * textureWidth;
            for (var x = xMin; x < xMax; x++)
            {
                if (pixels[rowOffset + x].a <= AlphaThreshold)
                    continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
            return false;

        // 像素尺寸换算成世界尺寸：贴图每单位像素数由 sprite 决定。
        var pixelsPerUnit = sprite.pixelsPerUnit;
        if (pixelsPerUnit <= 0f)
            return false;

        size = new Vector2((maxX - minX + 1) / pixelsPerUnit, (maxY - minY + 1) / pixelsPerUnit);
        return true;
    }
}
