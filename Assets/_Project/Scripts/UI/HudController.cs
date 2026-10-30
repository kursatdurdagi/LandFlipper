using System.Text;
using System.Text.RegularExpressions;
using LandFlipper.Core;
using LandFlipper.Gameplay;
using LandFlipper.Player;
using LandFlipper.Player.Tools;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// Oyun içi HUD (UI Toolkit): etkileşim ipucu, ortak bütçe, seviye/XP, taşınan yük ve bildirimler (GDD 17).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudController : MonoBehaviour
    {
        [SerializeField] float toastDuration = 2.4f;
        [SerializeField] float xpPopupDuration = 1.1f;
        [SerializeField] float levelUpDuration = 2.8f;

        Label promptLabel, moneyLabel, carryTitle, carryList, toastLabel;
        Label levelLabel, xpLabel, pointsLabel, xpPopup, levelUpTitle;
        VisualElement carryPanel, moneyPanel, xpFill, levelUpBanner;
        RadialProgress holdRing;

        PlayerInteractor interactor;
        PlayerCarry carry;
        float toastHideTime;
        string lastPrompt;
        string lastCarry;
        int shownMoney = -1;
        float displayedMoney;
        int shownLevel = -1, shownXp = -1, shownPoints = -1;

        int pendingXp;
        float xpPopupHideTime = -1f;
        float levelUpHideTime = -1f;

        readonly StringBuilder builder = new StringBuilder();

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            hintLabel = root.Q<Label>("hint-label");
            if (hintLabel != null) root.schedule.Execute(RefreshHint).Every(500);
            promptLabel = root.Q<Label>("prompt-label");
            moneyLabel = root.Q<Label>("money-label");
            moneyPanel = root.Q("money-panel");
            carryPanel = root.Q("carry-panel");
            carryTitle = root.Q<Label>("carry-title");
            carryList = root.Q<Label>("carry-list");
            toastLabel = root.Q<Label>("toast-label");
            levelLabel = root.Q<Label>("hud-level-label");
            xpLabel = root.Q<Label>("hud-xp-label");
            xpFill = root.Q("hud-xp-fill");
            pointsLabel = root.Q<Label>("hud-points");
            xpPopup = root.Q<Label>("xp-popup");
            levelUpBanner = root.Q("levelup-banner");
            levelUpTitle = root.Q<Label>("levelup-title");

            // Basılı tutma halkası (nişangâhın etrafında)
            holdRing = new RadialProgress { name = "hold-ring" };
            holdRing.AddToClassList("hud-hold-ring");
            root.Q("hud-root").Add(holdRing);
            SetVisible(holdRing, false);

            SetVisible(promptLabel, false);
            SetVisible(carryPanel, false);
            SetVisible(toastLabel, false);
            SetVisible(xpPopup, false);
            SetVisible(levelUpBanner, false);

            CareerState.MoneyChanged += OnMoneyChanged;
            CareerState.XpGained += OnXpGained;
            CareerState.LeveledUp += OnLeveledUp;
            CareerState.SkillUpgraded += OnSkillUpgraded;
            PlayerSkills.LocalNotification += ShowToast;
            PlayerTools.LocalNotification += ShowToast;
            CareerState.ToolUpgraded += OnToolUpgraded;
        }

        Label hintLabel;
        string lastHint;

        /// <summary>Alt ipucu satırı, oyuncunun atadığı tuşlarla güncellenir.</summary>
        void RefreshHint()
        {
            string text =
                HintKey(LFInput.KeyLabel(LFInput.Primary)) + "Kes     " +
                HintKey(LFInput.KeyLabel(LFInput.Interact)) + "Al / sat     " +
                HintKey(LFInput.KeyLabel(LFInput.Drop)) + "Bırak     " +
                HintKey(LFInput.KeyLabel(LFInput.SkillTree)) + "Yetenekler     " +
                HintKey(LFInput.KeyLabel(LFInput.ToggleView)) + "Görünüm     " +
                HintKey("Esc") + "Menü";
            if (text == lastHint) return;
            lastHint = text;
            hintLabel.text = text;
        }

        void OnDisable()
        {
            CareerState.MoneyChanged -= OnMoneyChanged;
            CareerState.XpGained -= OnXpGained;
            CareerState.LeveledUp -= OnLeveledUp;
            CareerState.SkillUpgraded -= OnSkillUpgraded;
            PlayerSkills.LocalNotification -= ShowToast;
            PlayerTools.LocalNotification -= ShowToast;
            CareerState.ToolUpgraded -= OnToolUpgraded;
            if (interactor != null) interactor.Notification -= ShowToast;
            interactor = null;
            carry = null;
        }

        void Update()
        {
            if (interactor == null) TryBindLocalPlayer();

            UpdatePrompt();
            UpdateHoldRing();
            UpdateCarry();
            UpdateCareer();

            float now = Time.unscaledTime;
            if (!toastLabel.ClassListContains("hidden") && now > toastHideTime)
                SetVisible(toastLabel, false);

            if (xpPopupHideTime > 0f && now > xpPopupHideTime)
            {
                xpPopupHideTime = -1f;
                pendingXp = 0;
                FadeOut(xpPopup);
            }

            if (levelUpHideTime > 0f && now > levelUpHideTime)
            {
                levelUpHideTime = -1f;
                FadeOut(levelUpBanner);
            }
        }

        void TryBindLocalPlayer()
        {
            var local = NetworkClient.localPlayer;
            if (local == null) return;
            interactor = local.GetComponent<PlayerInteractor>();
            carry = local.GetComponent<PlayerCarry>();
            if (interactor != null) interactor.Notification += ShowToast;
        }

        // Tuş adları ipucunda vurgulanır: "[E] Al" → "E  Al"
        const string KeyColor = "#4E8530";
        static readonly Regex KeyPattern = new Regex(@"\[([^\]]+)\]\s*");

        static string Key(string key) => $"<b><color={KeyColor}>{key}</color></b>  ";
        // alt tuş satırı koyu konturlu açık metin; tuşlar orada güneş sarısı
        static string HintKey(string key) => $"<color=#F5C24A>{key}</color>  ";

        static string FormatPrompt(string prompt) =>
            string.IsNullOrEmpty(prompt) ? prompt : KeyPattern.Replace(prompt, m => Key(m.Groups[1].Value));

        void UpdatePrompt()
        {
            string prompt = interactor != null ? interactor.Prompt : string.Empty;
            if (prompt == lastPrompt) return;
            lastPrompt = prompt;
            promptLabel.text = FormatPrompt(prompt);
            SetVisible(promptLabel, !string.IsNullOrEmpty(prompt));
        }

        void UpdateHoldRing()
        {
            float progress = interactor != null ? interactor.HoldProgress : 0f;
            bool show = progress > 0f;
            if (show != !holdRing.ClassListContains("hidden")) SetVisible(holdRing, show);
            holdRing.Progress = progress;
        }

        void UpdateCarry()
        {
            builder.Clear();
            if (carry != null && carry.IsCarrying)
            {
                var items = carry.Items;
                for (int i = items.Count - 1; i >= 0; i--) // en üstteki önce
                {
                    var def = items[i].Definition;
                    builder.Append(def.displayName).Append("  ").Append(Currency.Format(CareerState.SellPrice(def)));
                    if (i > 0) builder.Append('\n');
                }
            }

            string text = builder.ToString();
            string title = carry != null ? $"YÜK {carry.UsedSlots}/{carry.CapacitySlots}" : string.Empty;
            string key = title + text;
            if (key == lastCarry) return;
            lastCarry = key;

            carryTitle.text = title;
            carryList.text = text;
            SetVisible(carryPanel, text.Length > 0);
        }

        void UpdateCareer()
        {
            var career = CareerState.Instance;
            int money = career != null ? career.Money : 0;

            // para göstergesi hedefe sayarak ilerler (tık tık tık)
            if (shownMoney < 0 || money < displayedMoney) displayedMoney = money;
            else if (displayedMoney < money)
            {
                float speed = Mathf.Max(40f, (money - displayedMoney) * 6f);
                displayedMoney = Mathf.MoveTowards(displayedMoney, money, speed * Time.unscaledDeltaTime);
            }

            int visible = Mathf.RoundToInt(displayedMoney);
            if (visible != shownMoney)
            {
                shownMoney = visible;
                moneyLabel.text = Currency.Format(visible);
            }

            if (career == null) return;
            if (career.Level != shownLevel || career.Xp != shownXp)
            {
                shownLevel = career.Level;
                shownXp = career.Xp;
                levelLabel.text = shownLevel.ToString();
                xpLabel.text = $"{shownXp} / {career.XpToNextLevel} XP";
                xpFill.style.width = Length.Percent(100f * shownXp / Mathf.Max(1, career.XpToNextLevel));
            }

            if (career.SkillPoints != shownPoints)
            {
                shownPoints = career.SkillPoints;
                pointsLabel.text = $"{shownPoints} puan!";
                SetVisible(pointsLabel, shownPoints > 0);
            }
        }

        // ------------------------------------------------------------ events

        void OnMoneyChanged(int oldValue, int newValue)
        {
            if (newValue <= oldValue) return;
            moneyPanel.AddToClassList("pulse");
            moneyPanel.schedule.Execute(() => moneyPanel.RemoveFromClassList("pulse")).StartingIn(120);
        }

        void OnXpGained(int amount)
        {
            pendingXp += amount; // art arda kazanımlar tek etikette toplanır
            xpPopup.text = $"+{pendingXp} XP";
            FadeIn(xpPopup);
            xpPopupHideTime = Time.unscaledTime + xpPopupDuration;
        }

        void OnLeveledUp(int level)
        {
            levelUpTitle.text = $"Seviye {level}!";
            FadeIn(levelUpBanner);
            levelUpHideTime = Time.unscaledTime + levelUpDuration;
        }

        void OnSkillUpgraded(string skillName, int level) => ShowToast($"{skillName} · Sv {level} açıldı");

        void OnToolUpgraded(ToolKind kind, ToolDefinition tool)
        {
            if (tool != null) ShowToast($"Yeni alet: {tool.displayName} · {tool.PowerText}");
        }

        void ShowToast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            toastLabel.text = message;
            SetVisible(toastLabel, true);
            toastHideTime = Time.unscaledTime + toastDuration;
        }

        // ------------------------------------------------------------ helpers

        static void SetVisible(VisualElement element, bool visible) => element.EnableInClassList("hidden", !visible);

        static void FadeIn(VisualElement element)
        {
            element.RemoveFromClassList("hidden");
            element.RemoveFromClassList("fade");
        }

        static void FadeOut(VisualElement element)
        {
            element.AddToClassList("fade");
            element.schedule.Execute(() =>
            {
                if (element.ClassListContains("fade")) element.AddToClassList("hidden");
            }).StartingIn(300);
        }
    }
}
