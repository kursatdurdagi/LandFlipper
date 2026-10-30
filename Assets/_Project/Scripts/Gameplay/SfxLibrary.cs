using System.Collections.Generic;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    public enum SfxKind : byte
    {
        None,
        Wood,   // balta: ağaç, kulübe
        Rock,   // çekiç: taş
        Snip    // makas
    }

    /// <summary>
    /// Kısa, darbe anından başlayan tek vuruşluk sesler (SFx/Cuts). Uzun kayıtlar setup sırasında değil,
    /// önceden tek tek vuruşlara bölündü; böylece ses tam alet değdiği karede başlar, kuyruk taşmaz.
    /// Aynı varyant art arda çalınmaz, perde hafifçe oynatılır.
    /// </summary>
    [CreateAssetMenu(menuName = "Land Flipper/Sfx Library", fileName = "SfxLibrary")]
    public class SfxLibrary : ScriptableObject
    {
        public AudioClip[] wood;
        public AudioClip[] rock;
        public AudioClip[] snip;
        [Range(0f, 1f)] public float woodVolume = 0.8f;
        [Range(0f, 1f)] public float rockVolume = 0.75f;
        [Range(0f, 1f)] public float snipVolume = 0.45f;
        [Tooltip("Rastgele perde aralığı (±).")]
        [Range(0f, 0.2f)] public float pitchJitter = 0.06f;

        const int PoolSize = 12;
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static int next;
        static Transform poolRoot;
        readonly Dictionary<SfxKind, int> lastIndex = new Dictionary<SfxKind, int>();

        /// <summary>Sesi dünyada bir noktada çalar. spatial: 0 = 2B (yerel oyuncu), 1 = tam 3B.</summary>
        public void Play(SfxKind kind, Vector3 position, float spatial = 1f, float volumeScale = 1f)
        {
            AudioClip[] clips;
            float volume;
            switch (kind)
            {
                case SfxKind.Wood: clips = wood; volume = woodVolume; break;
                case SfxKind.Rock: clips = rock; volume = rockVolume; break;
                case SfxKind.Snip: clips = snip; volume = snipVolume; break;
                default: return;
            }
            if (clips == null || clips.Length == 0) return;

            int index = Random.Range(0, clips.Length);
            if (clips.Length > 1 && lastIndex.TryGetValue(kind, out int last) && index == last)
                index = (index + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
            lastIndex[kind] = index;
            var clip = clips[index];
            if (clip == null) return;

            var source = NextSource();
            source.transform.position = position;
            source.spatialBlend = spatial;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.volume = volume * volumeScale;
            source.clip = clip;
            source.Play();
        }

        static AudioSource NextSource()
        {
            if (poolRoot == null)
            {
                pool.Clear();
                var root = new GameObject("[Sfx Pool]");
                DontDestroyOnLoad(root);
                poolRoot = root.transform;
                for (int i = 0; i < PoolSize; i++)
                {
                    var go = new GameObject("Sfx");
                    go.transform.SetParent(poolRoot, false);
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.rolloffMode = AudioRolloffMode.Linear;
                    s.minDistance = 2f;
                    s.maxDistance = 28f;
                    s.dopplerLevel = 0f;
                    pool.Add(s);
                }
            }
            next = (next + 1) % pool.Count;
            var source = pool[next];
            source.Stop();
            return source;
        }

        public static SfxKind ForNode(NodeKind node) => node switch
        {
            NodeKind.Tree => SfxKind.Wood,
            NodeKind.Shack => SfxKind.Wood,
            NodeKind.Rock => SfxKind.Rock,
            _ => SfxKind.Snip
        };
    }
}
