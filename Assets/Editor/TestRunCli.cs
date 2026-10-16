using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 临时开发工具：外部（AI 代理/命令行）驱动的 EditMode 测试运行器。
/// 用法：先 SetFilter 设定要跑的测试类（空 = 全量），调 Run()，
/// 然后轮询 Temp/editmode-test-result.json 读结果。
/// 云存档功能验收结束后删除本文件。
/// </summary>
public static class TestRunCli
{
    private static string[] groupFilter = System.Array.Empty<string>();
    private static ResultSaver saver;

    /// <summary>设定要运行的测试类名（全名或含类名的匹配）。传空则跑全部 EditMode 测试。</summary>
    public static void SetFilter(params string[] groupNames)
    {
        groupFilter = groupNames ?? System.Array.Empty<string>();
    }

    /// <summary>启动一次 EditMode 测试运行（异步）。结果写入 Temp/editmode-test-result.json。</summary>
    public static void Run()
    {
        if (File.Exists(ResultPath))
            File.Delete(ResultPath);

        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var filter = new Filter { testMode = TestMode.EditMode };
        if (groupFilter.Length > 0)
            filter.groupNames = groupFilter;

        saver = ScriptableObject.CreateInstance<ResultSaver>();
        api.RegisterCallbacks(saver);
        api.Execute(new ExecutionSettings(filter));
    }

    /// <summary>结果文件路径（项目 Temp 目录）。</summary>
    public static string ResultPath => Path.Combine("Temp", "editmode-test-result.json");

    /// <summary>把失败明细缓存到结果文件里，供 RunFinished 汇总写出。</summary>
    private sealed class ResultSaver : ScriptableObject, ICallbacks
    {
        private readonly List<string> failures = new List<string>();
        private readonly List<string> errors = new List<string>();

        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"passCount\":").Append(result.PassCount);
            sb.Append(",\"failCount\":").Append(result.FailCount);
            sb.Append(",\"skipCount\":").Append(result.SkipCount);
            sb.Append(",\"inconclusiveCount\":").Append(result.InconclusiveCount);
            sb.Append(",\"resultState\":\"").Append(Escape(result.ResultState != null ? result.ResultState.ToString() : "")).Append('"');

            if (failures.Count > 0)
            {
                sb.Append(",\"failures\":[");
                for (var i = 0; i < failures.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append('"').Append(Escape(failures[i])).Append('"');
                }

                sb.Append(']');
            }

            if (errors.Count > 0)
            {
                sb.Append(",\"compileErrors\":[");
                for (var i = 0; i < errors.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append('"').Append(Escape(errors[i])).Append('"');
                }

                sb.Append(']');
            }

            sb.Append('}');
            File.WriteAllText(ResultPath, sb.ToString());
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.Test != null && result.Test.IsSuite)
                return;

            if (result.FailCount > 0)
                failures.Add(result.Test.FullName + " :: " + (result.Message ?? ""));

            // 编译错误会以单个 ErrorTest 节点出现。
            if (result.ResultState != null && result.ResultState.ToString().StartsWith("Error"))
                errors.Add(result.Test.FullName + " :: " + (result.Message ?? ""));
        }

        private static string Escape(string value) =>
            value != null ? value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") : "";
    }
}
