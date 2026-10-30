using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Biçilebilir çiçek kümesi. Çim öbeği gibi davranır (makasla basılı tutarak kesilir, çim balyası
    /// sayacına katkı verir); yalnızca adı ve kesilirken saçılan taç yaprağı rengi farklıdır.
    /// </summary>
    public class FlowerNode : GrassNode
    {
        [SerializeField] Color petalColor = new Color(0.98f, 0.6f, 0.72f);

        public override string DisplayName => "Çiçek";
        public Color PetalColor => petalColor;

#if UNITY_EDITOR
        public void EditorSetup(int id, Transform visual, Collider target, Color petal)
        {
            EditorSetup(id, visual, target);
            petalColor = petal;
        }
#endif
    }
}
