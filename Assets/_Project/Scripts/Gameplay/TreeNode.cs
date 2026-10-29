using System.Collections;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Kesilebilir ağaç. Devrilme yönünü host seçer (çit dışına taşmayacak şekilde);
    /// gövde devrilir, ardından host boyuna göre kütükleri yere bırakır (GDD 9.3).
    /// </summary>
    public class TreeNode : ResourceNode
    {
        [SerializeField] Transform fallPivot;
        [SerializeField] GameObject stump;
        [SerializeField] Collider trunkCollider;
        [SerializeField] float trunkHeight = 3f;
        [SerializeField] float canopyRadius = 1.5f;
        [SerializeField] float logLength = 1.1f;

        public const float FallDuration = 1.3f;

        Quaternion pivotRestRotation;

        public override NodeKind Kind => NodeKind.Tree;
        public override string ActionVerb => "Kes";
        public override string DisplayName => "Ağaç";

        public float TrunkHeight => trunkHeight;
        public float CanopyRadius => canopyRadius;
        /// <summary>Devrilince düşen en uzak nokta (gövde + taç).</summary>
        public float FallReach => trunkHeight + canopyRadius;
        public int LogCount => Mathf.Clamp(Mathf.RoundToInt(trunkHeight / logLength), 1, 5);
        public float LogLength => logLength;

        protected override void Awake()
        {
            base.Awake();
            if (fallPivot != null) pivotRestRotation = fallPivot.rotation;
        }

        public static Vector3 AngleToDirection(float degrees) => Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

        public override void PlayHitFeedback()
        {
            if (!IsDepleted) base.PlayHitFeedback();
        }

        protected override void OnStateApplied(NodeState state, bool animate)
        {
            if (!state.depleted) return;
            if (trunkCollider != null) trunkCollider.enabled = false;

            if (animate && isActiveAndEnabled)
            {
                StartCoroutine(Fall(AngleToDirection(state.fallAngle)));
            }
            else
            {
                // geç katılım: ağaç ve kütük dibi çoktan kaybolmuş
                fallPivot.gameObject.SetActive(false);
                stump.SetActive(false);
            }
        }

        IEnumerator Fall(Vector3 direction)
        {
            stump.SetActive(true);
            Vector3 axis = Vector3.Cross(Vector3.up, direction).normalized;

            for (float t = 0f; t < FallDuration; t += Time.deltaTime)
            {
                float k = t / FallDuration;
                float angle = Mathf.Lerp(0f, 88f, k * k); // yerçekimi gibi hızlanarak
                fallPivot.rotation = Quaternion.AngleAxis(angle, axis) * pivotRestRotation;
                yield return null;
            }

            // küçük sekme
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                float bounce = Mathf.Sin(t / 0.18f * Mathf.PI) * 4f;
                fallPivot.rotation = Quaternion.AngleAxis(88f - bounce, axis) * pivotRestRotation;
                yield return null;
            }

            fallPivot.gameObject.SetActive(false); // kütükler host tarafından spawn edilir

            // kütük dibi birkaç saniye kalır, sonra kaybolur
            yield return ShrinkAndHide(stump.transform, LeftoverLifetime);
        }

#if UNITY_EDITOR
        public void EditorSetup(int id, int hits, Transform pivot, GameObject stumpObject, Collider trunk,
            float height, float canopy)
        {
            EditorSetBase(id, hits, pivot);
            fallPivot = pivot;
            stump = stumpObject;
            trunkCollider = trunk;
            trunkHeight = height;
            canopyRadius = canopy;
        }
#endif
    }
}
