using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AndroidClientBuild
{
    private const string OutputPath = "Builds/android.apk";

    [MenuItem("Tools/Build/Android Client")]
    public static void Build()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new System.Exception($"Android client build failed: {report.summary.result}");

        Debug.Log($"Android client built: {OutputPath}");
    }
}
