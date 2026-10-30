using System.Collections.Generic;
using LandFlipper.Core;
using LandFlipper.Gameplay;
using LandFlipper.Player;
using LandFlipper.Player.Tools;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// Alet Tezgâhı ekranı (UI Toolkit). Tezgâhta E ile açılır; Esc / E / Kapat ile kapanır.
    /// Her alet türü için bir kart: mevcut alet, sonraki kademe, fiyat ve seviye şartı.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class WorkbenchController : MonoBehaviour
    {
        public static bool IsAnyOpen { get; private set; }
        /// <summary>Esc ile bu karede kapandıysa oyun menüsü aynı tuşla açılmasın.</summary>
        public static int ClosedFrame { get; private set; } = -1;

        static readonly ToolKind[] Kinds = { ToolKind.Shears, ToolKind.Axe, ToolKind.Hammer };

        VisualElement overlay, cardsRoot;
        Label levelLabel, moneyLabel;
        Button closeButton;
        InputAction closeAction, interactAction;
        IVisualElementScheduledItem refreshJob;
        bool built;

        class CardView
        {
            public ToolKind Kind;
            public VisualElement Card;
            public Label Name, Desc, Stat, NextName, NextStat, Requirement, Price, Tier;
            public VisualElement Next;
            public readonly List<VisualElement> Pips = new List<VisualElement>();
            public Button Button;
        }

        readonly List<CardView> cards = new List<CardView>();

        public bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsAnyOpen = false;
            ClosedFrame = -1;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            overlay = root.Q("bench-overlay");
            cardsRoot = root.Q("bench-cards");
            levelLabel = root.Q<Label>("bench-level");
            moneyLabel = root.Q<Label>("bench-money");
            closeButton = root.Q<Button>("bench-close");
            closeButton.clicked += Close;

            LFInput.EnsureCreated();
            closeAction = LFInput.Pause;
            closeAction.performed += OnCloseKey;
            interactAction = LFInput.Interact;
            interactAction.performed += OnCloseKey;

            PlayerInteractor.WorkbenchRequested += Open;

            refreshJob = root.schedule.Execute(Refresh).Every(150);
            refreshJob.Pause();
            SetOpen(false);
        }

        void OnDisable()
        {
            closeButton.clicked -= Close;
            if (closeAction != null) closeAction.performed -= OnCloseKey;
            if (interactAction != null) interactAction.performed -= OnCloseKey;
            PlayerInteractor.WorkbenchRequested -= Open;
            refreshJob?.Pause();

            if (IsOpen)
            {
                InputGate.SetBlocked(false);
                IsAnyOpen = false;
            }
            built = false;
            cards.Clear();
        }

        void OnCloseKey(InputAction.CallbackContext _)
        {
            if (!IsOpen || SettingsController.IsOpen) return;
            Close();
            ClosedFrame = Time.frameCount;
        }

        public void Open()
        {
            if (IsOpen || InputGate.IsBlocked || CareerState.Instance == null) return;
            SetOpen(true);
        }

        public void Close() => SetOpen(false);

        void SetOpen(bool open)
        {
            IsOpen = open;
            IsAnyOpen = open;
            overlay.EnableInClassList("hidden", !open);
            InputGate.SetBlocked(open);
            CursorUtility.SetLocked(!open && NetworkClient.localPlayer != null);

            if (open)
            {
                BuildIfNeeded();
                Refresh();
                refreshJob.Resume();
            }
            else
            {
                refreshJob.Pause();
            }
        }

        // ------------------------------------------------------------ build

        void BuildIfNeeded()
        {
            if (built) return;
            built = true;
            cardsRoot.Clear();
            cards.Clear();
            foreach (var kind in Kinds)
            {
                var view = BuildCard(kind);
                cards.Add(view);
                cardsRoot.Add(view.Card);
            }
        }

        CardView BuildCard(ToolKind kind)
        {
            var view = new CardView { Kind = kind, Card = new VisualElement() };
            view.Card.AddToClassList("tool-card");

            // üst satır: tür etiketi + kademe
            var caption = new VisualElement();
            caption.AddToClassList("tool-caption-row");
            var kindLabel = new Label($"{ToolDefinition.KindName(kind).ToUpperInvariant()}  ·  {ToolDefinition.KindUse(kind).ToUpperInvariant()}");
            kindLabel.AddToClassList("tool-kind");
            view.Tier = new Label();
            view.Tier.AddToClassList("tool-tier");
            caption.Add(kindLabel);
            caption.Add(view.Tier);
            view.Card.Add(caption);

            var top = new VisualElement();
            top.AddToClassList("tool-card-top");

            var icon = new VisualElement();
            icon.AddToClassList("tool-icon");
            icon.AddToClassList("tool-kind-" + kind.ToString().ToLowerInvariant());
            var iconText = new Label(ToolDefinition.KindName(kind).Substring(0, 1));
            iconText.AddToClassList("tool-icon-text");
            icon.Add(iconText);

            var group = new VisualElement();
            group.AddToClassList("tool-kind-group");
            view.Name = new Label();
            view.Name.AddToClassList("tool-name");
            var pips = new VisualElement();
            pips.AddToClassList("tool-pips");
            int maxTier = CareerState.Instance != null ? CareerState.Instance.MaxToolTier(kind) : 2;
            for (int i = 0; i <= maxTier; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("tool-pip");
                pips.Add(pip);
                view.Pips.Add(pip);
            }
            group.Add(view.Name);
            group.Add(pips);

            top.Add(icon);
            top.Add(group);
            view.Card.Add(top);

            view.Desc = new Label();
            view.Desc.AddToClassList("tool-desc");
            view.Card.Add(view.Desc);

            view.Stat = new Label();
            view.Stat.AddToClassList("tool-stat");
            view.Card.Add(view.Stat);

            var spacer = new VisualElement();
            spacer.AddToClassList("tool-spacer");
            view.Card.Add(spacer);

            view.Next = new VisualElement();
            view.Next.AddToClassList("tool-next");
            var nextCaption = new Label("SONRAKİ KADEME");
            nextCaption.AddToClassList("tool-next-caption");
            var nextRow = new VisualElement();
            nextRow.AddToClassList("tool-next-row");
            view.NextName = new Label();
            view.NextName.AddToClassList("tool-next-name");
            view.Price = new Label();
            view.Price.AddToClassList("tool-price");
            nextRow.Add(view.NextName);
            nextRow.Add(view.Price);
            view.NextStat = new Label();
            view.NextStat.AddToClassList("tool-next-stat");
            view.Next.Add(nextCaption);
            view.Next.Add(nextRow);
            view.Next.Add(view.NextStat);
            view.Card.Add(view.Next);

            view.Button = new Button(() => PlayerTools.Local?.RequestUpgrade(kind));
            view.Button.AddToClassList("tool-button");
            view.Card.Add(view.Button);
            return view;
        }

        // ------------------------------------------------------------ refresh

        void Refresh()
        {
            var career = CareerState.Instance;
            if (career == null) return;

            if (levelLabel != null) levelLabel.text = career.Level.ToString();
            moneyLabel.text = Currency.Format(career.Money);

            foreach (var view in cards)
            {
                var current = career.CurrentTool(view.Kind);
                var next = career.NextTool(view.Kind);
                int tier = career.GetToolTier(view.Kind);

                view.Name.text = current != null ? current.displayName : "—";
                view.Desc.text = current != null ? current.description : string.Empty;
                view.Stat.text = current != null ? current.PowerText : string.Empty;
                view.Tier.text = $"{tier + 1} / {view.Pips.Count}";
                for (int i = 0; i < view.Pips.Count; i++)
                    view.Pips[i].EnableInClassList("filled", i <= tier);

                bool maxed = next == null;
                bool canUpgrade = career.CanUpgradeTool(view.Kind, out string reason);
                view.Card.EnableInClassList("maxed", maxed);
                view.Card.EnableInClassList("affordable", canUpgrade);
                view.Next.style.display = maxed ? DisplayStyle.None : DisplayStyle.Flex;

                if (!maxed)
                {
                    view.NextName.text = next.displayName;
                    view.Price.text = Currency.Format(next.price);
                    view.Price.EnableInClassList("missing", career.Money < next.price);
                    view.NextStat.text = next.PowerText;
                }

                view.Button.SetEnabled(canUpgrade);
                view.Button.text = maxed
                    ? "En iyi alet sende"
                    : canUpgrade
                        ? "Satın al"
                        : "Bütçe yetersiz";
            }
        }
    }
}
