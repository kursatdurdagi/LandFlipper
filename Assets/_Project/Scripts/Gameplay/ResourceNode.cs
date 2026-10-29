using System.Collections;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    public enum NodeKind
    {
        Grass,
        Tree,
        Rock,
        Bush,
        Shack
    }

    /// <summary>
    /// Ağ üzerinden taşınan düğüm durumu. Sadece değişmiş düğümler PlotWorld'de tutulur;
    /// değişmemiş düğümler seed'den gelen başlangıç hâlindedir (GDD 8.4).
    /// </summary>
    public struct NodeState
    {
        public byte hitsTaken;
        public bool depleted;
        public short fallAngle;
    }

    /// <summary>
    /// Arazideki temizlenebilir nesne. Ağ nesnesi değildir; deterministik ObjectID ile
    /// PlotWorld'e kayıtlıdır. Durum değişiklikleri sadece host'ta yapılır.
    /// </summary>
    public abstract class ResourceNode : MonoBehaviour
    {
        [SerializeField] protected int objectId;
        [SerializeField] protected int maxHits = 1;
        [SerializeField] protected Transform visualRoot;

        Vector3 visualRestPosition;
        Coroutine shakeRoutine;

        public int ObjectId => objectId;
        public int MaxHits => maxHits;
        public bool IsDepleted { get; private set; }
        public int HitsTaken { get; private set; }

        public abstract NodeKind Kind { get; }
        public abstract string ActionVerb { get; }
        public abstract string DisplayName { get; }

        protected virtual void Awake()
        {
            if (visualRoot != null) visualRestPosition = visualRoot.localPosition;
        }

        public void ApplyState(NodeState state, bool animate)
        {
            bool wasDepleted = IsDepleted;
            IsDepleted = state.depleted;
            HitsTaken = state.hitsTaken;
            OnStateApplied(state, animate && !wasDepleted);
        }

        protected abstract void OnStateApplied(NodeState state, bool animate);

        public virtual void PlayHitFeedback()
        {
            if (visualRoot == null || !isActiveAndEnabled) return;
            if (shakeRoutine != null) StopCoroutine(shakeRoutine);
            shakeRoutine = StartCoroutine(Shake());
        }

        /// <summary>Kalıntının (anız, kütük dibi) bir süre sonra küçülerek kaybolması.</summary>
        public const float LeftoverLifetime = 5.5f;

        protected IEnumerator ShrinkAndHide(Transform target, float delay, float duration = 0.4f)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (target == null) yield break;

            Vector3 start = target.localScale;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                target.localScale = start * (1f - t / duration);
                yield return null;
            }
            target.gameObject.SetActive(false);
        }

        IEnumerator Shake()
        {
            const float duration = 0.22f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = 1f - t / duration;
                visualRoot.localPosition = visualRestPosition + Random.insideUnitSphere * (0.05f * k);
                yield return null;
            }
            visualRoot.localPosition = visualRestPosition;
            shakeRoutine = null;
        }

#if UNITY_EDITOR
        public void EditorSetBase(int id, int hits, Transform visual)
        {
            objectId = id;
            maxHits = Mathf.Max(1, hits);
            visualRoot = visual;
        }
#endif
    }
}
