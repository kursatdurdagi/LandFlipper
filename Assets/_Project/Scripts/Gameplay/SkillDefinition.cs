using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>Yeteneklerin etkilediği oyun değerleri.</summary>
    public enum SkillStat
    {
        MoveSpeed,      // yürüme + koşma hızı çarpanı
        SprintSpeed,    // sadece koşma hızı çarpanı
        CarryCapacity,  // ek taşıma yuvası
        HitSpeed,       // vuruş hızı çarpanı (bekleme süresi kısalır)
        SellBonus       // satış fiyatı çarpanı
    }

    /// <summary>
    /// Skill tree düğümü (GDD 13). Değerler veri odaklıdır; Inspector'dan dengelenir.
    /// </summary>
    [CreateAssetMenu(menuName = "Land Flipper/Skill Definition", fileName = "SK_New")]
    public class SkillDefinition : ScriptableObject
    {
        public string id = "skill";
        public string displayName = "Yetenek";
        [TextArea] public string description;

        [Header("Ağaç")]
        public string branchId = "carry";
        public string branchName = "Taşıma";
        public int branchOrder;
        [Tooltip("Kart üzerindeki kısa simge metni.")]
        public string iconText = "K";

        [Header("Etki")]
        public SkillStat stat;
        public float valuePerLevel = 0.1f;
        [Min(1)] public int maxLevel = 3;
        [Min(1)] public int costPerLevel = 1;

        [Header("Önkoşul")]
        public SkillDefinition prerequisite;
        [Min(1)] public int prerequisiteLevel = 1;

        public float ValueAt(int level) => valuePerLevel * level;

        public string FormatEffect(int level)
        {
            float v = ValueAt(level);
            return stat switch
            {
                SkillStat.CarryCapacity => $"+{v:0} yuva",
                SkillStat.MoveSpeed => $"+%{v * 100f:0} hız",
                SkillStat.SprintSpeed => $"+%{v * 100f:0} koşma",
                SkillStat.HitSpeed => $"+%{v * 100f:0} vuruş hızı",
                SkillStat.SellBonus => $"+%{v * 100f:0} satış fiyatı",
                _ => v.ToString("0.##")
            };
        }
    }
}
