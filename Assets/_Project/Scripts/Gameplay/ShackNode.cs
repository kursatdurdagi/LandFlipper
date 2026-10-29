using System.Collections;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Aşamalı sökülen harabe kulübe (GDD 9.5). Fizik çöküşü yok; parçalar önceden
    /// tanımlı sırayla (çatı → duvarlar → zemin) sökülür ve her parça tahta bırakır.
    /// </summary>
    public class ShackNode : ResourceNode
    {
        [Tooltip("Sökülme sırası: ilk eleman ilk sökülür.")]
        [SerializeField] Transform[] parts;
        [SerializeField] int hitsPerPart = 2;
        [SerializeField] int planksPerPart = 1;

        bool[] removed;

        public override NodeKind Kind => NodeKind.Shack;
        public override string ActionVerb => "Sök";
        public override string DisplayName => "Harabe Kulübe";

        public int PartCount => parts.Length;
        public int PlanksPerPart => planksPerPart;

        public int RemovedPartsFor(int hits) => Mathf.Clamp(hits / Mathf.Max(1, hitsPerPart), 0, parts.Length);
        public Vector3 PartPosition(int index) => parts[index] != null ? parts[index].position : transform.position;

        protected override void Awake()
        {
            base.Awake();
            removed = new bool[parts.Length];
        }

        protected override void OnStateApplied(NodeState state, bool animate)
        {
            int count = state.depleted ? parts.Length : RemovedPartsFor(state.hitsTaken);
            for (int i = 0; i < count; i++)
            {
                if (removed[i] || parts[i] == null) continue;
                removed[i] = true;
                if (animate && isActiveAndEnabled) StartCoroutine(PopOut(parts[i]));
                else parts[i].gameObject.SetActive(false);
            }
        }

        IEnumerator PopOut(Transform part)
        {
            foreach (var c in part.GetComponentsInChildren<Collider>()) c.enabled = false;
            Vector3 startScale = part.localScale;
            Vector3 startPos = part.localPosition;
            const float duration = 0.3f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                part.localScale = startScale * (1f - k);
                part.localPosition = startPos + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.3f);
                yield return null;
            }
            part.gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        public void EditorSetup(int id, Transform[] orderedParts, int hitsEachPart)
        {
            parts = orderedParts;
            hitsPerPart = Mathf.Max(1, hitsEachPart);
            EditorSetBase(id, orderedParts.Length * hitsPerPart, transform);
        }
#endif
    }
}
