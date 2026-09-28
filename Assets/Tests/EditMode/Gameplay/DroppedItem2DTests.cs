using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class DroppedItem2DTests
{
    private Texture2D wideTexture;
    private Texture2D tallTexture;
    private Sprite wideSprite;
    private Sprite tallSprite;
    private GameObject root;
    private DroppedItem2D dropped;
    private SpriteRenderer visual;
    private SpriteRenderer glow;
    private CircleCollider2D pickupZone;

    [SetUp]
    public void SetUp()
    {
        wideTexture = new Texture2D(64, 16);
        tallTexture = new Texture2D(16, 64);
        wideSprite = Sprite.Create(wideTexture, new Rect(0f, 0f, 64f, 16f), new Vector2(0.5f, 0.5f), 100f);
        tallSprite = Sprite.Create(tallTexture, new Rect(0f, 0f, 16f, 64f), new Vector2(0.5f, 0.5f), 100f);

        root = new GameObject("DroppedItem");
        var visualRoot = new GameObject("Visual");
        visualRoot.transform.SetParent(root.transform);
        visual = visualRoot.AddComponent<SpriteRenderer>();
        var glowRoot = new GameObject("Glow");
        glowRoot.transform.SetParent(root.transform);
        glow = glowRoot.AddComponent<SpriteRenderer>();
        // 用扁贴图当光圈：光圈素材本身是「宽 > 高」的地面椭圆
        glow.sprite = wideSprite;
        pickupZone = root.AddComponent<CircleCollider2D>();
        pickupZone.isTrigger = true;
        dropped = root.AddComponent<DroppedItem2D>();

        var serialized = new SerializedObject(dropped);
        serialized.FindProperty("visual").objectReferenceValue = visual;
        serialized.FindProperty("glow").objectReferenceValue = glow;
        serialized.FindProperty("pickupZone").objectReferenceValue = pickupZone;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(wideSprite);
        Object.DestroyImmediate(tallSprite);
        Object.DestroyImmediate(wideTexture);
        Object.DestroyImmediate(tallTexture);
    }

    [Test]
    public void Initialize_ScalesWideSpriteSoMaxSideMatchesWorldSize()
    {
        var item = ItemDefinitionFactory.Create(null, wideSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        var bounds = wideSprite.bounds.size;
        var scaledMax = Mathf.Max(bounds.x, bounds.y) * visual.transform.localScale.x;
        Assert.That(scaledMax, Is.EqualTo(0.5f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_ScalesTallSpriteSoMaxSideMatchesWorldSize()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        var bounds = tallSprite.bounds.size;
        var scaledMax = Mathf.Max(bounds.x, bounds.y) * visual.transform.localScale.y;
        Assert.That(scaledMax, Is.EqualTo(0.5f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_KeepsAspectRatioByUsingUniformScale()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        var scale = visual.transform.localScale;
        Assert.That(scale.x, Is.EqualTo(scale.y).Within(0.0001f));
        Assert.That(scale.y, Is.EqualTo(scale.z).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_FallsBackToIconWhenWorldSpriteMissing()
    {
        var item = ItemDefinitionFactory.Create(wideSprite, null, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        Assert.That(visual.sprite, Is.SameAs(wideSprite));
        Assert.That(visual.enabled, Is.True);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_SetsPickupRadiusToHalfWorldSize()
    {
        var item = ItemDefinitionFactory.Create(null, wideSprite, 0.6f);

        dropped.Initialize(ItemInstance.Create(item));

        Assert.That(pickupZone.radius, Is.EqualTo(0.3f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_DisablesVisualWhenNoSpriteAtAll()
    {
        var item = ItemDefinitionFactory.Create(null, null, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        Assert.That(visual.enabled, Is.False);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_ExposesTheItem()
    {
        var item = ItemDefinitionFactory.Create(null, wideSprite, 0.5f);
        var instance = ItemInstance.Create(item);

        dropped.Initialize(instance);

        Assert.That(dropped.Item, Is.SameAs(instance));
        Assert.That(dropped.Definition, Is.SameAs(item));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_ScalesGlowWidthByItemActualWidth()
    {
        // tallSprite 是竖长贴图（16x64），最大边为高，缩放后本体宽度 = 0.5 * (16/64) = 0.125。
        // 光圈宽度应按本体实际宽度算，而不是最大边 worldSize。
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        var itemWorldWidth = tallSprite.bounds.size.x * visual.transform.localScale.x;
        var glowWorldWidth = wideSprite.bounds.size.x * glow.transform.localScale.x;
        Assert.That(glowWorldWidth, Is.EqualTo(itemWorldWidth * 1.6f).Within(0.0001f));
        Assert.That(glow.enabled, Is.True);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_ScalesGlowWidthByItemActualWidthForWideSprite()
    {
        // wideSprite 是横长贴图（64x16），最大边为宽，本体宽度正好等于 worldSize。
        var item = ItemDefinitionFactory.Create(null, wideSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        var glowWorldWidth = wideSprite.bounds.size.x * glow.transform.localScale.x;
        Assert.That(glowWorldWidth, Is.EqualTo(0.5f * 1.6f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_KeepsGlowAspectRatioByUsingUniformScale()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        var scale = glow.transform.localScale;
        Assert.That(scale.x, Is.EqualTo(scale.y).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_SitsGlowAtItemBottom()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        // tallSprite 最大边是高，缩放后世界高度正好等于 worldSize，底边在 -worldSize / 2
        Assert.That(glow.transform.localPosition.y, Is.EqualTo(-0.25f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_AlignsGlowToItemCenterWhenSpriteUsesCustomPivot()
    {
        // 模拟武器贴图：自定义轴点靠左（pivot.x = 0.15），缩放后视觉中心会偏到原点右侧。
        var offsetTexture = new Texture2D(64, 16);
        var offsetSprite = Sprite.Create(
            offsetTexture,
            new Rect(0f, 0f, 64f, 16f),
            new Vector2(0.15f, 0.5f),
            100f);
        var item = ItemDefinitionFactory.Create(null, offsetSprite, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        // 视觉中心相对原点的偏移 = (0.5 - 0.15) * 贴图世界宽度 * 缩放
        var expectedX = (0.5f - 0.15f) * offsetSprite.bounds.size.x * visual.transform.localScale.x;
        Assert.That(glow.transform.localPosition.x, Is.EqualTo(expectedX).Within(0.0001f));
        // 该贴图是「宽 > 高」，最大边为宽，缩放后世界高度 = 0.16 * scale，底边在 -高度 / 2
        var expectedY = -offsetSprite.bounds.size.y * visual.transform.localScale.y * 0.5f;
        Assert.That(glow.transform.localPosition.y, Is.EqualTo(expectedY).Within(0.0001f));

        Object.DestroyImmediate(item);
        Object.DestroyImmediate(offsetSprite);
        Object.DestroyImmediate(offsetTexture);
    }

    [Test]
    public void Initialize_DisablesGlowWhenNoSpriteAtAll()
    {
        var item = ItemDefinitionFactory.Create(null, null, 0.5f);

        dropped.Initialize(ItemInstance.Create(item));

        Assert.That(glow.enabled, Is.False);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_DisablesGlowWhenItemIsNull()
    {
        dropped.Initialize(null);

        Assert.That(glow.enabled, Is.False);
    }
}
