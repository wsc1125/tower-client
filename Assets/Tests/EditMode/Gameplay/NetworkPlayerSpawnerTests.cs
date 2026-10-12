using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public sealed class NetworkPlayerSpawnerTests
{
    [Test]
    public void PlacePlayerAfterSpawn_TeleportsThroughSync_AndFallsBackOnlyWithoutSync()
    {
        var source = File.ReadAllText("Assets/Gameplay/Runtime/NetworkPlayerSpawner.cs");
        var methodStart = source.IndexOf("private System.Collections.IEnumerator PlacePlayerAfterSpawn(", System.StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private Vector2 ResolveSpawnPosition(", methodStart, System.StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd, Is.GreaterThan(methodStart));

        var method = source.Substring(methodStart, methodEnd - methodStart);
        Assert.That(method, Does.Match(@"var\s+sync\s*=\s*born\.GetComponent<PlayerNetworkSync>\(\)\s*;\s*if\s*\(sync\s*!=\s*null\)\s*sync\.TeleportTo\(target\)\s*;\s*else\s*\{"));
        var fallback = Regex.Match(method, @"else\s*\{(?<body>[^{}]*)\}");
        Assert.That(fallback.Success, Is.True);
        Assert.That(fallback.Groups["body"].Value, Does.Contain("body.position = target"));
        Assert.That(fallback.Groups["body"].Value, Does.Contain("born.transform.position ="));
        Assert.That(method.Substring(0, fallback.Index), Does.Not.Contain("body.position = target"));
        Assert.That(method.Substring(0, fallback.Index), Does.Not.Contain("born.transform.position ="));
    }
}
