using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class SpriteFrameAnimationTests
{
    private const float FramesPerSecond = 10f;
    private const float FrameDuration = 1f / FramesPerSecond;

    private Texture2D texture;
    private readonly List<Sprite> createdSprites = new List<Sprite>();

    [SetUp]
    public void SetUp()
    {
        texture = new Texture2D(2, 2);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var sprite in createdSprites)
            Object.DestroyImmediate(sprite);
        createdSprites.Clear();
        Object.DestroyImmediate(texture);
    }

    [Test]
    public void Tick_BelowOneFrameDuration_KeepsCurrentFrame()
    {
        var animation = new SpriteFrameAnimation(CreateFrames(4), FramesPerSecond);

        animation.Tick(FrameDuration * 0.5f);

        Assert.That(animation.FrameIndex, Is.EqualTo(0));
    }

    [Test]
    public void Tick_ReachingOneFrameDuration_AdvancesOneFrame()
    {
        var animation = new SpriteFrameAnimation(CreateFrames(4), FramesPerSecond);

        animation.Tick(FrameDuration);

        Assert.That(animation.FrameIndex, Is.EqualTo(1));
    }

    [Test]
    public void Tick_AcrossMultipleFrameDurations_AdvancesProportionally()
    {
        var animation = new SpriteFrameAnimation(CreateFrames(4), FramesPerSecond);

        animation.Tick(FrameDuration * 3f);

        Assert.That(animation.FrameIndex, Is.EqualTo(3));
    }

    [Test]
    public void Tick_PastLastFrame_WrapsToFirstFrame()
    {
        var animation = new SpriteFrameAnimation(CreateFrames(4), FramesPerSecond);

        animation.Tick(FrameDuration * 4f);

        Assert.That(animation.FrameIndex, Is.EqualTo(0));
    }

    [Test]
    public void Reset_ReturnsToFirstFrame()
    {
        var animation = new SpriteFrameAnimation(CreateFrames(4), FramesPerSecond);
        animation.Tick(FrameDuration * 2f);

        animation.Reset();

        Assert.That(animation.FrameIndex, Is.EqualTo(0));
    }

    [Test]
    public void CurrentFrame_FollowsFrameIndex()
    {
        var frames = CreateFrames(4);
        var animation = new SpriteFrameAnimation(frames, FramesPerSecond);

        animation.Tick(FrameDuration * 2f);

        Assert.That(animation.CurrentFrame, Is.SameAs(frames[2]));
    }

    [Test]
    public void EmptyFrames_ProduceNoCurrentFrameAndDoNotAdvance()
    {
        var animation = new SpriteFrameAnimation(new Sprite[0], FramesPerSecond);

        animation.Tick(FrameDuration * 5f);

        Assert.That(animation.CurrentFrame, Is.Null);
        Assert.That(animation.FrameIndex, Is.EqualTo(0));
    }

    [Test]
    public void NullFrames_ProduceNoCurrentFrameAndDoNotAdvance()
    {
        var animation = new SpriteFrameAnimation(null, FramesPerSecond);

        animation.Tick(FrameDuration * 5f);

        Assert.That(animation.CurrentFrame, Is.Null);
        Assert.That(animation.FrameIndex, Is.EqualTo(0));
    }

    [Test]
    public void SingleFrame_StaysOnThatFrame()
    {
        var frames = CreateFrames(1);
        var animation = new SpriteFrameAnimation(frames, FramesPerSecond);

        animation.Tick(FrameDuration * 10f);

        Assert.That(animation.FrameIndex, Is.EqualTo(0));
        Assert.That(animation.CurrentFrame, Is.SameAs(frames[0]));
    }

    [Test]
    public void NonPositiveFrameRate_DoesNotAdvance()
    {
        var animation = new SpriteFrameAnimation(CreateFrames(4), 0f);

        animation.Tick(FrameDuration * 10f);

        Assert.That(animation.FrameIndex, Is.EqualTo(0));
    }

    private Sprite[] CreateFrames(int count)
    {
        var frames = new Sprite[count];
        for (var i = 0; i < count; i++)
        {
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            sprite.name = $"frame{i}";
            createdSprites.Add(sprite);
            frames[i] = sprite;
        }

        return frames;
    }
}
