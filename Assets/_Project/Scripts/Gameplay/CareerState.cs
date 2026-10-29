using System;
using Mirror;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Host kariyerinin ortak bütçesi (GDD 6.3). Para sadece host'ta, doğrulanmış
    /// işlemlerle değişir. Kalıcı kayıt Phase 9'da Career/Save sistemine bağlanacak.
    /// </summary>
    public class CareerState : NetworkBehaviour
    {
        public static CareerState Instance { get; private set; }

        /// <summary>(eski, yeni) — istemcilerde ve host'ta tetiklenir.</summary>
        public static event Action<int, int> MoneyChanged;

        [SyncVar(hook = nameof(OnMoneyChanged))] int money;

        public int Money => money;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [Server]
        public void ServerAddMoney(int amount)
        {
            if (amount <= 0) return;
            money += amount;
        }

        void OnMoneyChanged(int oldValue, int newValue) => MoneyChanged?.Invoke(oldValue, newValue);
    }
}
