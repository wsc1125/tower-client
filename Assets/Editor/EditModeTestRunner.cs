using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// EditMode 测试运行助手：注册 TestRunnerApi 回调，把测试结果打印到 Console，
/// 供外部工具轮询读取。菜单 Test/Run All EditMode Tests 运行全部；
/// 调用 Run(params string[]) 可按全名过滤运行指定用例。
/// </summary>
public static class EditModeTestRunner
{
    private const string LogPrefix = "[EditModeTestRunner]";

    static EditModeTestRunner()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new ConsoleResultLogger());
    }

    [MenuItem("Test/Run All EditMode Tests")]
    public static void RunAll()
    {
        Run(null);
    }

    /// <summary>运行指定用例（完整名称，如 PlayerHealth2DTests.TakeDamage_FiresDiedExactlyOnce）；传空则运行全部。</summary>
    public static void Run(params string[] testNames)
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var filter = new Filter { testMode = TestMode.EditMode };
        if (testNames != null && testNames.Length > 0)
            filter.testNames = testNames;

        api.Execute(new ExecutionSettings(filter));
    }

    private sealed class ConsoleResultLogger : IErrorCallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
            Debug.Log($"{LogPrefix} run started: {testsToRun.TestCaseCount} test(s)");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            Debug.Log(
                $"{LogPrefix} run finished: {result.TestStatus}" +
                $" passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus == TestStatus.Failed)
                Debug.Log($"{LogPrefix} FAILED {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }

        public void OnError(string message)
        {
            Debug.Log($"{LogPrefix} run errored: {message}");
        }
    }
}
