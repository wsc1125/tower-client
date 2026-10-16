using System;
using System.Collections;
using UnityEngine;

namespace Tower.Network
{
    /// <summary>云同步状态。UI 门禁只认 Ready。</summary>
    public enum AssetSyncState
    {
        /// <summary>未登录。</summary>
        NotLoggedIn = 0,

        /// <summary>登录后正在拉取/落地服务端快照。</summary>
        Syncing = 1,

        /// <summary>同步完成，资产可用（后台仍会去抖上传）。</summary>
        Ready = 2,

        /// <summary>快照拉取失败（重试耗尽）。出发被门禁拦截，重新登录可重试。</summary>
        Failed = 3,
    }

    /// <summary>
    /// 玩家资产云同步服务：登录拉取快照、变化去抖上传、关键点强制上传、断网退避重试。
    /// 由 <see cref="NetworkSession"/> 在 Awake 时挂到同一 GameObject（随其跨场景常驻）。
    /// 纯逻辑在 <see cref="AssetSyncCore"/>，本类只做协程驱动与 Unity 生命周期接线。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssetSyncService : MonoBehaviour
    {
        /// <summary>全局实例。未挂载时为 null（EditMode 测试 / 未进过主菜单）。</summary>
        public static AssetSyncService Instance { get; private set; }

        /// <summary>同步状态变化（出发门禁据此放行/拦截）。</summary>
        public static event Action<AssetSyncState> StateChanged;

        /// <summary>
        /// 服务端快照落地完成，参数为下发的星空币余额。
        /// PlayerCurrency 订阅它刷新运行时显示（Network 程序集不能反向引用 UI 层，走事件反转）。
        /// </summary>
        public static event Action<int> SnapshotApplied;

        /// <summary>登录拉取失败的重试次数。</summary>
        private const int LoginGetAttempts = 3;

        /// <summary>登录拉取重试间隔（秒）。</summary>
        private const float LoginRetryIntervalSeconds = 1f;

        public AssetSyncState State { get; private set; } = AssetSyncState.NotLoggedIn;

        private ApiClient api;
        private readonly AssetSyncCore core = new AssetSyncCore();
        private bool uploading;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnEnable()
        {
            GameSession.LoadoutChanged += OnAssetChanged;
            GameSession.RaidEnded += OnRaidEnded;
            CollectionService.ProgressSaved += OnAssetChanged;
        }

        private void OnDisable()
        {
            GameSession.LoadoutChanged -= OnAssetChanged;
            GameSession.RaidEnded -= OnRaidEnded;
            CollectionService.ProgressSaved -= OnAssetChanged;
        }

        private void Update()
        {
            if (State == AssetSyncState.Ready && core.IsDirty && !uploading
                && core.IsUploadDue(Time.unscaledTime))
            {
                FlushNow();
            }
        }

        private void OnApplicationQuit() => FlushNow();

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                FlushNow();
        }

        /// <summary>由 NetworkSession 在创建后注入 HTTP 客户端。</summary>
        public void Initialize(ApiClient client) => api = client;

        /// <summary>标记资产已变化。外部源（PlayerCurrency）与未登录期调用都安全：只记脏，不做事。</summary>
        public static void MarkDirty()
        {
            if (Instance != null)
                Instance.core.MarkDirty(Time.unscaledTime);
        }

        /// <summary>强制上传点（出发/撤离/死亡/登出/退游戏）：绕过去抖立即上传，失败进入退避。</summary>
        public void FlushNow()
        {
            if (State != AssetSyncState.Ready || !core.IsDirty || uploading)
                return;

            StartCoroutine(UploadRoutine());
        }

        /// <summary>登录成功、身份已绑定账号后由 NetworkSession 调用：拉取服务端快照。</summary>
        public void OnAccountBound(string accountId)
        {
            core.Reset();
            core.BindAccount(accountId);
            SetState(AssetSyncState.Syncing);
            StartCoroutine(LoginSyncRoutine(accountId));
        }

