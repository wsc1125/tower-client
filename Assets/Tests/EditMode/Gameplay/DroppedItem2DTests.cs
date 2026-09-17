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

        dropped.Initialize(item);

        var bounds = wideSprite.bounds.size;
        var scaledMax = Mathf.Max(bounds.x, bounds.y) * visual.transform.localScale.x;
        Assert.That(scaledMax, Is.EqualTo(0.5f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_ScalesTallSpriteSoMaxSideMatchesWorldSize()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(item);

        var bounds = tallSprite.bounds.size;
        var scaledMax = Mathf.Max(bounds.x, bounds.y) * visual.transform.localScale.y;
        Assert.That(scaledMax, Is.EqualTo(0.5f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_KeepsAspectRatioByUsingUniformScale()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(item);

        var scale = visual.transform.localScale;
        Assert.That(scale.x, Is.EqualTo(scale.y).Within(0.0001f));
        Assert.That(scale.y, Is.EqualTo(scale.z).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_FallsBackToIconWhenWorldSpriteMissing()
    {
        var item = ItemDefinitionFactory.Create(wideSprite, null, 0.5f);

        dropped.Initialize(item);

        Assert.That(visual.sprite, Is.SameAs(wideSprite));
        Assert.That(visual.enabled, Is.True);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_SetsPickupRadiusToHalfWorldSize()
    {
        var item = ItemDefinitionFactory.Create(null, wideSprite, 0.6f);

        dropped.Initialize(item);

        Assert.That(pickupZone.radius, Is.EqualTo(0.3f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_DisablesVisualWhenNoSpriteAtAll()
    {
        var item = ItemDefinitionFactory.Create(null, null, 0.5f);

        dropped.Initialize(item);

        Assert.That(visual.enabled, Is.False);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_ExposesTheItem()
    {
        var item = ItemDefinitionFactory.Create(null, wideSprite, 0.5f);

        dropped.Initialize(item);

        Assert.That(dropped.Item, Is.SameAs(item));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_ScalesGlowWidthByItemWorldSize()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(item);

        var glowWorldWidth = wideSprite.bounds.size.x * glow.transform.localScale.x;
        Assert.That(glowWorldWidth, Is.EqualTo(0.5f * 1.6f).Within(0.0001f));
        Assert.That(glow.enabled, Is.True);
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_KeepsGlowAspectRatioByUsingUniformScale()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(item);

        var scale = glow.transform.localScale;
        Assert.That(scale.x, Is.EqualTo(scale.y).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_SitsGlowAtItemBottom()
    {
        var item = ItemDefinitionFactory.Create(null, tallSprite, 0.5f);

        dropped.Initialize(item);

        // tallSprite 最大边是高，缩放后世界高度正好等于 worldSize，底边在 -worldSize / 2
        Assert.That(glow.transform.localPosition.y, Is.EqualTo(-0.25f).Within(0.0001f));
        Object.DestroyImmediate(item);
    }

    [Test]
    public void Initialize_DisablesGlowWhenNoSpriteAtAll()
    {
        var item = ItemDefinitionFactory.Create(null, null, 0.5f);

        dropped.Initialize(item);

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
