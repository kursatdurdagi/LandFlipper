using System;
using System.Collections.Generic;
using LandFlipper.Core;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// Ayarlar ekranı (UI Toolkit). Ana menüden ve oyun içi menüden açılır.
    /// Sekmeler: Görüntü · Kontroller · Ses · Tuşlar. Her değişiklik SettingsService üzerinden
    /// anında uygulanır ve kaydedilir; tuş atamaları LFInput'a yazılır.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SettingsController : MonoBehaviour
    {
        enum Tab { Display, Controls, Audio, Keys }

        public static SettingsController Instance { get; private set; }
        public static bool IsOpen { get; private set; }
        public static bool IsRebinding { get; private set; }
        /// <summary>Esc bu karede ayarlarda kullanıldıysa diğer menüler aynı basışa tepki vermesin.</summary>
        public static int ConsumedEscapeFrame { get; private set; } = -1;

        static readonly string[] DisplayModeNames = { "Tam Ekran", "Pencereli Tam Ekran", "Pencere" };
        static readonly string[] AntiAliasNames = { "Kapalı", "FXAA", "SMAA" };

        VisualElement overlay;
        ScrollView content;
        Label hintLabel;
        Button resetButton, closeButton;
        readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        Tab currentTab = Tab.Display;

        InputAction pauseAction;
        InputActionRebindingExtensions.RebindingOperation rebindOperation;
        bool wasBlocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            IsOpen = false;
            IsRebinding = false;
            ConsumedEscapeFrame = -1;
        }

        // ------------------------------------------------------------ yaşam döngüsü

        void OnEnable()
        {
            Instance = this;
            var root = GetComponent<UIDocument>().rootVisualElement;

            overlay = root.Q("settings-overlay");
            content = root.Q<ScrollView>("settings-content");
            hintLabel = root.Q<Label>("settings-hint");
            resetButton = root.Q<Button>("settings-reset");
            closeButton = root.Q<Button>("settings-close");

            tabButtons.Clear();
            BindTab("tab-display", Tab.Display);
            BindTab("tab-controls", Tab.Controls);
            BindTab("tab-audio", Tab.Audio);
            BindTab("tab-keys", Tab.Keys);

            resetButton.clicked += OnResetClicked;
            closeButton.clicked += Close;

            LFInput.EnsureCreated();
            pauseAction = LFInput.Pause;
            pauseAction.performed += OnPausePerformed;

            // Panel boş tıklamada kapanmasın, dış (karartılmış) alana tıklayınca kapansın
            overlay.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == overlay && !IsRebinding) Close();
            });

            overlay.EnableInClassList("hidden", true);
            IsOpen = false;
        }

        void OnDisable()
        {
            CancelRebind();
            resetButton.clicked -= OnResetClicked;
            closeButton.clicked -= Close;
            if (pauseAction != null) pauseAction.performed -= OnPausePerformed;

            if (IsOpen)
            {
                IsOpen = false;
                InputGate.SetBlocked(wasBlocked);
            }
            if (Instance == this) Instance = null;
        }

        void BindTab(string name, Tab tab)
        {
            var button = overlay.Q<Button>(name);
            if (button == null) return;
            tabButtons[tab] = button;
            button.clicked += () => ShowTab(tab);
        }

        // ------------------------------------------------------------ aç / kapat

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            wasBlocked = InputGate.IsBlocked;
            InputGate.SetBlocked(true);
            CursorUtility.SetLocked(false);
            overlay.EnableInClassList("hidden", false);
            ShowTab(currentTab);
        }

        public void Close()
        {
            if (!IsOpen) return;
            CancelRebind();
            IsOpen = false;
            overlay.EnableInClassList("hidden", true);
            InputGate.SetBlocked(wasBlocked);
            CursorUtility.SetLocked(!wasBlocked && NetworkClient.localPlayer != null);
        }

        void OnPausePerformed(InputAction.CallbackContext _)
        {
            if (!IsOpen) return;
            if (IsRebinding) return; // Esc atamayı iptal eder, onu rebind işlemi hallediyor
            ConsumedEscapeFrame = Time.frameCount;
            Close();
        }

        void OnResetClicked()
        {
            CancelRebind();
            if (currentTab == Tab.Keys)
            {
                LFInput.ResetAllBindings();
                SetHint("Tüm tuşlar varsayılana döndü.");
            }
            else
            {
                SettingsService.ResetToDefaults();
                SetHint("Ayarlar varsayılana döndü.");
            }
            ShowTab(currentTab);
        }

        void SetHint(string text) => hintLabel.text = text;

        // ------------------------------------------------------------ sekmeler

        void ShowTab(Tab tab)
        {
            CancelRebind();
            currentTab = tab;
            foreach (var pair in tabButtons)
                pair.Value.EnableInClassList("active", pair.Key == tab);

            content.Clear();
            switch (tab)
            {
                case Tab.Display: BuildDisplay(); break;
                case Tab.Controls: BuildControls(); break;
                case Tab.Audio: BuildAudio(); break;
                case Tab.Keys: BuildKeys(); break;
            }
            content.scrollOffset = Vector2.zero;

            resetButton.text = tab == Tab.Keys ? "Tuşları Sıfırla" : "Varsayılana Döndür";
            SetHint(tab == Tab.Keys
                ? "Değiştirmek için bir tuşa tıkla, sonra yeni tuşa bas. Esc iptal eder."
                : "Değişiklikler anında uygulanır ve kaydedilir.");
        }

        void BuildDisplay()
        {
            var s = SettingsService.Current;

            Section("EKRAN");
            Stepper("Ekran Modu", DisplayModeNames, Mathf.Clamp(s.displayMode, 0, 2),
                i => SettingsService.Modify(x => x.displayMode = i));

            var resolutions = SettingsService.AvailableResolutions();
            var resNames = new string[resolutions.Count];
            int resIndex = 0;
            for (int i = 0; i < resolutions.Count; i++)
            {
                resNames[i] = $"{resolutions[i].width} × {resolutions[i].height}";
                bool match = s.resolutionWidth > 0
                    ? resolutions[i].width == s.resolutionWidth && resolutions[i].height == s.resolutionHeight
                    : resolutions[i].width == Screen.currentResolution.width && resolutions[i].height == Screen.currentResolution.height;
                if (match) resIndex = i;
            }
            Stepper("Çözünürlük", resNames, resIndex, i => SettingsService.Modify(x =>
            {
                x.resolutionWidth = resolutions[i].width;
                x.resolutionHeight = resolutions[i].height;
            }));

            Switch("Dikey Senkron (VSync)", s.vsync, v => { SettingsService.Modify(x => x.vsync = v); ShowTab(Tab.Display); });
            if (!s.vsync)
                Stepper("FPS Sınırı", SettingsService.FrameLimitNames, Mathf.Clamp(s.frameLimitIndex, 0, SettingsService.FrameLimits.Length - 1),
                    i => SettingsService.Modify(x => x.frameLimitIndex = i));

            Section("GÖRÜNTÜ");
            var qualityNames = QualitySettings.names;
            int quality = s.qualityLevel >= 0 ? s.qualityLevel : QualitySettings.GetQualityLevel();
            Stepper("Grafik Kalitesi", qualityNames, Mathf.Clamp(quality, 0, qualityNames.Length - 1),
                i => SettingsService.Modify(x => x.qualityLevel = i));
            Slider("Görüş Alanı (FOV)", 60f, 100f, s.fov, v => $"{v:0}°", v => SettingsService.Modify(x => x.fov = Mathf.Round(v)));
            Slider("Parlaklık", -1f, 1f, s.brightness, v => (v >= 0 ? "+" : "") + Mathf.RoundToInt(v * 100) + "%",
                v => SettingsService.Modify(x => x.brightness = v));
            Switch("Gölgeler", s.shadows, v => SettingsService.Modify(x => x.shadows = v));
            Switch("Son İşleme (Post-Process)", s.postProcessing, v => SettingsService.Modify(x => x.postProcessing = v));
            Stepper("Kenar Yumuşatma", AntiAliasNames, Mathf.Clamp(s.antiAliasing, 0, 2),
                i => SettingsService.Modify(x => x.antiAliasing = i));
        }

        void BuildControls()
        {
            var s = SettingsService.Current;
            Section("FARE");
            Slider("Fare Hassasiyeti", 0.02f, 0.4f, s.mouseSensitivity, v => (v / 0.12f).ToString("0.00") + "x",
                v => SettingsService.Modify(x => x.mouseSensitivity = v));
            Switch("Dikey Ekseni Ters Çevir", s.invertY, v => SettingsService.Modify(x => x.invertY = v));

            Section("GAMEPAD");
            Slider("Gamepad Hassasiyeti", 60f, 360f, s.gamepadSensitivity, v => (v / 180f).ToString("0.00") + "x",
                v => SettingsService.Modify(x => x.gamepadSensitivity = v));
        }

        void BuildAudio()
        {
            var s = SettingsService.Current;
            Section("SES SEVİYESİ");
            Slider("Ana Ses", 0f, 1f, s.masterVolume, Percent, v => SettingsService.Modify(x => x.masterVolume = v));
            Slider("Müzik", 0f, 1f, s.musicVolume, Percent, v => SettingsService.Modify(x => x.musicVolume = v));
            Slider("Efektler", 0f, 1f, s.sfxVolume, Percent, v => SettingsService.Modify(x => x.sfxVolume = v));
        }

        static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

        // ------------------------------------------------------------ tuş atama

        void BuildKeys()
        {
            Section("KLAVYE / FARE");
            foreach (var entry in LFInput.GetRebindableBindings())
                KeyRow(entry);

            Section("SABİT");
            var row = Row("Duraklat / Menü", null);
            var fixedKey = new Button { text = "Esc" };
            fixedKey.AddToClassList("key-button");
            fixedKey.SetEnabled(false);
            row.control.Add(fixedKey);
            var spacer = new VisualElement();
            spacer.AddToClassList("key-reset");
            spacer.style.visibility = UnityEngine.UIElements.Visibility.Hidden;
            row.control.Add(spacer);
        }

        void KeyRow(LFInput.RebindEntry entry)
        {
            var row = Row(entry.Label, null);

            bool conflict = LFInput.HasConflict(entry.Action, entry.BindingIndex);
            if (conflict)
            {
                var warn = new Label("Çakışıyor");
                warn.AddToClassList("key-conflict-label");
                row.control.Add(warn);
            }

            var keyButton = new Button { text = LFInput.DisplayName(entry.Action, entry.BindingIndex) };
            keyButton.AddToClassList("key-button");
            keyButton.EnableInClassList("conflict", conflict);
            keyButton.clicked += () => StartRebind(entry, keyButton);
            row.control.Add(keyButton);

            var reset = new Button { text = "↺", tooltip = "Varsayılan tuş" };
            reset.AddToClassList("key-reset");
            reset.clicked += () =>
            {
                CancelRebind();
                entry.Action.RemoveBindingOverride(entry.BindingIndex);
                LFInput.SaveOverrides();
                ShowTab(Tab.Keys);
            };
            row.control.Add(reset);
        }

        void StartRebind(LFInput.RebindEntry entry, Button keyButton)
        {
            if (IsRebinding) return;
            IsRebinding = true;
            keyButton.text = "Bir tuşa bas…";
            keyButton.AddToClassList("listening");
            SetHint($"\"{entry.Label}\" için yeni tuşa bas. İptal: Esc");

            var action = entry.Action;
            bool wasEnabled = action.enabled;
            action.Disable();

            rebindOperation = action.PerformInteractiveRebinding(entry.BindingIndex)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Mouse>/press")
                .WithControlsExcluding("<Mouse>/clickCount")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(op => FinishRebind(action, wasEnabled, true, entry.Label))
                .OnCancel(op => FinishRebind(action, wasEnabled, false, entry.Label))
                .Start();
        }

        void FinishRebind(InputAction action, bool wasEnabled, bool completed, string label)
        {
            rebindOperation?.Dispose();
            rebindOperation = null;
            IsRebinding = false;
            ConsumedEscapeFrame = Time.frameCount; // iptal eden Esc menüyü kapatmasın
            if (wasEnabled) action.Enable();

            if (completed)
                LFInput.SaveOverrides();

            if (IsOpen && currentTab == Tab.Keys)
            {
                ShowTab(Tab.Keys);
                SetHint(completed ? $"\"{label}\" kaydedildi." : "Atama iptal edildi.");
            }
        }

        void CancelRebind()
        {
            if (rebindOperation == null) return;
            rebindOperation.Cancel(); // OnCancel → FinishRebind
            rebindOperation?.Dispose();
            rebindOperation = null;
            IsRebinding = false;
        }

        // ------------------------------------------------------------ satır üreticileri

        struct RowParts
        {
            public VisualElement row;
            public VisualElement control;
        }

        void Section(string title)
        {
            var label = new Label(title);
            label.AddToClassList("settings-section");
            content.Add(label);
        }

        RowParts Row(string title, string sub)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-row");

            var labelBox = new VisualElement();
            var label = new Label(title);
            label.AddToClassList("settings-row-label");
            labelBox.Add(label);
            if (!string.IsNullOrEmpty(sub))
            {
                var subLabel = new Label(sub);
                subLabel.AddToClassList("settings-row-sub");
                labelBox.Add(subLabel);
            }
            row.Add(labelBox);

            var control = new VisualElement();
            control.AddToClassList("settings-row-control");
            row.Add(control);

            content.Add(row);
            return new RowParts { row = row, control = control };
        }

        void Slider(string title, float min, float max, float value, Func<float, string> format, Action<float> onChange)
        {
            var parts = Row(title, null);
            var slider = new Slider(min, max) { value = Mathf.Clamp(value, min, max) };
            slider.AddToClassList("lf-slider");
            var valueLabel = new Label(format(slider.value));
            valueLabel.AddToClassList("settings-value");
            slider.RegisterValueChangedCallback(e =>
            {
                valueLabel.text = format(e.newValue);
                onChange(e.newValue);
            });
            parts.control.Add(slider);
            parts.control.Add(valueLabel);
        }

        void Switch(string title, bool value, Action<bool> onChange)
        {
            var parts = Row(title, null);
            var button = new Button();
            button.AddToClassList("lf-switch");
            bool state = value;
            void Paint()
            {
                button.text = state ? "AÇIK" : "KAPALI";
                button.EnableInClassList("on", state);
            }
            Paint();
            button.clicked += () =>
            {
                state = !state;
                Paint();
                onChange(state);
            };
            parts.control.Add(button);
        }

        void Stepper(string title, string[] options, int index, Action<int> onChange)
        {
            var parts = Row(title, null);
            if (options == null || options.Length == 0) return;

            var box = new VisualElement();
            box.AddToClassList("lf-stepper");
            var left = new Button { text = "‹" };
            var right = new Button { text = "›" };
            left.AddToClassList("lf-stepper-arrow");
            right.AddToClassList("lf-stepper-arrow");
            var valueLabel = new Label(options[index]);
            valueLabel.AddToClassList("lf-stepper-value");

            int current = index;
            void Step(int delta)
            {
                current = (current + delta + options.Length) % options.Length;
                valueLabel.text = options[current];
                onChange(current);
            }
            left.clicked += () => Step(-1);
            right.clicked += () => Step(1);

            box.Add(left);
            box.Add(valueLabel);
            box.Add(right);
            parts.control.Add(box);
        }
    }
}
