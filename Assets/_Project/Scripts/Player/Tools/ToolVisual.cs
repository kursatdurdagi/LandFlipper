using UnityEngine;

namespace LandFlipper.Player.Tools
{
    /// <summary>
    /// Alet modelinin köküne eklenir. Kök = tutma noktası (+Y aletin başı, +Z vuran yüz).
    /// Makaslarda iki ağız atanırsa kesme animasyonunda açılıp kapanır; atanmazsa sadece alet sallanır.
    /// Gerçek modele geçerken: modeli bu bileşenin altına koy, ağızları (varsa) bağla.
    /// </summary>
    public class ToolVisual : MonoBehaviour
    {
        [Tooltip("Makas ağzı / sapı (opsiyonel). Açılırken +snipAngle döner.")]
        [SerializeField] Transform bladeA;
        [Tooltip("Diğer ağız (opsiyonel). Açılırken -snipAngle döner.")]
        [SerializeField] Transform bladeB;
        [Tooltip("Ağızların döndüğü yerel eksen (vida ekseni).")]
        [SerializeField] Vector3 snipAxis = Vector3.forward;
        [SerializeField] float snipAngle = 18f;

        Quaternion restA, restB;
        bool cached;

        void Cache()
        {
            if (cached) return;
            cached = true;
            if (bladeA != null) restA = bladeA.localRotation;
            if (bladeB != null) restB = bladeB.localRotation;
        }

        /// <summary>0 = kapalı, 1 = tam açık.</summary>
        public void SetOpen(float amount)
        {
            Cache();
            float a = Mathf.Clamp01(amount) * snipAngle;
            if (bladeA != null) bladeA.localRotation = restA * Quaternion.AngleAxis(a, snipAxis);
            if (bladeB != null) bladeB.localRotation = restB * Quaternion.AngleAxis(-a, snipAxis);
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform a, Transform b, Vector3 axis, float angle)
        {
            bladeA = a;
            bladeB = b;
            snipAxis = axis;
            snipAngle = angle;
        }
#endif
    }
}
