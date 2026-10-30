using UnityEngine;

namespace LandFlipper.Gameplay.Effects
{
    /// <summary>
    /// Ortamda süzülen polen / ışık zerreleri. Tek, düşük sayılı bir partikül sistemi aktif kamerayı izler;
    /// parçacıklar dünya uzayında kalır (kamera dönünce kaymazlar). Güneşte hafifçe parlar.
    /// </summary>
    public class AmbientFx : MonoBehaviour
    {
        [SerializeField] int maxMotes = 70;
        [SerializeField] float radius = 13f;
        [SerializeField] float height = 4f;
        [SerializeField] Color color = new Color(1f, 0.95f, 0.78f, 0.55f);

        ParticleSystem system;

        void Start()
        {
            var library = Resources.Load<FxLibrary>(FxLibrary.ResourcePath);
            if (library == null || library.mote == null)
            {
                enabled = false;
                return;
            }

            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxMotes;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = color;
            main.gravityModifier = -0.002f;
            main.prewarm = true;

            var emission = system.emission;
            emission.rateOverTime = maxMotes / 8f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, height, radius * 2f);
            shape.position = new Vector3(0f, height * 0.5f - 0.3f, 0f);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.15f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var col = system.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = library.mote;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;

            system.Play();
        }

        void LateUpdate()
        {
            if (system == null) return;
            var cam = Camera.main;
            if (cam != null) system.transform.position = cam.transform.position - Vector3.up * 1.2f;
        }
    }
}
