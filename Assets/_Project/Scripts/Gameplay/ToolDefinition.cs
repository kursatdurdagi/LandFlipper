using UnityEngine;

namespace LandFlipper.Gameplay
{
    public enum ToolKind
    {
        Shears = 0,  // çim ve çalı
        Axe = 1,     // ağaç
        Hammer = 2   // taş ve kulübe
    }

    /// <summary>
    /// Bir aletin tek kademesi (ör. Çocuk Makası → Terzi Makası → Bahçe Makası).
    /// Kademe 0 başlangıçta açıktır; sonrakiler Alet Tezgâhı'nda para ile alınır.
    /// Görsel prefab'ın kökü tutma noktasıdır: +Y aletin ucu/başı, +Z vuran yüz (ağız) yönü.
    /// </summary>
    [CreateAssetMenu(menuName = "Land Flipper/Tool Definition", fileName = "TL_NewTool")]
    public class ToolDefinition : ScriptableObject
    {
        public string id;
        public ToolKind kind;
        [Tooltip("0 = başlangıç aleti.")]
        public int tier;
        public string displayName;
        [TextArea] public string description;
        public string iconText = "?";

        [Header("Satın alma")]
        public int price;
        public int requiredLevel = 1;

        [Header("Güç")]
        [Tooltip("Makas: biçme hızı çarpanı (1 = temel). Balta/Çekiç: vuruş başına hasar.")]
        public float power = 1f;

        [Header("Görsel")]
        [Tooltip("Kök = tutma noktası. +Y aletin başı, +Z vuran yüz.")]
        public GameObject visualPrefab;
        [Tooltip("Model değiştirince elde oturtmak için ince ayar (tutma noktasına göre).")]
        public Vector3 gripPositionOffset;
        public Vector3 gripEulerOffset;

        /// <summary>Vuruş başına hasar (en az 1).</summary>
        public int Damage => Mathf.Max(1, Mathf.RoundToInt(power));

        /// <summary>Makas için biçme hızı çarpanı.</summary>
        public float SpeedMultiplier => Mathf.Max(0.1f, power);

        public string PowerText => kind == ToolKind.Shears
            ? $"Biçme hızı ×{power:0.0#}"
            : $"Vuruş gücü {Damage}";

        public static ToolKind KindFor(NodeKind node) => node switch
        {
            NodeKind.Grass => ToolKind.Shears,
            NodeKind.Bush => ToolKind.Shears,
            NodeKind.Tree => ToolKind.Axe,
            _ => ToolKind.Hammer
        };

        public static string KindName(ToolKind kind) => kind switch
        {
            ToolKind.Shears => "Makas",
            ToolKind.Axe => "Balta",
            _ => "Çekiç"
        };

        public static string KindUse(ToolKind kind) => kind switch
        {
            ToolKind.Shears => "Çim ve çalı",
            ToolKind.Axe => "Ağaç",
            _ => "Taş ve kulübe"
        };
    }
}
