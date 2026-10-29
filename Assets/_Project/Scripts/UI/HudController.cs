using System.Text;
using LandFlipper.Gameplay;
using LandFlipper.Player;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// Oyun içi HUD (UI Toolkit): etkileşim ipucu, ortak bütçe, taşınan yük ve kısa bildirimler (GDD 17).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudController : MonoBehaviour
    {
        [SerializeField] float toastDuration = 2.4f;

        Label promptLabel, moneyLabel, carryTitle, carryList, toastLabel;
        VisualElement carryPanel, moneyPanel;

        PlayerInteractor interactor;
        PlayerCarry carry;
        float toastHideTime;
        string lastPrompt;
        string lastCarry;
        int shownMoney = -1;

        readonly StringBuilder builder = new StringBuilder();

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            promptLabel = root.Q<Label>("prompt-label");
            moneyLabel = root.Q<Label>("money-label");
            moneyPanel = root.Q("money-panel");
            carryPanel = root.Q("carry-panel");
            carryTitle = root.Q<Label>("carry-title");
            carryList = root.Q<Label>("carry-list");
            toastLabel = root.Q<Label>("toast-label");

            SetVisible(promptLabel, false);
            SetVisible(carryPanel, false);
            SetVisible(toastLabel, false);
            CareerState.MoneyChanged += OnMoneyChanged;
        }

        void OnDisable()
        {
            CareerState.MoneyChanged -= OnMoneyChanged;
            if (interactor != null) interactor.Notification -= ShowToast;
            interactor = null;
            carry = null;
        }

        void Update()
        {
            if (interactor == null) TryBindLocalPlayer();

            UpdatePrompt();
            UpdateCarry();
            UpdateMoney();

            if (toastLabel.ClassListContains("hidden") == false && Time.unscaledTime > toastHideTime)
                SetVisible(toastLabel, false);
        }

        void TryBindLocalPlayer()
        {
            var local = NetworkClient.localPlayer;
            if (local == null) return;
            interactor = local.GetComponent<PlayerInteractor>();
            carry = local.GetComponent<PlayerCarry>();
            if (interactor != null) interactor.Notification += ShowToast;
        }

        void UpdatePrompt()
        {
            string prompt = interactor != null ? interactor.Prompt : string.Empty;
            if (prompt == lastPrompt) return;
            lastPrompt = prompt;
            promptLabel.text = prompt;
            SetVisible(promptLabel, !string.IsNullOrEmpty(prompt));
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
                    builder.Append(def.displayName).Append("  ").Append(Currency.Format(def.sellPrice));
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

        void UpdateMoney()
        {
            int money = CareerState.Instance != null ? CareerState.Instance.Money : 0;
            if (money == shownMoney) return;
            shownMoney = money;
            moneyLabel.text = Currency.Format(money);
        }

        void OnMoneyChanged(int oldValue, int newValue)
        {
            if (newValue <= oldValue) return;
            moneyPanel.AddToClassList("pulse");
            moneyPanel.schedule.Execute(() => moneyPanel.RemoveFromClassList("pulse")).StartingIn(250);
        }

        void ShowToast(string message)
        {
            toastLabel.text = message;
            SetVisible(toastLabel, true);
            toastHideTime = Time.unscaledTime + toastDuration;
        }

        static void SetVisible(VisualElement element, bool visible) => element.EnableInClassList("hidden", !visible);
    }
}
