using System.Collections;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Kırılabilir taş. Her darbede biraz ufalır; kırılınca sınırlı sayıda taş parçası bırakır (GDD 9.4).
    /// </summary>
    public class RockNode : ResourceNode
    {
        [SerializeField] Collider rockCollider;
        [SerializeField] int chunkCount = 4;
        [SerializeField] float radius = 0.8f;

        Vector3 visualRestScale;

        public override NodeKind Kind => NodeKind.Rock;
        public override string ActionVerb => "Kır";
        public override string DisplayName => "Taş";
        public int ChunkCount => chunkCount;
        public float Radius => radius;

        protected override void Awake()
        {
            base.Awake();
            if (visualRoot != null) visualRestScale = visualRoot.localScale;
        }

        protected override void OnStateApplied(NodeState state, bool animate)
        {
            if (state.depleted)
            {
                if (rockCollider != null) rockCollider.enabled = false;
                if (animate && isActiveAndEnabled) StartCoroutine(Crumble());
                else visualRoot.gameObject.SetActive(false);
                return;
            }

            // Çatlama aşamaları: darbe aldıkça hafifçe küçülür
            float damage = maxHits > 0 ? (float)state.hitsTaken / maxHits : 0f;
            visualRoot.localScale = visualRestScale * Mathf.Lerp(1f, 0.82f, damage);
        }

        IEnumerator Crumble()
        {
            Vector3 start = visualRoot.localScale;
            const float duration = 0.18f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                visualRoot.localScale = start * (1f - t / duration);
                yield return null;
            }
            visualRoot.gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        public void EditorSetup(int id, int hits, Transform visual, Collider collider, int chunks, float rockRadius)
        {
            EditorSetBase(id, hits, visual);
            rockCollider = collider;
            chunkCount = chunks;
            radius = rockRadius;
        }
#endif
    }
}
