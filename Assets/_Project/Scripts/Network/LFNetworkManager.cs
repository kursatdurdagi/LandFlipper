using kcp2k;
using LandFlipper.Core;
using Mirror;
using UnityEngine;

namespace LandFlipper.Network
{
    /// <summary>
    /// Land Flipper oturum yöneticisi. Tek oyunculu mod da host olarak çalışır
    /// (dinlemeyen host), böylece tüm oyun kodu tek bir host-authoritative yol izler.
    /// </summary>
    public class LFNetworkManager : NetworkManager
    {
        public static LFNetworkManager Instance => singleton as LFNetworkManager;

        bool leavingIntentionally;
        bool clientWasConnected;

        public bool IsSessionActive => NetworkServer.active || NetworkClient.active;

        public ushort Port
        {
            get => transport is KcpTransport kcp ? kcp.port : SessionContext.DefaultPort;
            set { if (transport is KcpTransport kcp) kcp.port = value; }
        }

        public void StartSinglePlayer()
        {
            if (IsSessionActive) return;
            PrepareStart(GameMode.SinglePlayer);
            NetworkServer.listen = false;   // dışarıdan bağlantı kabul etme
            maxConnections = 1;
            StartHost();
        }

        public void StartCoopHost(ushort port)
        {
            if (IsSessionActive) return;
            PrepareStart(GameMode.CoopHost);
            NetworkServer.listen = true;
            maxConnections = SessionContext.MaxCoopPlayers;
            Port = port;
            StartHost();
        }

        public void JoinCoop(string address, ushort port)
        {
            if (IsSessionActive) return;
            PrepareStart(GameMode.CoopClient);
            networkAddress = address;
            Port = port;
            StartClient();
        }

        /// <summary>Oyuncunun kendi isteğiyle oturumdan çıkması.</summary>
        public void LeaveSession()
        {
            leavingIntentionally = true;

            if (NetworkServer.active && NetworkClient.isConnected)
                StopHost();
            else if (NetworkClient.active)
                StopClient();
            else if (NetworkServer.active)
                StopServer();
        }

        void PrepareStart(GameMode mode)
        {
            leavingIntentionally = false;
            clientWasConnected = false;
            SessionContext.Mode = mode;
        }

        public override void OnClientConnect()
        {
            clientWasConnected = true;
            base.OnClientConnect();
        }

        public override void OnClientDisconnect()
        {
            if (!leavingIntentionally && SessionContext.Mode == GameMode.CoopClient)
            {
                SessionContext.PostMessage(clientWasConnected
                    ? "Host ile bağlantı kesildi."
                    : $"{networkAddress}:{Port} adresine bağlanılamadı. Adresi ve host'un oturumu açık olduğunu kontrol et.");
            }

            base.OnClientDisconnect();
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);
            Debug.Log($"[LandFlipper] Oyuncu katıldı (conn {conn.connectionId}). Toplam: {NetworkServer.connections.Count}");
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            ResetSession();
        }

        public override void OnStopHost()
        {
            base.OnStopHost();
            ResetSession();
        }

        void ResetSession()
        {
            SessionContext.Mode = GameMode.None;
            NetworkServer.listen = true;
            InputGate.SetBlocked(false);
            Time.timeScale = 1f;
        }
    }
}
