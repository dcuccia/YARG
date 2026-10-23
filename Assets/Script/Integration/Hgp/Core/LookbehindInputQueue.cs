using System.Collections.Generic;
using YARG.Core.Input;

namespace YARG.Integration.Hgp
{
    public delegate void InputSink(ref GameInput input);

    /// <summary>
    /// Holds source-timestamped inputs until the engine's (delayed) clock reaches them.
    /// </summary>
    /// <remarks>
    /// YARG.Core's <c>BaseEngine.QueueInput</c> moves any input older than the engine's current time
    /// forward to "now". Network inputs (phones, Wi-Fi bells) are stamped at the source but arrive
    /// 5–40 ms later, so they would be judged by arrival time. Running the engine
    /// <see cref="BudgetSeconds"/> behind the input clock and releasing inputs in time order keeps
    /// their source timestamps intact, at the cost of hit/miss feedback lagging by the budget.
    /// Pure C# (no Unity types) so it can be tested headlessly against YARG.Core.
    /// </remarks>
    public sealed class LookbehindInputQueue
    {
        private readonly List<GameInput> _pending = new();
        private double _lastDrainTime = double.NegativeInfinity;

        public LookbehindInputQueue(double budgetSeconds)
        {
            BudgetSeconds = budgetSeconds;
        }

        public double BudgetSeconds { get; set; }

        public int Count => _pending.Count;

        /// <summary>Inputs that arrived after the engine had already passed their timestamp.</summary>
        public int LateCount { get; private set; }

        public void Enqueue(in GameInput input)
        {
            if (input.Time < _lastDrainTime)
            {
                LateCount++;
            }

            // Stable insertion by time; inputs normally arrive almost sorted
            int index = _pending.Count;
            while (index > 0 && _pending[index - 1].Time > input.Time)
            {
                index--;
            }

            _pending.Insert(index, input);
        }

        /// <summary>Releases every pending input with a time at or before <paramref name="time"/>, in order.</summary>
        public int DrainUpTo(double time, InputSink sink)
        {
            int count = 0;
            while (_pending.Count > 0 && _pending[0].Time <= time)
            {
                var input = _pending[0];
                _pending.RemoveAt(0);
                sink(ref input);
                count++;
            }

            if (time > _lastDrainTime)
            {
                _lastDrainTime = time;
            }

            return count;
        }

        public void Clear()
        {
            _pending.Clear();
            _lastDrainTime = double.NegativeInfinity;
        }
    }
}
