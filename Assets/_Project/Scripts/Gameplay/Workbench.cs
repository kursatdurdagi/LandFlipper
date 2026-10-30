using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Alet Tezgâhı: oyuncu E ile yükseltme ekranını açar. Satın alma host'ta
    /// PlayerTools.CmdUpgrade ile doğrulanır (mesafe + bütçe + seviye).
    /// </summary>
    public class Workbench : MonoBehaviour
    {
        static readonly List<Workbench> active = new List<Workbench>();

        [SerializeField] float interactRadius = 4.5f;
        [Tooltip("Yükseltme olunca zıplayan parça (örs).")]
        [SerializeField] Transform bounceTarget;

        Vector3 bounceRestScale;
        Coroutine bounceRoutine;

        void OnEnable()
        {
            active.Add(this);
            if (bounceTarget != null) bounceRestScale = bounceTarget.localScale;
            CareerState.ToolUpgraded += OnToolUpgraded;
        }

        void OnDisable()
        {
            active.Remove(this);
            CareerState.ToolUpgraded -= OnToolUpgraded;
        }

        public static Workbench FindInRange(Vector3 position)
        {
            foreach (var bench in active)
            {
                Vector3 d = bench.transform.position - position;
                d.y = 0f;
                if (d.magnitude <= bench.interactRadius) return bench;
            }
            return null;
        }

        void OnToolUpgraded(ToolKind kind, ToolDefinition tool)
        {
            if (bounceTarget == null || !isActiveAndEnabled) return;
            if (bounceRoutine != null) StopCoroutine(bounceRoutine);
            bounceRoutine = StartCoroutine(Bounce());
        }

        IEnumerator Bounce()
        {
            const float duration = 0.4f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float s = 1f + Mathf.Sin(t / duration * Mathf.PI) * 0.18f;
                bounceTarget.localScale = bounceRestScale * s;
                yield return null;
            }
            bounceTarget.localScale = bounceRestScale;
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform bounce) => bounceTarget = bounce;
#endif
    }
}
