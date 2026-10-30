using UnityEngine;

namespace LandFlipper.Gameplay.Effects
{
    /// <summary>
    /// Stilize partikül malzemeleri. Setup sırasında üretilir ve Resources altında durur
    /// (malzemeler build'e girsin, efektler sahneden bağımsız yüklensin).
    /// </summary>
    [CreateAssetMenu(menuName = "Land Flipper/Fx Library", fileName = "LF_FxLibrary")]
    public class FxLibrary : ScriptableObject
    {
        public const string ResourcePath = "LF_FxLibrary";

        [Tooltip("Yumuşak toz/duman bulutu (alfa).")] public Material puff;
        [Tooltip("Tahta yongası / taş kırığı / çim kırpıntısı (alfa, keskin kenar).")] public Material chip;
        [Tooltip("Yaprak (alfa).")] public Material leaf;
        [Tooltip("Parıltı yıldızı (additive).")] public Material sparkle;
        [Tooltip("Ortamdaki polen/ışık zerreleri (additive, soluk).")] public Material mote;

        [Header("Ortam")]
        public int ambientMotes = 60;
        public float ambientRadius = 14f;
    }
}
