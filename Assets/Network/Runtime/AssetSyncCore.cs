using UnityEngine;

namespace Tower.Network
{
    /// <summary>
    /// 资产云同步的纯逻辑核心：脏标记、去抖、退避重试、断点续传标记、快照打包与落地。
    /// 不持有 MonoBehaviour 依赖；时间一律由调用方注入（运行时传 Time.unscaledTime，
    /// 测试手动推进），便于 EditMode 测试。
    ///
    /// 心智模型：本地 PlayerPrefs 缓存什么时候写，服务端就（去抖后）什么时候有同样的数据。
    /// </summary>
    public sealed class AssetSyncCore
    {
        /// <summary>最后一条变化后等待多久才上传（合批）。</summary>
        public const float DebounceSeconds = 5f;

        /// <summary>变化持续不断时的最大上传间隔。</summary>
        public const float MaxDeferSeconds = 30f;

        /// <summary>上传失败退避基数，按失败次数指数翻倍。</summary>
        public const float BaseRetrySeconds = 5f;

        /// <summary>退避封顶。</summary>
        public const float MaxRetrySeconds = 60f;

        private string accountId;
        private bool isDirty;
        private bool suppress;
        private float firstDirtyAt = -1f;
        private float lastDirtyAt = -1f;
        private float nextRetryAt = -1f;
        private int retryCount;

        /// <summary>当前绑定的账号 ID。未绑定时所有标脏/落盘操作均为空操作。</summary>
        public string AccountId => accountId;

        public bool IsDirty => isDirty;

        public void BindAccount(string id) => accountId = id;

        /// <summary>登出或重新登录前清空运行时状态（不清理持久化的 pending 标记）。</summary>
        public void Reset()
        {
            accountId = null;
            isDirty = false;
            suppress = false;
            firstDirtyAt = -1f;
            lastDirtyAt = -1f;
            nextRetryAt = -1f;
            retryCount = 0;
        }

        /// <summary>回声压制：快照落地会触发 LoadoutChanged 等事件，期间标脏一律忽略。</summary>
        public void SetSuppress(bool value) => suppress = value;

        public void MarkDirty(float now)
        {
            if (suppress || string.IsNullOrEmpty(accountId))
                return;

            if (!isDirty)
            {
                isDirty = true;
                firstDirtyAt = now;
                WritePendingFlag(true);
            }

            lastDirtyAt = now;
        }

        /// <summary>当前是否应发起上传：去抖到点 / 30 秒上限 / 退避到点，三者任一。</summary>
        public bool IsUploadDue(float now)
        {
            if (!isDirty || now < nextRetryAt)
                return false;

            return now - lastDirtyAt >= DebounceSeconds || now - firstDirtyAt >= MaxDeferSeconds;
        }

        public void OnUploadSucceeded(float now)
        {
            isDirty = false;
            firstDirtyAt = -1f;
            lastDirtyAt = -1f;
            nextRetryAt = -1f;
            retryCount = 0;
            WritePendingFlag(false);
        }

        public void OnUploadFailed(float now)
        {
            retryCount++;
            var delay = Mathf.Min(BaseRetrySeconds * Mathf.Pow(2f, retryCount - 1), MaxRetrySeconds);
            nextRetryAt = now + delay;
        }

        /// <summary>
        /// 本账号是否存在「上次没传上去」的本地脏数据（跨进程持久化）。
        /// 登录拉取后若此标记为真，以上传代替覆盖，避免本地进度被服务端旧档冲掉。
        /// 单机 local 身份永不参与云同步，恒为 false。
        /// </summary>
        public bool HasPendingUpload(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || playerId == GameSession.LocalPlayerId)
                return false;

            return PlayerPrefs.GetInt(PendingKey(playerId), 0) == 1;
        }

        /// <summary>断点续传标记的缓存键（账号隔离）。</summary>
        public string PendingKey(string playerId) => LocalCacheKeys.For("AssetSyncPending", playerId);

        /// <summary>打包当前账号的完整快照（仓库/装备 + 收藏 + 星空币）。</summary>
        public AssetSnapshotEnvelopeDto CaptureEnvelope(string playerId)
        {
            var context = GameSession.GetPlayer(playerId);
            return new AssetSnapshotEnvelopeDto
            {
                save = PlayerSaveStore.Capture(context.Loadout),
                collection = CollectionStore.Capture(CollectionService.Progress),
                currency = PlayerPrefs.GetInt(LocalCacheKeys.For("StarCoins", playerId), 0),
            };
        }

        /// <summary>
        /// 把服务端快照落地到本地缓存并重载运行时上下文。
        /// 调用方须先 <see cref="SetSuppress"/>(true) 压制落地过程的回声脏标记，结束后恢复 false。
        /// </summary>
        public bool ApplyEnvelope(AssetSnapshotEnvelopeDto envelope, string playerId)
        {
            if (envelope == null || string.IsNullOrEmpty(playerId))
                return false;

            envelope.save?.Validate();
            envelope.collection?.Validate();

            PlayerSaveStore.Save(envelope.save ?? PlayerSaveData.CreateEmpty(), playerId);
            CollectionStore.SaveData(envelope.collection ?? new CollectionSaveData(), playerId);
            PlayerPrefs.SetInt(LocalCacheKeys.For("StarCoins", playerId), Mathf.Max(0, envelope.currency));
            PlayerPrefs.Save();

            GameSession.ReloadPlayer(playerId);
            CollectionService.InvalidateProgress();
            return true;
        }

        private void WritePendingFlag(bool value)
        {
            if (string.IsNullOrEmpty(accountId) || accountId == GameSession.LocalPlayerId)
                return;

            PlayerPrefs.SetInt(PendingKey(accountId), value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
