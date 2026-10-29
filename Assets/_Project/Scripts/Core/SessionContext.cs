using System;

namespace LandFlipper.Core
{
    public enum GameMode
    {
        None,
        SinglePlayer,
        CoopHost,
        CoopClient
    }

    /// <summary>
    /// Aktif oturumun modu ve sahneler arası taşınması gereken kısa mesajlar.
    /// Kalıcı kariyer verisi burada tutulmaz (Career/Save sistemi ayrı olacak).
    /// </summary>
    public static class SessionContext
    {
        public const ushort DefaultPort = 7777;
        public const int MaxCoopPlayers = 4;

        public static GameMode Mode { get; set; } = GameMode.None;

        public static bool IsCoop => Mode == GameMode.CoopHost || Mode == GameMode.CoopClient;

        /// <summary>Ana menüye dönüldüğünde gösterilecek bilgi/hata mesajı.</summary>
        public static string PendingMessage { get; private set; }

        public static event Action<string> MessagePosted;

        public static void PostMessage(string message)
        {
            PendingMessage = message;
            MessagePosted?.Invoke(message);
        }

        public static string ConsumeMessage()
        {
            string message = PendingMessage;
            PendingMessage = null;
            return message;
        }
    }
}
