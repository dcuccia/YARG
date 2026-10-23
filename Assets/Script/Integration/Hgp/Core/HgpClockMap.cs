using System;

namespace YARG.Integration.Hgp
{
    /// <summary>
    /// Maps the sidecar's clock (seconds) to the local clock, from <c>sync</c> packets sent
    /// immediately by the sidecar. Uses the minimum observed one-way delay over a sliding window,
    /// which on localhost is tens of microseconds; the remaining bias is that minimum delay.
    /// </summary>
    public sealed class HgpClockMap
    {
        private readonly double[] _offsets;
        private int _count;
        private int _next;

        public HgpClockMap(int window = 64)
        {
            _offsets = new double[Math.Max(1, window)];
        }

        public bool IsSynced => _count > 0;

        /// <summary>local − remote, in seconds (minimum over the window).</summary>
        public double Offset { get; private set; }

        public void AddSample(double remoteSendTime, double localReceiveTime)
        {
            _offsets[_next] = localReceiveTime - remoteSendTime;
            _next = (_next + 1) % _offsets.Length;
            if (_count < _offsets.Length)
            {
                _count++;
            }

            double min = double.PositiveInfinity;
            for (int i = 0; i < _count; i++)
            {
                min = Math.Min(min, _offsets[i]);
            }

            Offset = min;
        }

        public double ToLocal(double remoteTime) => remoteTime + Offset;
    }
}
