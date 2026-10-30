using LandFlipper.Core;
using LandFlipper.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// Ana menü (UI Toolkit): Tek Oyunculu, Co-op (Host / Katıl), Ayarlar, Çıkış.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        VisualElement mainPanel, coopPanel, busyPanel;
        Button singleButton, coopButton, settingsButton, quitButton, hostButton, joinButton, backButton, cancelButton;
        TextField portField, addressField;
        Label statusLabel, busyLabel;

        // canlı menü: logo düşüp sallanır, butonlar sırayla kayarak gelir
        VisualElement logo;
        IVisualElementScheduledItem logoJob;
        float introStartTime;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            mainPanel = root.Q("main-panel");
            coopPanel = root.Q("coop-panel");
            busyPanel = root.Q("busy-panel");
            singleButton = root.Q<Button>("single-button");
            coopButton = root.Q<Button>("coop-button");
            settingsButton = root.Q<Button>("settings-button");
            quitButton = root.Q<Button>("quit-button");
            hostButton = root.Q<Button>("host-button");
            joinButton = root.Q<Button>("join-button");
            backButton = root.Q<Button>("back-button");
            cancelButton = root.Q<Button>("cancel-button");
            portField = root.Q<TextField>("port-field");
            addressField = root.Q<TextField>("address-field");
            statusLabel = root.Q<Label>("status-label");
            busyLabel = root.Q<Label>("busy-label");

            singleButton.clicked += OnSingleClicked;
            coopButton.clicked += OnCoopClicked;
            if (settingsButton != null) settingsButton.clicked += OnSettingsClicked;
            quitButton.clicked += OnQuitClicked;
            hostButton.clicked += OnHostClicked;
            joinButton.clicked += OnJoinClicked;
            backButton.clicked += OnBackClicked;
            cancelButton.clicked += OnCancelClicked;
            SessionContext.MessagePosted += OnSessionMessage;

            CursorUtility.SetLocked(false);
            InputGate.SetBlocked(false);
            Time.timeScale = 1f;

            logo = root.Q("menu-logo");
            introStartTime = Time.unscaledTime;
            if (logo != null) logoJob = logo.schedule.Execute(AnimateLogo).Every(16);

            ShowPanel(mainPanel);
            SetStatus(null);

            string pending = SessionContext.ConsumeMessage();
            if (!string.IsNullOrEmpty(pending))
                SetStatus(pending, true);
        }

        void OnDisable()
        {
            singleButton.clicked -= OnSingleClicked;
            coopButton.clicked -= OnCoopClicked;
            if (settingsButton != null) settingsButton.clicked -= OnSettingsClicked;
            quitButton.clicked -= OnQuitClicked;
            hostButton.clicked -= OnHostClicked;
            joinButton.clicked -= OnJoinClicked;
            backButton.clicked -= OnBackClicked;
            cancelButton.clicked -= OnCancelClicked;
            SessionContext.MessagePosted -= OnSessionMessage;
            logoJob?.Pause();
        }

        // ------------------------------------------------------------ animasyon

        void AnimateLogo()
        {
            float t = Time.unscaledTime - introStartTime;
            float drop = t < 0.7f ? 1f - EaseOutBack(t / 0.7f) : 0f;
            float y = -70f * drop + Mathf.Sin(t * 1.3f) * 3f;
            float angle = -4f + Mathf.Sin(t * 0.9f) * 1.2f;
            logo.style.translate = new Translate(0f, y);
            logo.style.rotate = new Rotate(angle);
            logo.style.opacity = Mathf.Clamp01(t / 0.25f);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            x = Mathf.Clamp01(x) - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        /// <summary>Panelin "mm-anim" öğeleri sırayla soldan kayarak gelir.</summary>
        static void PlayEnter(VisualElement panel, int baseDelayMs)
        {
            var items = new System.Collections.Generic.List<VisualElement>();
            if (panel.ClassListContains("mm-anim")) items.Add(panel);
            panel.Query(className: "mm-anim").ForEach(e => { if (e != panel) items.Add(e); });
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.AddToClassList("mm-enter");
                item.schedule.Execute(() => item.RemoveFromClassList("mm-enter")).StartingIn(baseDelayMs + 70 * i);
            }
        }

        void OnSettingsClicked() => SettingsController.Instance?.Open();

        LFNetworkManager Manager
        {
            get
            {
                var manager = LFNetworkManager.Instance;
                if (manager == null)
                    SetStatus("NetworkManager bulunamadı. Oyunu MainMenu sahnesinden başlat.", true);
                return manager;
            }
        }

        void OnSingleClicked()
        {
            var manager = Manager;
            if (manager == null) return;
            ShowBusy("Arazi yükleniyor...", false);
            manager.StartSinglePlayer();
        }

        void OnCoopClicked()
        {
            SetStatus(null);
            ShowPanel(coopPanel);
        }

        void OnBackClicked()
        {
            SetStatus(null);
            ShowPanel(mainPanel);
        }

        void OnHostClicked()
        {
            if (!TryGetPort(out ushort port)) return;
            var manager = Manager;
            if (manager == null) return;
            ShowBusy("Oturum kuruluyor...", false);
            manager.StartCoopHost(port);
        }

        void OnJoinClicked()
        {
            if (!TryGetPort(out ushort port)) return;

            string address = addressField.value?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                SetStatus("Katılmak için host'un adresini gir.", true);
                return;
            }

            var manager = Manager;
            if (manager == null) return;
            ShowBusy($"{address}:{port} adresine bağlanılıyor...", true);
            manager.JoinCoop(address, port);
        }

        void OnCancelClicked()
        {
            LFNetworkManager.Instance?.LeaveSession();
            SetStatus("Bağlantı iptal edildi.");
            ShowPanel(coopPanel);
        }

        void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnSessionMessage(string message)
        {
            ShowPanel(SessionContext.Mode == GameMode.None ? coopPanel : mainPanel);
            SetStatus(message, true);
            SessionContext.ConsumeMessage();
        }

        bool TryGetPort(out ushort port)
        {
            if (ushort.TryParse(portField.value?.Trim(), out port) && port >= 1024)
                return true;

            SetStatus("Port 1024 ile 65535 arasında bir sayı olmalı.", true);
            return false;
        }

        void ShowBusy(string message, bool cancellable)
        {
            busyLabel.text = message;
            cancelButton.style.display = cancellable ? DisplayStyle.Flex : DisplayStyle.None;
            SetStatus(null);
            ShowPanel(busyPanel);
        }

        void ShowPanel(VisualElement panel)
        {
            bool changed = panel.ClassListContains("hidden") || Time.unscaledTime - introStartTime < 0.1f;
            mainPanel.EnableInClassList("hidden", panel != mainPanel);
            coopPanel.EnableInClassList("hidden", panel != coopPanel);
            busyPanel.EnableInClassList("hidden", panel != busyPanel);
            if (changed) PlayEnter(panel, panel == mainPanel && Time.unscaledTime - introStartTime < 0.1f ? 300 : 20);
        }

        void SetStatus(string message, bool isError = false)
        {
            statusLabel.text = message ?? string.Empty;
            statusLabel.EnableInClassList("error", isError);
            statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
