using Tower.Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Netcode
{
    [DisallowMultipleComponent]
    public sealed class TeammateHealthTag : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString128Bytes> syncedName =
            new NetworkVariable<FixedString128Bytes>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerHealth2D health;
        private GameObject tagRoot;
        private Text nameText;
        private Image healthFill;

        public string DisplayedName => nameText != null ? nameText.text : string.Empty;
        public Image HealthFill => healthFill;
        public bool IsVisible => tagRoot != null && tagRoot.activeSelf;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            health = GetComponent<PlayerHealth2D>();
            if (IsOwner)
            {
                var account = NetworkSession.Instance != null ? NetworkSession.Instance.Account : null;
                var name = account != null ? account.display_name : null;
                syncedName.Value = new FixedString128Bytes(string.IsNullOrWhiteSpace(name)
                    ? "玩家" : name.Substring(0, Mathf.Min(name.Length, 20)));
                return;
            }

            if (BattleHudReady.IsReady)
                Refresh();
            BattleHudReady.Ready += Refresh;
        }

        public override void OnNetworkDespawn()
        {
            BattleHudReady.Ready -= Refresh;
            if (tagRoot != null)
                Destroy(tagRoot);
            tagRoot = null;
            base.OnNetworkDespawn();
        }

        private void LateUpdate()
        {
            if (!IsSpawned || IsOwner || tagRoot == null)
                return;

            if (!BattleHudReady.IsReady)
            {
                tagRoot.SetActive(false);
                return;
            }

            Refresh();
        }

        private void Refresh()
        {
            if (!IsSpawned || IsOwner || !BattleHudReady.IsReady)
                return;

            if (health == null)
                health = GetComponent<PlayerHealth2D>();
            SetState(syncedName.Value.ToString(), health.CurrentHealthValue,
                health.MaximumHealthValue, !health.IsDead);
        }

        public void SetState(string playerName, float current, float maximum, bool alive)
        {
            if (tagRoot == null)
                CreateTag();

            nameText.text = string.IsNullOrWhiteSpace(playerName) ? "玩家" : playerName;
            var ratio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            healthFill.fillAmount = ratio;
            healthFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 112f * ratio);
            tagRoot.SetActive(alive && current > 0f);
        }

        private void CreateTag()
        {
            tagRoot = new GameObject("TeammateHealthTag", typeof(RectTransform), typeof(Canvas));
            tagRoot.transform.SetParent(transform, false);
            var rect = (RectTransform)tagRoot.transform;
            rect.localPosition = new Vector3(0f, 0.85f, -0.2f);
            rect.localScale = Vector3.one * 0.01f;
            rect.sizeDelta = new Vector2(160f, 36f);

            var canvas = tagRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;

            var label = new GameObject("PlayerName", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(rect, false);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchoredPosition = new Vector2(0f, 7f);
            labelRect.sizeDelta = new Vector2(160f, 22f);
            nameText = label.GetComponent<Text>();
            nameText.font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            nameText.fontSize = 16;
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = 10;
            nameText.resizeTextMaxSize = 16;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.color = Color.white;
            nameText.raycastTarget = false;

            var background = CreateImage("HealthBackground", rect, new Vector2(116f, 12f),
                new Vector2(0f, -10f), new Color(0.08f, 0.12f, 0.13f, 0.9f));
            var track = CreateImage("HealthTrack", background.rectTransform, new Vector2(112f, 8f),
                Vector2.zero, new Color(0.17f, 0.25f, 0.23f, 1f));
            healthFill = CreateImage("HealthFill", track.rectTransform, new Vector2(112f, 8f),
                new Vector2(-56f, 0f), new Color(0.35f, 0.85f, 0.4f, 1f));
            healthFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        private static Image CreateImage(string objectName, Transform parent, Vector2 size,
            Vector2 position, Color color)
        {
            var child = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            var rect = (RectTransform)child.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = child.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
