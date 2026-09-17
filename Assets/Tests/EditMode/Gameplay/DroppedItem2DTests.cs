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
        pickupZone = root.AddComponent<CircleCollider2D>();
        pickupZone.isTrigger = true;
        dropped = root.AddComponent<DroppedItem2D>();

        var serialized = new SerializedObject(dropped);
        serialized.FindProperty("visual").objectReferenceValue = visual;
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
}
