using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Toplanabilir/satılabilir kaynak verisi. Fiyat ve taşıma maliyeti Inspector'dan dengelenir.
    /// </summary>
    [CreateAssetMenu(menuName = "Land Flipper/Resource Definition", fileName = "RD_New")]
    public class ResourceDefinition : ScriptableObject
    {
        public string id = "resource";
        public string displayName = "Kaynak";
        [Min(0)] public int sellPrice = 1;
        [Tooltip("Taşıma yuvası maliyeti (GDD 10: büyük nesneler birden fazla yuva kaplayabilir).")]
        [Min(1)] public int slotCost = 1;
        [Tooltip("Elde üst üste dizilirken kapladığı yükseklik (m).")]
        public float stackHeight = 0.3f;
        [Tooltip("Elde taşınırken yerel dönüş.")]
        public Vector3 carryEuler;
        public Pickup prefab;
    }

    public static class Currency
    {
        public const string Symbol = "$";
        /// <summary>Dolar biçimi: 1250 → "$1,250", -40 → "-$40".</summary>
        public static string Format(int amount) =>
            (amount < 0 ? "-" : "") + Symbol + System.Math.Abs((long)amount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
