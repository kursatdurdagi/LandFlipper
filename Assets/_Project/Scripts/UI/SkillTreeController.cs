using System.Collections.Generic;
using System.Linq;
using LandFlipper.Core;
using LandFlipper.Gameplay;
using LandFlipper.Player;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// Yetenek ağacı ekranı (UI Toolkit, GDD 13 ve 17). TAB ile açılır/kapanır, Esc ile kapanır.
    /// Kartlar CareerState'teki SkillDefinition listesinden dal dal üretilir.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SkillTreeController : MonoBehaviour
    {
        public static bool IsAnyOpen { get; private set; }
        /// <summary>Esc ile bu karede kapandıysa oyun menüsü aynı tuşla açılmasın.</summary>
        public static int ClosedFrame { get; private set; } = -1;

        static readonly string[] BranchOrder = { "carry", "move", "gather", "yield" };

        VisualElement overlay, branchesRoot, pointsChip, xpFill;
        Label levelLabel, xpLabel, pointsLabel;
        InputAction toggleAction, closeAction;
        IVisualElementScheduledItem refreshJob;
        bool built;

        class CardView
        {
            public SkillDefinition Skill;
            public VisualElement Card;
            public VisualElement Connector;
            public List<VisualElement> Pips = new List<VisualElement>();
            public Label Effect;
            public Button Button;
        }

        readonly List<CardView> cards = new List<CardView>();

        public bool IsOpen { get; private set; }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            overlay = root.Q("skill-overlay");
            branchesRoot = root.Q("branches");
            pointsChip = root.Q("points-chip");
            xpFill = root.Q("xp-fill");
            levelLabel = root.Q<Label>("level-label");
            xpLabel = root.Q<Label>("xp-label");
            pointsLabel = root.Q<Label>("points-label");

            LFInput.EnsureCreated();
            toggleAction = LFInput.SkillTree;   // değiştirilebilir (Ayarlar → Tuşlar)
            toggleAction.performed += OnToggle;
            closeAction = LFInput.Pause;        // Esc
            closeAction.performed += OnClose;

            refreshJob = root.schedule.Execute(Refresh).Every(200);
            refreshJob.Pause();

            SetOpen(false);
        }

        void OnDisable()
        {
            if (toggleAction != null) toggleAction.performed -= OnToggle;
            if (closeAction != null) closeAction.performed -= OnClose;
            refreshJob?.Pause();

            if (IsOpen)
            {
                InputGate.SetBlocked(false);
                IsAnyOpen = false;
            }
            built = false;
            cards.Clear();
        }

        void OnToggle(InputAction.CallbackContext _)
        {
            if (SettingsController.IsOpen || SettingsController.IsRebinding) return; // tuş atanırken açılmasın
            if (!IsOpen && InputGate.IsBlocked) return; // oyun menüsü açıkken açılmasın
            if (!IsOpen && CareerState.Instance == null) return;
            SetOpen(!IsOpen);
        }

        void OnClose(InputAction.CallbackContext _)
        {
            if (!IsOpen) return;
            SetOpen(false);
            ClosedFrame = Time.frameCount;
        }

        public void SetOpen(bool open)
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
            var career = CareerState.Instance;
            if (built || career == null) return;
            built = true;
            branchesRoot.Clear();
            cards.Clear();

            var groups = career.Skills
                .Where(s => s != null)
                .GroupBy(s => s.branchId)
                .OrderBy(g => System.Array.IndexOf(BranchOrder, g.Key) is int i && i >= 0 ? i : 99);

            foreach (var group in groups)
            {
                var first = group.First();
                var branch = new VisualElement();
                branch.AddToClassList("skill-branch");

                var header = new VisualElement();
                header.AddToClassList("skill-branch-header");
                var dot = new VisualElement();
                dot.AddToClassList("skill-branch-dot");
                dot.AddToClassList("branch-" + first.branchId);
                var name = new Label(first.branchName.ToUpperInvariant());
                name.AddToClassList("skill-branch-name");
                header.Add(dot);
                header.Add(name);
                branch.Add(header);

                bool firstCard = true;
                foreach (var skill in group.OrderBy(s => s.branchOrder))
                {
                    VisualElement connector = null;
                    if (!firstCard)
                    {
                        connector = new VisualElement();
                        connector.AddToClassList("skill-connector");
                        branch.Add(connector);
                    }
                    firstCard = false;

                    var view = BuildCard(skill);
                    view.Connector = connector;
                    branch.Add(view.Card);
                    cards.Add(view);
                }

                branchesRoot.Add(branch);
            }
        }

        CardView BuildCard(SkillDefinition skill)
        {
            var view = new CardView { Skill = skill, Card = new VisualElement() };
            view.Card.AddToClassList("skill-card");

            var top = new VisualElement();
            top.AddToClassList("skill-card-top");

            var icon = new VisualElement();
            icon.AddToClassList("skill-icon");
            icon.AddToClassList("branch-" + skill.branchId);
            var iconText = new Label(skill.iconText);
            iconText.AddToClassList("skill-icon-text");
            icon.Add(iconText);

            var nameGroup = new VisualElement();
            nameGroup.AddToClassList("skill-name-group");
            var nameLabel = new Label(skill.displayName);
            nameLabel.AddToClassList("skill-name");
            var pips = new VisualElement();
            pips.AddToClassList("skill-pips");
            for (int i = 0; i < skill.maxLevel; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("skill-pip");
                pips.Add(pip);
                view.Pips.Add(pip);
            }
            nameGroup.Add(nameLabel);
            nameGroup.Add(pips);

            top.Add(icon);
            top.Add(nameGroup);
            view.Card.Add(top);

            var desc = new Label(skill.description);
            desc.AddToClassList("skill-desc");
            view.Card.Add(desc);

            view.Effect = new Label();
            view.Effect.AddToClassList("skill-effect");
            view.Card.Add(view.Effect);

            view.Button = new Button(() => PlayerSkills.Local?.RequestUpgrade(skill));
            view.Button.AddToClassList("skill-button");
            view.Card.Add(view.Button);
            return view;
        }

        // ------------------------------------------------------------ refresh

        void Refresh()
        {
            var career = CareerState.Instance;
            if (career == null) return;

            levelLabel.text = career.Level.ToString();
            xpLabel.text = $"{career.Xp} / {career.XpToNextLevel} XP";
            xpFill.style.width = Length.Percent(100f * career.Xp / Mathf.Max(1, career.XpToNextLevel));
            pointsLabel.text = career.SkillPoints.ToString();
            pointsChip.EnableInClassList("has-points", career.SkillPoints > 0);

            foreach (var view in cards)
            {
                var skill = view.Skill;
                int level = career.GetSkillLevel(skill);
                bool maxed = level >= skill.maxLevel;
                bool unlocked = career.IsUnlocked(skill);
                bool canUpgrade = career.CanUpgrade(skill, out string reason);

                for (int i = 0; i < view.Pips.Count; i++)
                    view.Pips[i].EnableInClassList("filled", i < level);

                view.Card.EnableInClassList("maxed", maxed);
                view.Card.EnableInClassList("locked", !unlocked);
                view.Card.EnableInClassList("affordable", canUpgrade);
                view.Connector?.EnableInClassList("active", unlocked);

                if (maxed)
                    view.Effect.text = $"Şu an: {skill.FormatEffect(level)}";
                else if (level == 0)
                    view.Effect.text = $"Sv 1: {skill.FormatEffect(1)}";
                else
                    view.Effect.text = $"Şu an: {skill.FormatEffect(level)}  →  {skill.FormatEffect(level + 1)}";

                view.Button.SetEnabled(canUpgrade);
                view.Button.text = maxed
                    ? "MAKSİMUM"
                    : !unlocked
                        ? $"Kilitli · {skill.prerequisite.displayName} Sv {skill.prerequisiteLevel}"
                        : canUpgrade
                            ? $"Geliştir · {skill.costPerLevel} puan"
                            : $"{skill.costPerLevel} puan gerekli";
            }
        }
    }
}