        /// <summary>登出完成后由 NetworkSession 调用。</summary>
        public void OnLoggedOut()
        {
            // pending 标记是持久化的：若登出前上传失败，下次登录该账号会自动补传。
            core.Reset();
            SetState(AssetSyncState.NotLoggedIn);
        }

        /// <summary>登出前由 NetworkSession 调用：有脏数据就尽力传一次（失败保留 pending 标记）。</summary>
        public IEnumerator FlushBeforeLogout()
        {
            if (State != AssetSyncState.Ready || !core.IsDirty || uploading || api == null || !api.IsLoggedIn)
                yield break;

            yield return UploadRoutine();
        }

        private IEnumerator LoginSyncRoutine(string accountId)
        {
            AssetSnapshotResponseDto response = null;
            ApiError failure = null;

            for (var attempt = 1; attempt <= LoginGetAttempts && response == null; attempt++)
            {
                failure = null;
                yield return api.GetSnapshot(
                    r => response = r,
                    e => failure = e);

                if (response == null && attempt < LoginGetAttempts)
                    yield return new WaitForSecondsRealtime(LoginRetryIntervalSeconds);
            }

            if (response == null)
            {
                Debug.LogError($"[AssetSync] 快照拉取失败（{failure?.Code} {failure?.Message}），已阻止进入。重新登录可重试。");
                SetState(AssetSyncState.Failed);
                yield break;
            }

            if (response.schema_version > AssetSnapshotProtocol.SchemaVersion)
            {
                Debug.LogError($"[AssetSync] 服务端快照版本 {response.schema_version} 高于客户端支持的 {AssetSnapshotProtocol.SchemaVersion}，请升级客户端。");
                SetState(AssetSyncState.Failed);
                yield break;
            }

            // 本机有上次没传上去的脏数据：以上传代替覆盖，避免本地进度被服务端旧档冲掉。
            // （第一期客户端权威，本地数据可信度不低于服务端；权威化后此分支由服务端裁决。）
            // revision == 0 = 服务端尚无快照，走下方建档分支。
            if (core.HasPendingUpload(accountId) && response.revision > 0)
            {
                SetState(AssetSyncState.Ready);
                core.MarkDirty(Time.unscaledTime);
                yield return UploadRoutine();
                yield break;
            }

            if (response.revision == 0)
            {
                // 新账号：上下文已含初始装备（EnsureInitialized 流程），直接建档上传。
                // 判定用 revision == 0 —— JsonUtility 会把 "data":null 物化成完整空对象图，JSON 的 null 不可用作信号。
                SetState(AssetSyncState.Ready);
                core.MarkDirty(Time.unscaledTime);
                yield return UploadRoutine();
                yield break;
            }

            core.SetSuppress(true);
            try
            {
                if (!core.ApplyEnvelope(response.data, accountId))
                {
                    SetState(AssetSyncState.Failed);
                    yield break;
                }

                SnapshotApplied?.Invoke(response.data.currency);
            }
            finally
            {
                core.SetSuppress(false);
            }

            SetState(AssetSyncState.Ready);
        }

        private IEnumerator UploadRoutine()
        {
            uploading = true;

            var envelope = core.CaptureEnvelope(core.AccountId);
            var succeeded = false;

            yield return api.PutSnapshot(envelope,
                _ => succeeded = true,
                e => Debug.LogWarning($"[AssetSync] 快照上传失败，稍后重试：{e}"));

            uploading = false;

            if (succeeded)
                core.OnUploadSucceeded(Time.unscaledTime);
            else
                core.OnUploadFailed(Time.unscaledTime);
        }

        private void OnAssetChanged() => core.MarkDirty(Time.unscaledTime);

        private void OnRaidEnded() => FlushNow();

        private void SetState(AssetSyncState next)
        {
            if (State == next)
                return;

            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
