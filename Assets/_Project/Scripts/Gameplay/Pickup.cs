using Mirror;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Yerden alınabilen kaynak (kütük, taş parçası, çim balyası).
    /// Fizik sadece host'ta çalışır; istemciler konumu yumuşatarak takip eder.
    /// Taşınırken konumunu taşıyan oyuncunun PlayerCarry'si belirler.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Pickup : NetworkBehaviour
    {
        [SerializeField] ResourceDefinition definition;

        [SyncVar(hook = nameof(OnCarrierChanged))] uint carrierNetId;
        [SyncVar] Vector3 syncPosition;
        [SyncVar] Quaternion syncRotation = Quaternion.identity;

        Rigidbody body;
        Collider[] colliders;

        public ResourceDefinition Definition => definition;
        public bool IsCarried => carrierNetId != 0;
        public uint CarrierNetId => carrierNetId;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnStartServer()
        {
            syncPosition = transform.position;
            syncRotation = transform.rotation;
            ApplyPhysicsMode();
        }

        public override void OnStartClient()
        {
            if (!isServer && !IsCarried)
                transform.SetPositionAndRotation(syncPosition, syncRotation);
            ApplyPhysicsMode();
        }

        void OnCarrierChanged(uint _, uint __) => ApplyPhysicsMode();

        void ApplyPhysicsMode()
        {
            bool simulate = isServer && !IsCarried;
            body.isKinematic = !simulate;
            body.interpolation = simulate ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            foreach (var c in colliders) c.enabled = !IsCarried;
        }

        [Server]
        public void ServerSetCarrier(uint netId)
        {
            carrierNetId = netId;
            ApplyPhysicsMode();
        }

        [Server]
        public void ServerLaunch(Vector3 impulse)
        {
            if (impulse != Vector3.zero && !body.isKinematic)
                body.AddForce(impulse, ForceMode.VelocityChange);
        }

        [Server]
        public void ServerDrop(Vector3 position, Quaternion rotation, Vector3 impulse) =>
            ServerDrop(position, rotation, impulse, Vector3.zero, null);

        /// <summary>
        /// Elden fırlatma: nesne bulunduğu yerden verilen hız ve dönüşle atılır.
        /// Atan oyuncunun gövdesiyle kısa bir süre çarpışmaz (içinden doğup itilmesin).
        /// </summary>
        [Server]
        public void ServerDrop(Vector3 position, Quaternion rotation, Vector3 impulse, Vector3 spin, Collider ignoreFor)
        {
            carrierNetId = 0;
            transform.SetPositionAndRotation(position, rotation);
            ApplyPhysicsMode();
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            ServerLaunch(impulse);
            if (spin != Vector3.zero) body.AddTorque(spin, ForceMode.VelocityChange);
            syncPosition = position;
            syncRotation = rotation;

            if (ignoreFor != null)
            {
                foreach (var c in colliders) Physics.IgnoreCollision(c, ignoreFor, true);
                StartCoroutine(RestoreCollision(ignoreFor));
            }
        }

        System.Collections.IEnumerator RestoreCollision(Collider other)
        {
            yield return new WaitForSeconds(0.6f);
            if (other == null) yield break;
            foreach (var c in colliders) Physics.IgnoreCollision(c, other, false);
        }

        void Update()
        {
            // elde küçültülmüş olabilir: bırakılınca yumuşakça normal boyuta dön
            if (!IsCarried && transform.localScale != Vector3.one)
                transform.localScale = Vector3.MoveTowards(transform.localScale, Vector3.one, Time.deltaTime * 4f);

            if (IsCarried) return;

            if (isServer)
            {
                if (transform.position.y < -20f && PlotWorld.Instance != null)
                {
                    // dünyadan düştüyse araziye geri koy
                    Vector3 safe = PlotWorld.Instance.transform.position + Vector3.up * 2f;
                    ServerDrop(safe, transform.rotation, Vector3.zero);
                    return;
                }

                if ((transform.position - syncPosition).sqrMagnitude > 0.0004f ||
                    Quaternion.Angle(transform.rotation, syncRotation) > 1f)
                {
                    syncPosition = transform.position;
                    syncRotation = transform.rotation;
                }
            }
            else
            {
                float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, syncPosition, k),
                    Quaternion.Slerp(transform.rotation, syncRotation, k));
            }
        }

#if UNITY_EDITOR
        public void EditorSetDefinition(ResourceDefinition def) => definition = def;
#endif
    }
}
