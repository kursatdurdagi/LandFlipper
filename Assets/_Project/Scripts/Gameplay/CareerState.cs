using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Host kariyerinin ortak durumu (GDD 6.3): bütçe, XP, seviye, yetenek puanı ve yetenek seviyeleri.
    /// Tüm değişiklikler sadece host'ta, doğrulanmış işlemlerle yapılır; istemciler okur.
    /// Kalıcı kayıt Phase 9'da Career/Save sistemine bağlanacak.
    /// </summary>
    public class CareerState : NetworkBehaviour
    {
        public static CareerState Instance { get; private set; }

        /// <summary>(eski, yeni) — istemcilerde ve host'ta tetiklenir.</summary>
        public static event Action<int, int> MoneyChanged;
        /// <summary>(kazanılan XP) — herkese.</summary>
        public static event Action<int> XpGained;
        /// <summary>(yeni seviye) — herkese.</summary>
        public static event Action<int> LeveledUp;
        /// <summary>(yetenek adı, yeni seviye) — herkese.</summary>
        public static event Action<string, int> SkillUpgraded;

        [SerializeField] SkillDefinition[] skills = Array.Empty<SkillDefinition>();
        [Tooltip("Seviye 1→2 için gereken XP.")]
        [SerializeField] int baseXpToLevel = 40;
        [Tooltip("Her seviyede gereken XP'ye eklenen miktar.")]
        [SerializeField] int xpGrowthPerLevel = 30;
        [SerializeField] int skillPointsPerLevel = 1;

        [SyncVar(hook = nameof(OnMoneyChanged))] int money;
        [SyncVar] int level = 1;
        [SyncVar] int xp;
        [SyncVar] int skillPoints;

        readonly SyncDictionary<string, int> skillLevels = new SyncDictionary<string, int>();

        public int Money => money;
        public int Level => level;
        public int Xp => xp;
        public int XpToNextLevel => baseXpToLevel + (level - 1) * xpGrowthPerLevel;
        public int SkillPoints => skillPoints;
        public IReadOnlyList<SkillDefinition> Skills => skills;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------ okuma

        public int GetSkillLevel(SkillDefinition skill) =>
            skill != null && skillLevels.TryGetValue(skill.id, out int l) ? l : 0;

        public float GetStat(SkillStat stat)
        {
            float total = 0f;
            foreach (var skill in skills)
                if (skill != null && skill.stat == stat)
                    total += skill.ValueAt(GetSkillLevel(skill));
            return total;
        }

        /// <summary>Kariyer nesnesi yoksa (ör. menüde) 0 döner.</summary>
        public static float Stat(SkillStat stat) => Instance != null ? Instance.GetStat(stat) : 0f;

        public static int SellPrice(ResourceDefinition def) =>
            Mathf.RoundToInt(def.sellPrice * (1f + Stat(SkillStat.SellBonus)));

        public bool CanUpgrade(SkillDefinition skill, out string reason)
        {
            int current = GetSkillLevel(skill);
            if (current >= skill.maxLevel) { reason = "Maksimum seviye"; return false; }
            if (skill.prerequisite != null && GetSkillLevel(skill.prerequisite) < skill.prerequisiteLevel)
            {
                reason = $"Önce {skill.prerequisite.displayName} Sv {skill.prerequisiteLevel}";
                return false;
            }
            if (skillPoints < skill.costPerLevel) { reason = "Yetersiz yetenek puanı"; return false; }
            reason = null;
            return true;
        }

        public bool IsUnlocked(SkillDefinition skill) =>
            skill.prerequisite == null || GetSkillLevel(skill.prerequisite) >= skill.prerequisiteLevel;

        public SkillDefinition FindSkill(string id)
        {
            foreach (var skill in skills)
                if (skill != null && skill.id == id) return skill;
            return null;
        }

        // ------------------------------------------------------------ server

        [Server]
        public void ServerAddMoney(int amount)
        {
            if (amount <= 0) return;
            money += amount;
        }

        [Server]
        public void ServerAddXp(int amount)
        {
            if (amount <= 0) return;
            xp += amount;
            int gainedLevels = 0;
            while (xp >= XpToNextLevel)
            {
                xp -= XpToNextLevel;
                level++;
                skillPoints += skillPointsPerLevel;
                gainedLevels++;
            }
            RpcXpGained(amount, gainedLevels > 0 ? level : 0);
        }

        [Server]
        public bool ServerTryUpgrade(string skillId, out string message)
        {
            var skill = FindSkill(skillId);
            if (skill == null) { message = "Bilinmeyen yetenek."; return false; }
            if (!CanUpgrade(skill, out message)) return false;

            skillPoints -= skill.costPerLevel;
            int newLevel = GetSkillLevel(skill) + 1;
            skillLevels[skill.id] = newLevel;
            RpcSkillUpgraded(skill.displayName, newLevel);
            message = null;
            return true;
        }

        [ClientRpc]
        void RpcXpGained(int amount, int newLevel)
        {
            XpGained?.Invoke(amount);
            if (newLevel > 0) LeveledUp?.Invoke(newLevel);
        }

        [ClientRpc]
        void RpcSkillUpgraded(string skillName, int newLevel) => SkillUpgraded?.Invoke(skillName, newLevel);

        void OnMoneyChanged(int oldValue, int newValue) => MoneyChanged?.Invoke(oldValue, newValue);

#if UNITY_EDITOR
        public void EditorSetSkills(SkillDefinition[] definitions) => skills = definitions;
#endif
    }
}
