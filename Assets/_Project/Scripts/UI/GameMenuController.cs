using System.Net;
using System.Net.Sockets;
using LandFlipper.Core;
using LandFlipper.Network;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// Oyun içi menü (UI Toolkit). Esc / Gamepad Start ile açılır.
    /// Tek oyunculuda oyunu durdurur; co-op'ta dünya akmaya devam eder,
    /// sadece local oyuncunun girdisi kapanır.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameMenuController : MonoBehaviour
    {
        enum PendingAction { None, Leave, Quit }

        VisualElement overlay, menuPanel, confirmPanel;
        Label modeLabel, infoLabel, confirmLabel;
        Button resumeButton, settingsButton, leaveButton, quitButton, confirmYesButton, confirmNoButton;
        InputAction pauseAction;
        IVisualElementScheduledItem refreshJob;
        PendingAction pendingAction;
        string cachedLocalIp;

        public bool IsOpen { get; private set; }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            overlay = root.Q("pause-overlay");
            menuPanel = root.Q("menu-panel");
            confirmPanel = root.Q("confirm-panel");
            modeLabel = root.Q<Label>("mode-label");
            infoLabel = root.Q<Label>("info-label");
            confirmLabel = root.Q<Label>("confirm-label");
            resumeButton = root.Q<Button>("resume-button");
            settingsButton = root.Q<Button>("settings-button");
            leaveButton = root.Q<Button>("leave-button");
            quitButton = root.Q<Button>("quit-button");
            confirmYesButton = root.Q<Button>("confirm-yes-button");
            confirmNoButton = root.Q<Button>("confirm-no-button");

            resumeButton.clicked += Close;
            if (settingsButton != null) settingsButton.clicked += OnSettingsClicked;
            leaveButton.clicked += OnLeaveClicked;
            quitButton.clicked += OnQuitClicked;
            confirmYesButton.clicked += OnConfirmYes;
            confirmNoButton.clicked += HideConfirm;

            LFInput.EnsureCreated();
            pauseAction = LFInput.Pause;
            pauseAction.performed += OnPausePerformed;

            refreshJob = root.schedule.Execute(RefreshInfo).Every(500);
            refreshJob.Pause();

            SetOpen(false);
        }

        void OnDisable()
        {
            resumeButton.clicked -= Close;
            if (settingsButton != null) settingsButton.clicked -= OnSettingsClicked;
            leaveButton.clicked -= OnLeaveClicked;
            quitButton.clicked -= OnQuitClicked;
            confirmYesButton.clicked -= OnConfirmYes;
            confirmNoButton.clicked -= HideConfirm;

            if (pauseAction != null) pauseAction.performed -= OnPausePerformed;

            refreshJob?.Pause();

            if (IsOpen)
            {
                InputGate.SetBlocked(false);
                Time.timeScale = 1f;
            }
        }

        void OnPausePerformed(InputAction.CallbackContext _)
        {
            // Esc yetenek ağacını ya da ayarları kapatıyorsa oyun menüsü bu tuşla açılıp kapanmasın
            if (!IsOpen && (SkillTreeController.IsAnyOpen || SkillTreeController.ClosedFrame == Time.frameCount))
                return;
            if (!IsOpen && (WorkbenchController.IsAnyOpen || WorkbenchController.ClosedFrame == Time.frameCount))
                return;
            if (SettingsController.IsOpen || SettingsController.ConsumedEscapeFrame == Time.frameCount)
                return;

            if (IsOpen && pendingAction != PendingAction.None)
                HideConfirm();
            else
                SetOpen(!IsOpen);
        }

        void OnSettingsClicked() => SettingsController.Instance?.Open();

        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);

        void SetOpen(bool open)
        {
            IsOpen = open;
            overlay.EnableInClassList("hidden", !open);
            HideConfirm();

            InputGate.SetBlocked(open);
            CursorUtility.SetLocked(!open && NetworkClient.localPlayer != null);

            if (SessionContext.Mode == GameMode.SinglePlayer)
                Time.timeScale = open ? 0f : 1f;

            if (open)
            {
                RefreshInfo();
                refreshJob.Resume();
            }
            else
            {
                refreshJob.Pause();
            }
        }

        void RefreshInfo()
        {
            var manager = LFNetworkManager.Instance;

            switch (SessionContext.Mode)
            {
                case GameMode.SinglePlayer:
                    modeLabel.text = "Tek Oyunculu";
                    infoLabel.text = "Oyun duraklatıldı.";
                    break;

                case GameMode.CoopHost:
                    modeLabel.text = "Co-op · Host";
                    cachedLocalIp ??= GetLocalIPv4();
                    int port = manager != null ? manager.Port : SessionContext.DefaultPort;
                    infoLabel.text =
                        $"Oyuncular: {NetworkServer.connections.Count}/{SessionContext.MaxCoopPlayers}\n" +
                        $"Arkadaşların bu adrese katılabilir: {cachedLocalIp}:{port}";
                    break;

                case GameMode.CoopClient:
                    modeLabel.text = "Co-op · Katılımcı";
                    infoLabel.text = manager != null
                        ? $"Bağlı: {manager.networkAddress}:{manager.Port}\nOyun dünyası akmaya devam ediyor."
                        : "Bağlı.";
                    break;

                default:
                    modeLabel.text = "Oturum yok";
                    infoLabel.text = string.Empty;
                    break;
            }
        }

        void OnLeaveClicked()
        {
            if (NeedsHostConfirmation())
                ShowConfirm(PendingAction.Leave);
            else
                LeaveSession();
        }

        void OnQuitClicked()
        {
            if (NeedsHostConfirmation())
                ShowConfirm(PendingAction.Quit);
            else
                QuitGame();
        }

        void OnConfirmYes()
        {
            var action = pendingAction;
            HideConfirm();
            if (action == PendingAction.Leave) LeaveSession();
            else if (action == PendingAction.Quit) QuitGame();
        }

        static bool NeedsHostConfirmation()
        {
            return SessionContext.Mode == GameMode.CoopHost && NetworkServer.connections.Count > 1;
        }

        void ShowConfirm(PendingAction action)
        {
            pendingAction = action;
            int others = NetworkServer.connections.Count - 1;
            confirmLabel.text = $"Host sensin. Ayrılırsan oturum {others} oyuncu için de kapanacak.";
            menuPanel.AddToClassList("hidden");
            confirmPanel.RemoveFromClassList("hidden");
        }

        void HideConfirm()
        {
            pendingAction = PendingAction.None;
            menuPanel?.RemoveFromClassList("hidden");
            confirmPanel?.AddToClassList("hidden");
        }

        void LeaveSession()
        {
            Time.timeScale = 1f;
            InputGate.SetBlocked(false);
            LFNetworkManager.Instance?.LeaveSession();
        }

        void QuitGame()
        {
            LeaveSession();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static string GetLocalIPv4()
        {
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
            }
            catch (SocketException) { }
            return "localhost";
        }
    }
}
