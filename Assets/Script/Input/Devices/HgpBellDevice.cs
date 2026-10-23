using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using YARG.Integration.Hgp;

namespace YARG.Input.Devices
{
    public struct HgpBellState : IInputStateTypeInfo
    {
        public static FourCC Format => new('H', 'G', 'P', 'B');
        public FourCC format => Format;

        [InputControl(name = "bell1", layout = "Button", bit = 0, displayName = "Bell 1")]
        [InputControl(name = "bell2", layout = "Button", bit = 1, displayName = "Bell 2")]
        [InputControl(name = "bell3", layout = "Button", bit = 2, displayName = "Bell 3")]
        [InputControl(name = "bell4", layout = "Button", bit = 3, displayName = "Bell 4")]
        [InputControl(name = "bell5", layout = "Button", bit = 4, displayName = "Bell 5")]
        [InputControl(name = "bell6", layout = "Button", bit = 5, displayName = "Bell 6")]
        [InputControl(name = "bell7", layout = "Button", bit = 6, displayName = "Bell 7")]
        [InputControl(name = "bell8", layout = "Button", bit = 7, displayName = "Bell 8")]
        public byte bells;
    }

    /// <summary>
    /// A virtual handbell-ringer device fed by <see cref="HgpBridge"/> (phones over the Handbell
    /// Gesture Protocol, or the keyboard debug feeder). One device per ringer.
    /// </summary>
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    [InputControlLayout(stateType = typeof(HgpBellState), displayName = "HGP Bell")]
    public class HgpBellDevice : InputDevice, IInputLatencyBudget
    {
        public const string INTERFACE_NAME = "HGP";

        public ButtonControl[] Bells { get; private set; }

        /// <summary>Ringer part slot (0..4), see <see cref="HgpRingerSlots"/>.</summary>
        public int RingerSlot { get; set; }

        public double LatencyBudgetSeconds { get; set; } = HgpBridge.DefaultLatencyBudgetSeconds;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
        internal static void Initialize()
        {
            InputSystem.RegisterLayout<HgpBellDevice>(
                matches: new InputDeviceMatcher().WithInterface(INTERFACE_NAME)
            );
        }

        protected override void FinishSetup()
        {
            base.FinishSetup();

            Bells = new ButtonControl[HgpRingerSlots.MAX_BELLS];
            for (int i = 0; i < Bells.Length; i++)
            {
                Bells[i] = GetChildControl<ButtonControl>($"bell{i + 1}");
            }
        }
    }
}
