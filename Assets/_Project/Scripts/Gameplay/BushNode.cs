using System.Collections;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>Kırılabilir çalı. Kırılınca çim balyası bırakır (GDD 9.2: çim ve çalılar).</summary>
    public class BushNode : ResourceNode
    {
        [SerializeField] Collider bushCollider;
        [SerializeField] int baleCount = 1;

        Vector3 visualRestScale;

        public override NodeKind Kind => NodeKind.Bush;
        public override string ActionVerb => "Kır";
        public override string DisplayName => "Çalı";
        public int BaleCount => baleCount;

        protected override void Awake()
        {
            base.Awake();
            if (visualRoot != null) visualRestScale = visualRoot.localScale;
        }

        protected override void OnStateApplied(NodeState state, bool animate)
        {
            if (state.depleted)
            {
                if (bushCollider != null) bushCollider.enabled = false;
                if (animate && isActiveAndEnabled) StartCoroutine(Crumble());
                else visualRoot.gameObject.SetActive(false);
                return;
            }

            float damage = maxHits > 0 ? (float)state.hitsTaken / maxHits : 0f;
            visualRoot.localScale = Vector3.Scale(visualRestScale, new Vector3(1f, Mathf.Lerp(1f, 0.75f, damage), 1f));
        }

        IEnumerator Crumble()
        {
            // yere yatar gibi basılır, sonra kaybolur
            Vector3 start = visualRoot.localScale;
            const float duration = 0.2f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                visualRoot.localScale = new Vector3(start.x * (1f + k * 0.2f), start.y * (1f - k * 0.8f), start.z * (1f + k * 0.2f));
                yield return null;
            }
            yield return ShrinkAndHide(visualRoot, 0f, 0.25f);
        }

#if UNITY_EDITOR
        public void EditorSetup(int id, int hits, Transform visual, Collider collider, int bales)
        {
            EditorSetBase(id, hits, visual);
            bushCollider = collider;
            baleCount = bales;
        }
#endif
    }
}
