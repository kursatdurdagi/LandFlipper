using System.Collections;
using System.Collections.Generic;
using LandFlipper.Gameplay.Effects;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Arazideki satış tezgâhı (GDD 11). Satış kararı host'ta PlayerInteractor.CmdSell ile verilir;
    /// bu bileşen konumu ve görsel geri bildirimi sağlar.
    /// </summary>
    public class SellStation : MonoBehaviour
    {
        static readonly List<SellStation> active = new List<SellStation>();

        [SerializeField] float interactRadius = 4.5f;
        [SerializeField] Transform bounceTarget;
        [Tooltip("Satılan kaynakların uçtuğu nokta (tezgâh üstü).")]
        [SerializeField] Transform intakePoint;

        public Vector3 IntakePosition => intakePoint != null ? intakePoint.position : transform.position + Vector3.up * 1.2f;

        Vector3 bounceRestScale;
        Coroutine bounceRoutine;

        public float InteractRadius => interactRadius;

        void OnEnable()
        {
            active.Add(this);
            if (bounceTarget != null) bounceRestScale = bounceTarget.localScale;
            CareerState.MoneyChanged += OnMoneyChanged;
        }

        void OnDisable()
        {
            active.Remove(this);
            CareerState.MoneyChanged -= OnMoneyChanged;
        }

        public static SellStation FindInRange(Vector3 position)
        {
            foreach (var station in active)
            {
                Vector3 d = station.transform.position - position;
                d.y = 0f;
                if (d.magnitude <= station.interactRadius) return station;
            }
            return null;
        }

        void OnMoneyChanged(int oldValue, int newValue)
        {
            if (newValue <= oldValue) return;
            StylizedFx.Sold(IntakePosition + Vector3.up * 0.25f);
            if (bounceTarget == null) return;
            if (bounceRoutine != null) StopCoroutine(bounceRoutine);
            bounceRoutine = StartCoroutine(Bounce());
        }

        IEnumerator Bounce()
        {
            const float duration = 0.35f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float s = 1f + Mathf.Sin(t / duration * Mathf.PI) * 0.15f;
                bounceTarget.localScale = bounceRestScale * s;
                yield return null;
            }
            bounceTarget.localScale = bounceRestScale;
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform bounce, Transform intake)
        {
            bounceTarget = bounce;
            intakePoint = intake;
        }
#endif
    }
}
