using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode 下 Unity 不会自动调用 Awake / OnEnable / OnDisable，
/// 需要覆盖写在这些回调里的接线（订阅、首次刷新）时，用这里手动触发一次 —— 与 <c>HoldButtonTests</c> 的做法一致。
/// </summary>
internal static class EditModeLifecycle
{
    public static void Invoke(MonoBehaviour behaviour, string methodName)
    {
        var method = behaviour.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, behaviour.GetType().Name + " 必须保留 " + methodName);
        method.Invoke(behaviour, null);
    }
}
