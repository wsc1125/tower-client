using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class CameraFollow2DTests
{
    private GameObject cameraObject;
    private GameObject roomObject;
    private Texture2D texture;
    private Sprite sprite;

    [TearDown]
    public void TearDown()
    {
        if (cameraObject != null)
            Object.DestroyImmediate(cameraObject);

        if (roomObject != null)
            Object.DestroyImmediate(roomObject);

        if (sprite != null)
            Object.DestroyImmediate(sprite);

        if (texture != null)
            Object.DestroyImmediate(texture);
    }

    [Test]
    public void ClampToRoom_WhenRoomIsBiggerThanView_ClampsBothAxes()
    {
        var room = new Rect(-10f, -10f, 20f, 20f);

        var result = CameraFollow2D.ClampToRoom(new Vector2(50f, -50f), new Vector2(4f, 2f), room);

        Assert.That(result.x, Is.EqualTo(8f).Within(0.0001f), "视野右边界应贴住房间右边界");
        Assert.That(result.y, Is.EqualTo(-9f).Within(0.0001f), "视野下边界应贴住房间下边界");
    }

    [Test]
    public void ClampToRoom_WhenRoomIsSmallerThanView_CentersThatAxis()
    {
        var room = new Rect(-1f, -2f, 2f, 4f);

        var result = CameraFollow2D.ClampToRoom(new Vector2(5f, 9f), new Vector2(4f, 2f), room);

        Assert.That(result.x, Is.EqualTo(0f).Within(0.0001f), "宽度不够时应锁在房间中心");
        Assert.That(result.y, Is.EqualTo(1f).Within(0.0001f), "高度够时仍应夹取");
    }

    [Test]
    public void CaptureRoomBounds_IgnoresDisabledRenderers()
    {
        texture = new Texture2D(8, 8);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 8f);

        cameraObject = new GameObject("Camera");
        cameraObject.AddComponent<Camera>();

        roomObject = new GameObject("Room");

        var visible = new GameObject("Floor");
        visible.transform.SetParent(roomObject.transform);
        visible.transform.position = Vector3.zero;
        visible.AddComponent<SpriteRenderer>().sprite = sprite;

        var hidden = new GameObject("Wall_Hidden");
        hidden.transform.SetParent(roomObject.transform);
        hidden.transform.position = new Vector3(10f, 0f, 0f);
        var hiddenRenderer = hidden.AddComponent<SpriteRenderer>();
        hiddenRenderer.sprite = sprite;
        hiddenRenderer.enabled = false;

        var follow = cameraObject.AddComponent<CameraFollow2D>();
        var serialized = new SerializedObject(follow);
        serialized.FindProperty("roomBounds").objectReferenceValue = roomObject.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var captured = follow.CaptureRoomBounds();

        Assert.That(captured, Is.True);
        Assert.That(follow.Bounds.center.x, Is.EqualTo(0f).Within(0.0001f), "被关闭的渲染器不应参与夹取范围");
        Assert.That(follow.Bounds.width, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void OnlineBattleScene_BindsCameraToExistingLocalPlayer()
    {
        var source = File.ReadAllText("Assets/Gameplay/Runtime/BattleNetworkBinder.cs");
        var start = source.IndexOf("private void Start()", System.StringComparison.Ordinal);
        var attach = source.IndexOf("public void AttachLocalCamera(", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(attach, Is.GreaterThan(start));

        var startup = source.Substring(start, attach - start);
        Assert.That(startup, Does.Contain("GetPlayerNetworkObject("));
        Assert.That(startup, Does.Contain("LocalClientId"));
        Assert.That(startup, Does.Contain("AttachLocalCamera("));
    }
}
