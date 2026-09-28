using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class RoomCollisionBoundsTests
{
    private const string RoomPrefabPath = "Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab";

    [Test]
    public void BattleScene_RoomUsesDedicatedCollisionBoundsInsteadOfVisualWallColliders()
    {
        // 房间由 TowerRunManager 在运行时实例化，场景里没有常驻实例，
        // 因此直接检查房间 prefab 资产。
        var room = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefabPath);
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

        // 四条边界必须首尾相接围成闭合矩形：横墙要盖住竖墙的中心线，竖墙要盖住横墙的中心线。
        // 注意用「中心线」而非「外沿」比较——横墙比竖墙间距略短是正常的，
        // 只要竖墙中心落在横墙跨度内，角落就不会漏缝。
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
