using System.Collections;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>Biçilebilir çim öbeği. Biçilince kısa anız olarak kalır (GDD 9.2).</summary>
    public class GrassNode : ResourceNode
    {
        [SerializeField] Collider targetCollider;
        [SerializeField] float cutHeight = 0.18f;

        public override NodeKind Kind => NodeKind.Grass;
        public override string ActionVerb => "Biç";
        public override string DisplayName => "Çimen";

        protected override void OnStateApplied(NodeState state, bool animate)
        {
            if (!state.depleted) return;
            if (targetCollider != null) targetCollider.enabled = false;
            if (animate && isActiveAndEnabled) StartCoroutine(CutAnimation());
            else visualRoot.gameObject.SetActive(false); // geç katılım: anız çoktan kaybolmuş
        }

        IEnumerator CutAnimation()
        {
            const float duration = 0.15f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                SetHeight(Mathf.Lerp(1f, cutHeight, t / duration));
                yield return null;
            }
            SetHeight(cutHeight);

            // anız birkaç saniye görünür, sonra kaybolur
            yield return ShrinkAndHide(visualRoot, LeftoverLifetime);
        }

        void SetHeight(float y)
        {
            var s = visualRoot.localScale;
            visualRoot.localScale = new Vector3(s.x, y, s.z);
        }

#if UNITY_EDITOR
        public void EditorSetup(int id, Transform visual, Collider target)
        {
            EditorSetBase(id, 1, visual);
            targetCollider = target;
        }
#endif
    }
}
