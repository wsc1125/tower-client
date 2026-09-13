using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public class RoomCollisionBoundsTests
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [Test]
    public void SampleScene_RoomUsesDedicatedCollisionBoundsInsteadOfVisualWallColliders()
    {
        EditorSceneManager.OpenScene(ScenePath);

        var room = GameObject.Find("Room_Battle_Closed_01");
        Assert.That(room, Is.Not.Null);

        var walls = room.transform.Find("Walls");
        Assert.That(walls, Is.Not.Null);

        Assert.That(walls.Find("Wall_Left").GetComponent<BoxCollider2D>(), Is.Null);
        Assert.That(walls.Find("Wall_Right").GetComponent<BoxCollider2D>(), Is.Null);
        Assert.That(walls.Find("Wall_Top").GetComponent<BoxCollider2D>(), Is.Null);
        Assert.That(walls.Find("Wall_Bottom").GetComponent<BoxCollider2D>(), Is.Null);

        var collisionBounds = room.transform.Find("CollisionBounds");
        Assert.That(collisionBounds, Is.Not.Null);

        var left = GetColliderData(collisionBounds, "Boundary_Left");
        var right = GetColliderData(collisionBounds, "Boundary_Right");
        var top = GetColliderData(collisionBounds, "Boundary_Top");
        var bottom = GetColliderData(collisionBounds, "Boundary_Bottom");

        Assert.That(left.Size.x, Is.LessThanOrEqualTo(1.5f));
        Assert.That(right.Size.x, Is.LessThanOrEqualTo(1.5f));
        Assert.That(top.Size.y, Is.LessThanOrEqualTo(1.5f));
        Assert.That(bottom.Size.y, Is.LessThanOrEqualTo(1.5f));

        Assert.That(top.Min.x, Is.LessThanOrEqualTo(left.Center.x));
        Assert.That(top.Max.x, Is.GreaterThanOrEqualTo(right.Center.x));
        Assert.That(bottom.Min.x, Is.LessThanOrEqualTo(left.Center.x));
        Assert.That(bottom.Max.x, Is.GreaterThanOrEqualTo(right.Center.x));
        Assert.That(left.Min.y, Is.LessThanOrEqualTo(bottom.Center.y));
        Assert.That(left.Max.y, Is.GreaterThanOrEqualTo(top.Center.y));
        Assert.That(right.Min.y, Is.LessThanOrEqualTo(bottom.Center.y));
        Assert.That(right.Max.y, Is.GreaterThanOrEqualTo(top.Center.y));
    }

    private static ColliderData GetColliderData(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        Assert.That(child, Is.Not.Null, $"Missing collision child: {childName}");

        var collider = child.GetComponent<BoxCollider2D>();
        Assert.That(collider, Is.Not.Null, $"Missing BoxCollider2D on: {childName}");

        var center = (Vector2)child.localPosition + collider.offset;
        var size = collider.size;
        var extents = size * 0.5f;

        return new ColliderData(center, size, center - extents, center + extents);
    }

    private readonly struct ColliderData
    {
        public ColliderData(Vector2 center, Vector2 size, Vector2 min, Vector2 max)
        {
            Center = center;
            Size = size;
            Min = min;
            Max = max;
        }

        public Vector2 Center { get; }

        public Vector2 Size { get; }

        public Vector2 Min { get; }

        public Vector2 Max { get; }
    }
}
