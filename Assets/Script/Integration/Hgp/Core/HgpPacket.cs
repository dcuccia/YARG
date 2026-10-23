using System;
using System.Globalization;

namespace YARG.Integration.Hgp
{
    public enum HgpPacketKind
    {
        Invalid,
        Hello,
        Bye,
        Sync,
        Ring,
        Pong,
    }

    /// <summary>
    /// One datagram from the HGP sidecar on localhost. Spike wire format (UTF-8, space separated):
    /// <code>
    /// hgp0 hello &lt;device&gt; &lt;slot&gt; [name]   // slot = ringer part 0..4
    /// hgp0 bye   &lt;device&gt;
    /// hgp0 sync  &lt;sidecarTime&gt;              // sent immediately; used for clock mapping
    /// hgp0 ring  &lt;device&gt; &lt;bell&gt; &lt;1|0&gt; &lt;sidecarTime&gt; &lt;seq&gt;
    /// </code>
    /// Times are seconds on the sidecar's monotonic clock, already mapped from the phone's clock by
    /// the sidecar (S1 / HGP). This is a bridge format, not HGP itself.
    /// <para>YARG → sidecar (replies to the sender of the last datagram):</para>
    /// <code>
    /// hgp0 pong  &lt;sidecarTime&gt;              // echo of each sync: "YARG is listening"
    /// hgp0 song  &lt;specs&gt; &lt;title&gt;            // on chart load; specs = device:part:difficulty[,...] or "-"
    /// </code>
    /// <c>part</c> is the ringer part index (<see cref="HgpRingerSlots"/>) of the instrument the player
    /// picked, or -1; <c>difficulty</c> is the lower-case YARG difficulty name.
    /// </summary>
    public readonly struct HgpPacket
    {
        public const string MAGIC = "hgp0";

        public readonly HgpPacketKind Kind;
        public readonly string Device;
        public readonly int Slot;
        public readonly string Name;
        public readonly int Bell;
        public readonly bool Down;
        public readonly double Time;
        public readonly long Sequence;

        private HgpPacket(HgpPacketKind kind, string device = null, int slot = 0, string name = null,
            int bell = 0, bool down = false, double time = 0, long sequence = 0)
        {
            Kind = kind;
            Device = device;
            Slot = slot;
            Name = name;
            Bell = bell;
            Down = down;
            Time = time;
            Sequence = sequence;
        }

        public static HgpPacket Hello(string device, int slot, string name = null) =>
            new(HgpPacketKind.Hello, device, slot, name);

        public static HgpPacket Bye(string device) => new(HgpPacketKind.Bye, device);

        public static HgpPacket Sync(double time) => new(HgpPacketKind.Sync, time: time);

        public static HgpPacket Ring(string device, int bell, bool down, double time, long sequence) =>
            new(HgpPacketKind.Ring, device, bell: bell, down: down, time: time, sequence: sequence);

        public static HgpPacket Pong(double time) => new(HgpPacketKind.Pong, time: time);

        /// <summary>Formats a YARG → sidecar <c>song</c> report.</summary>
        public static string FormatSong(string title, System.Collections.Generic.IEnumerable<(string device, int part, string difficulty)> players)
        {
            var inv = CultureInfo.InvariantCulture;
            var specs = new System.Collections.Generic.List<string>();
            foreach (var (device, part, difficulty) in players)
            {
                specs.Add($"{device}:{part.ToString(inv)}:{difficulty.ToLowerInvariant()}");
            }

            string safeTitle = string.IsNullOrWhiteSpace(title) ? "?" : title.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return $"{MAGIC} song {(specs.Count > 0 ? string.Join(",", specs) : "-")} {safeTitle}";
        }

        public static HgpPacket Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return default;
            }

            var parts = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != MAGIC)
            {
                return default;
            }

            var inv = CultureInfo.InvariantCulture;
            try
            {
                switch (parts[1])
                {
                    case "hello" when parts.Length >= 4:
                        return Hello(parts[2], int.Parse(parts[3], inv),
                            parts.Length > 4 ? string.Join(" ", parts, 4, parts.Length - 4) : null);
                    case "bye" when parts.Length >= 3:
                        return Bye(parts[2]);
                    case "sync" when parts.Length >= 3:
                        return Sync(double.Parse(parts[2], inv));
                    case "ring" when parts.Length >= 7:
                        return Ring(parts[2], int.Parse(parts[3], inv), parts[4] == "1",
                            double.Parse(parts[5], inv), long.Parse(parts[6], inv));
                    case "pong" when parts.Length >= 3:
                        return Pong(double.Parse(parts[2], inv));
                    default:
                        return default;
                }
            }
            catch (FormatException)
            {
                return default;
            }
            catch (OverflowException)
            {
                return default;
            }
        }

        public override string ToString()
        {
            var inv = CultureInfo.InvariantCulture;
            return Kind switch
            {
                HgpPacketKind.Hello => $"{MAGIC} hello {Device} {Slot.ToString(inv)}" + (Name != null ? " " + Name : ""),
                HgpPacketKind.Bye   => $"{MAGIC} bye {Device}",
                HgpPacketKind.Sync  => $"{MAGIC} sync {Time.ToString("R", inv)}",
                HgpPacketKind.Pong  => $"{MAGIC} pong {Time.ToString("R", inv)}",
                HgpPacketKind.Ring  => $"{MAGIC} ring {Device} {Bell.ToString(inv)} {(Down ? 1 : 0)} " +
                    $"{Time.ToString("R", inv)} {Sequence.ToString(inv)}",
                _ => "",
            };
        }
    }
}
