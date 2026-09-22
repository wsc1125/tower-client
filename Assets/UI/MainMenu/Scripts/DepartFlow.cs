using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 「出发」按钮的流程编排：显示加载页 → 异步加载目标场景 → 进度走满并满足最短展示时长后切场。
/// 加载页只管展示，进度换算与防重入都在这里。
/// </summary>
public sealed class DepartFlow : MonoBehaviour
{
    [Header("接线")]
    [Tooltip("出发按钮，加载期间会被禁用，避免重复触发。")]
    [SerializeField] private Button departButton;

    [Tooltip("加载页，负责显示进度与文案。")]
    [SerializeField] private LoadingScreen loadingScreen;

    [Header("加载")]
    [Tooltip("点击出发后进入的场景名，需已在 Build Settings 中登记。")]
    [SerializeField] private string targetSceneName = "BattleScene";

    [Tooltip("加载页最短展示时长（秒）。场景加载过快时，避免加载页一闪而过。")]
    [SerializeField, Min(0f)] private float minDisplaySeconds = 0.6f;

    /// <summary>关闭场景自动激活后，AsyncOperation.progress 会停在 0.9，进度需按这个上限归一化。</summary>
    private const float ActivationProgress = 0.9f;

    private bool isLoading;

    /// <summary>供 Btn_Depart 的 onClick 调用。</summary>
    public void Depart()
    {
        if (isLoading)
            return;

        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogError($"[DepartFlow] 场景「{targetSceneName}」不在 Build Settings 中，出发已取消。");
            return;
        }

        StartCoroutine(LoadTargetScene());
    }

    private IEnumerator LoadTargetScene()
    {
        isLoading = true;
        SetButtonInteractable(false);
        loadingScreen?.Show();

        var startedAt = Time.realtimeSinceStartup;

        var operation = SceneManager.LoadSceneAsync(targetSceneName);
        // 先攥住激活权：进度条走满、最短展示时长也满足后再切场。
        operation.allowSceneActivation = false;

        while (operation.progress < ActivationProgress)
        {
            loadingScreen?.SetProgress(operation.progress / ActivationProgress);
            yield return null;
        }

        loadingScreen?.SetProgress(1f);
        loadingScreen?.SetStatus("即将进入战斗…");

        while (Time.realtimeSinceStartup - startedAt < minDisplaySeconds)
            yield return null;

        // 激活后当前场景连同加载页一起被卸载，无需再还原按钮状态。
        operation.allowSceneActivation = true;
    }

    private void SetButtonInteractable(bool value)
    {
        if (departButton != null)
            departButton.interactable = value;
    }
}
