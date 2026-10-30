using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LandFlipper.Core
{
    /// <summary>
    /// Oyunun tüm eylemleri tek bir yerde. Klavye/fare atamaları oyuncu tarafından değiştirilebilir
    /// (Ayarlar → Tuşlar) ve PlayerPrefs'e kaydedilir. Gamepad atamaları sabittir.
    /// Duraklat (Esc) değiştirilemez; oyuncu menüye her zaman ulaşabilsin.
    /// </summary>
    public static class LFInput
    {
        public const string KeyboardGroup = "KeyboardMouse";
        public const string GamepadGroup = "Gamepad";
        const string PrefsKey = "LF_BindingOverrides";

        static InputActionAsset asset;
        static InputActionMap map;

        public static InputAction Move { get; private set; }
        public static InputAction Look { get; private set; }
        public static InputAction Jump { get; private set; }
        public static InputAction Sprint { get; private set; }
        public static InputAction Primary { get; private set; }
        public static InputAction Interact { get; private set; }
        public static InputAction Drop { get; private set; }
        public static InputAction ToggleView { get; private set; }
        public static InputAction SkillTree { get; private set; }
        public static InputAction Pause { get; private set; }

        public struct RebindEntry
        {
            public string Label;
            public InputAction Action;
            public int BindingIndex;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Domain reload kapalıyken Play'ler arasında eski asset kalmasın
            if (asset != null) Object.Destroy(asset);
            asset = null;
            map = null;
        }

        public static void EnsureCreated()
        {
            if (asset != null) return;

            asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "LandFlipperInput";
            asset.AddControlScheme(KeyboardGroup).WithRequiredDevice("<Keyboard>").WithOptionalDevice("<Mouse>");
            asset.AddControlScheme(GamepadGroup).WithRequiredDevice("<Gamepad>");
            map = asset.AddActionMap("Gameplay");

            Move = map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", KeyboardGroup)
                .With("Down", "<Keyboard>/s", KeyboardGroup)
                .With("Left", "<Keyboard>/a", KeyboardGroup)
                .With("Right", "<Keyboard>/d", KeyboardGroup);
            Move.AddBinding("<Gamepad>/leftStick").WithGroup(GamepadGroup);

            Look = map.AddAction("Look", InputActionType.Value);
            Look.AddBinding("<Pointer>/delta").WithGroup(KeyboardGroup);
            Look.AddBinding("<Gamepad>/rightStick").WithGroup(GamepadGroup);

            Jump = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Primary = Button("Primary", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            Interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonWest");
            Drop = Button("Drop", "<Keyboard>/f", "<Gamepad>/buttonEast");
            ToggleView = Button("ToggleView", "<Keyboard>/v", "<Gamepad>/select");
            SkillTree = Button("SkillTree", "<Keyboard>/tab", "<Gamepad>/dpad/up");
            Pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");

            LoadOverrides();
            asset.Enable();
        }

        static InputAction Button(string name, string keyboard, string gamepad)
        {
            var action = map.AddAction(name, InputActionType.Button);
            action.AddBinding(keyboard).WithGroup(KeyboardGroup);
            action.AddBinding(gamepad).WithGroup(GamepadGroup);
            return action;
        }

        // ------------------------------------------------------------ atama kaydı

        // Eylemler kodla kurulduğu için binding id'leri her açılışta yeniden üretilir;
        // bu yüzden atamalar id ile değil "eylem adı + binding sırası" ile saklanır.
        [System.Serializable]
        class SavedOverride
        {
            public string action;
            public int index;
            public string path;
        }

        [System.Serializable]
        class SavedOverrides
        {
            public List<SavedOverride> items = new List<SavedOverride>();
        }

        static void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var saved = JsonUtility.FromJson<SavedOverrides>(json);
                if (saved?.items == null) return;
                foreach (var item in saved.items)
                {
                    var action = map.FindAction(item.action);
                    if (action == null || item.index < 0 || item.index >= action.bindings.Count || string.IsNullOrEmpty(item.path))
                        continue;
                    action.ApplyBindingOverride(item.index, item.path);
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[LandFlipper] Tuş atamaları okunamadı, varsayılanlar kullanılıyor: {e.Message}"); }
        }

        public static void SaveOverrides()
        {
            EnsureCreated();
            var saved = new SavedOverrides();
            foreach (var action in map.actions)
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var b = action.bindings[i];
                    if (!string.IsNullOrEmpty(b.overridePath))
                        saved.items.Add(new SavedOverride { action = action.name, index = i, path = b.overridePath });
                }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(saved));
            PlayerPrefs.Save();
        }

        public static void ResetAllBindings()
        {
            EnsureCreated();
            asset.RemoveAllBindingOverrides();
            SaveOverrides();
        }

        // ------------------------------------------------------------ ayarlar ekranı için

        /// <summary>Ayarlar ekranında gösterilen, değiştirilebilir klavye/fare atamaları.</summary>
        public static List<RebindEntry> GetRebindableBindings()
        {
            EnsureCreated();
            var list = new List<RebindEntry>();

            // Hareket: 2D composite parçaları tek tek
            var moveLabels = new Dictionary<string, string> { { "up", "İleri" }, { "down", "Geri" }, { "left", "Sola" }, { "right", "Sağa" } };
            for (int i = 0; i < Move.bindings.Count; i++)
            {
                var b = Move.bindings[i];
                if (b.isPartOfComposite && b.groups.Contains(KeyboardGroup) && moveLabels.TryGetValue(b.name.ToLowerInvariant(), out var label))
                    list.Add(new RebindEntry { Label = label, Action = Move, BindingIndex = i });
            }

            Add(list, Jump, "Zıpla");
            Add(list, Sprint, "Koş");
            Add(list, Primary, "Kes / Kır / Biç");
            Add(list, Interact, "Al / Sat");
            Add(list, Drop, "At / Bırak");
            Add(list, SkillTree, "Yetenek Ağacı");
            Add(list, ToggleView, "Görünüm Değiştir");
            return list;
        }

        static void Add(List<RebindEntry> list, InputAction action, string label)
        {
            for (int i = 0; i < action.bindings.Count; i++)
                if (!action.bindings[i].isComposite && action.bindings[i].groups.Contains(KeyboardGroup))
                {
                    list.Add(new RebindEntry { Label = label, Action = action, BindingIndex = i });
                    return;
                }
        }

        public static string DisplayName(InputAction action, int bindingIndex)
        {
            string text = action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            return Localize(text);
        }

        /// <summary>Eylemin klavye/fare tuşunun kısa adı (HUD ipuçları için).</summary>
        public static string KeyLabel(InputAction action)
        {
            if (action == null) return "?";
            for (int i = 0; i < action.bindings.Count; i++)
                if (!action.bindings[i].isComposite && action.bindings[i].groups.Contains(KeyboardGroup))
                    return DisplayName(action, i);
            return "?";
        }

        static string Localize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "—";
            switch (text)
            {
                case "LMB": return "Sol Tık";
                case "RMB": return "Sağ Tık";
                case "MMB": return "Orta Tık";
                case "Space": return "Boşluk";
                case "Left Shift": return "Sol Shift";
                case "Right Shift": return "Sağ Shift";
                case "Left Ctrl": return "Sol Ctrl";
                case "Right Ctrl": return "Sağ Ctrl";
                case "Left Alt": return "Sol Alt";
                case "Escape": return "Esc";
                case "Enter": return "Enter";
                case "Tab": return "Tab";
                default: return text.Length == 1 ? text.ToUpperInvariant() : text;
            }
        }

        /// <summary>Aynı klavye/fare tuşu başka bir eyleme de atanmış mı?</summary>
        public static bool HasConflict(InputAction action, int bindingIndex)
        {
            string path = action.bindings[bindingIndex].effectivePath;
            if (string.IsNullOrEmpty(path)) return false;
            foreach (var entry in GetRebindableBindings())
            {
                if (entry.Action == action && entry.BindingIndex == bindingIndex) continue;
                if (entry.Action.bindings[entry.BindingIndex].effectivePath == path) return true;
            }
            return false;
        }
    }
}
