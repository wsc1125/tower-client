using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class HoldButtonTests
{
    private GameObject root;
    private HoldButton button;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("HoldButton");
        button = root.AddComponent<HoldButton>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void Initially_IsNotHeld()
    {
        Assert.That(button.IsHeld, Is.False);
    }

    [Test]
    public void OnPointerDown_MarksHeld()
    {
        button.OnPointerDown(null);

        Assert.That(button.IsHeld, Is.True);
    }

    [Test]
    public void OnPointerUp_ClearsHeld()
    {
        button.OnPointerDown(null);

        button.OnPointerUp(null);

        Assert.That(button.IsHeld, Is.False);
    }

    [Test]
    public void OnPointerExit_ClearsHeld()
    {
        button.OnPointerDown(null);

        button.OnPointerExit(null);

        Assert.That(button.IsHeld, Is.False);
    }

    [Test]
    public void OnDisable_ClearsHeld()
    {
        button.OnPointerDown(null);

        InvokeOnDisable();

        Assert.That(button.IsHeld, Is.False);
    }

    private void InvokeOnDisable()
    {
        var onDisable = typeof(HoldButton).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(onDisable, Is.Not.Null, "HoldButton 必须保留 OnDisable 复位逻辑");
        onDisable.Invoke(button, null);
    }
}
