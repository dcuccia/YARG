using YARG.Core;
using YARG.Core.Input;

namespace YARG.Integration.Hgp
{
    /// <summary>
    /// Ringer part slots. Matches the handbell-hero S5 chart export: one ringer per 5-lane track in
    /// the order PART GUITAR, PART BASS, PART RHYTHM, PART KEYS, PART GUITAR COOP. Every slot is
    /// played in Five-Lane Keys mode (GameMode.ProKeys with a five-lane instrument).
    /// </summary>
    public static class HgpRingerSlots
    {
        public const int MAX_BELLS = 8;

        public static readonly Instrument[] Instruments =
        {
            Instrument.FiveFretGuitar,
            Instrument.FiveFretBass,
            Instrument.FiveFretRhythm,
            Instrument.Keys,
            Instrument.FiveFretCoopGuitar,
        };

        public static readonly ProKeysAction[] BellActions =
        {
            ProKeysAction.GreenKey,
            ProKeysAction.RedKey,
            ProKeysAction.YellowKey,
            ProKeysAction.BlueKey,
            ProKeysAction.OrangeKey,
        };

        public static int Count => Instruments.Length;

        public static Instrument InstrumentForSlot(int slot) =>
            Instruments[((slot % Count) + Count) % Count];
    }

    /// <summary>Implemented by input devices whose inputs arrive late but carry accurate source timestamps.</summary>
    public interface IInputLatencyBudget
    {
        double LatencyBudgetSeconds { get; }
    }
}
