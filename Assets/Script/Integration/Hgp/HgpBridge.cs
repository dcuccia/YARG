using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using YARG.Core.Logging;
using YARG.Input.Devices;

namespace YARG.Integration.Hgp
{
    /// <summary>
    /// Spike bridge from the handbell-hero HGP sidecar (localhost UDP) to virtual
    /// <see cref="HgpBellDevice"/>s. Events are queued with their mapped <b>source</b> timestamps.
    /// Optional keyboard feeder (QWERT / ASDFG / ZXCVB / YUIOP / HJKL;) with simulated network delay
    /// for testing without phones.
    /// </summary>
    /// <remarks>
    /// Enable with <c>-hgp</c> or env <c>YARG_HGP=1</c>. Options (arg / env):
    /// <c>-hgp-port N</c> / <c>YARG_HGP_PORT</c> (default 47800),
    /// <c>-hgp-budget-ms N</c> / <c>YARG_HGP_BUDGET_MS</c> (default 40; 0 = stock YARG behavior),
    /// <c>-hgp-keyboard</c> / <c>YARG_HGP_KEYBOARD=1</c>,
    /// <c>-hgp-kbd-delay-ms N</c> / <c>YARG_HGP_KBD_DELAY_MS</c>,
    /// <c>-hgp-kbd-jitter-ms N</c> / <c>YARG_HGP_KBD_JITTER_MS</c>.
    /// </remarks>
    public class HgpBridge : MonoBehaviour
    {
        public const int DEFAULT_PORT = 47800;

        public static double DefaultLatencyBudgetSeconds { get; private set; } = 0.040;

        public static HgpBridge Instance { get; private set; }

        private static readonly Key[][] KEYBOARD_RINGERS =
        {
            new[] { Key.Q, Key.W, Key.E, Key.R, Key.T },
            new[] { Key.A, Key.S, Key.D, Key.F, Key.G },
            new[] { Key.Z, Key.X, Key.C, Key.V, Key.B },
            new[] { Key.Y, Key.U, Key.I, Key.O, Key.P },
            new[] { Key.H, Key.J, Key.K, Key.L, Key.Semicolon },
        };

        private struct Received
        {
            public string Text;
            public long Ticks;
        }

        private struct DelayedRing
        {
            public HgpBellDevice Device;
            public int Bell;
            public bool Down;
            public double Time;
            public double ReleaseAt;
        }

        private readonly ConcurrentQueue<Received> _received = new();
        private readonly HgpClockMap _clock = new();
        private readonly Dictionary<string, HgpBellDevice> _devices = new();
        private readonly Dictionary<HgpBellDevice, byte> _bellState = new();
        private readonly Dictionary<HgpBellDevice, double> _lastEventTime = new();
        private readonly Dictionary<string, long> _lastSequence = new();
        private readonly List<DelayedRing> _delayed = new();
        private readonly HgpBellDevice[] _keyboardDevices = new HgpBellDevice[KEYBOARD_RINGERS.Length];
        private readonly bool[,] _keyboardPressed = new bool[KEYBOARD_RINGERS.Length, 5];

        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private System.Random _rng = new(1);

        private bool _keyboardFeeder;
        private double _kbdDelay;
        private double _kbdJitter;

        // Stats
        private int _packets, _rings, _droppedUnsynced, _droppedDuplicate, _clampedMonotonic, _clampedFuture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!HasFlag("-hgp", "YARG_HGP") && !HasFlag("-hgp-keyboard", "YARG_HGP_KEYBOARD"))
            {
                return;
            }

            var go = new GameObject("HgpBridge");
            DontDestroyOnLoad(go);
            go.AddComponent<HgpBridge>();
        }

        private void Awake()
        {
            Instance = this;

            int port = (int) GetNumber("-hgp-port", "YARG_HGP_PORT", DEFAULT_PORT);
            DefaultLatencyBudgetSeconds = GetNumber("-hgp-budget-ms", "YARG_HGP_BUDGET_MS", 40) / 1000.0;
            _keyboardFeeder = HasFlag("-hgp-keyboard", "YARG_HGP_KEYBOARD");
            _kbdDelay = GetNumber("-hgp-kbd-delay-ms", "YARG_HGP_KBD_DELAY_MS", 0) / 1000.0;
            _kbdJitter = GetNumber("-hgp-kbd-jitter-ms", "YARG_HGP_KBD_JITTER_MS", 0) / 1000.0;

            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "HGP bridge" };
                _thread.Start();
                YargLogger.LogFormatInfo("HGP bridge listening on 127.0.0.1:{0}, latency budget {1} ms",
                    port, DefaultLatencyBudgetSeconds * 1000);
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "HGP bridge failed to open UDP port");
            }

            if (_keyboardFeeder)
            {
                for (int i = 0; i < _keyboardDevices.Length; i++)
                {
                    _keyboardDevices[i] = AddBellDevice($"kbd{i + 1}", i, $"Keyboard ringer {i + 1}");
                }

                InputSystem.onEvent += OnKeyboardEvent;
                YargLogger.LogFormatInfo("HGP keyboard feeder on: delay {0} ms, jitter {1} ms",
                    _kbdDelay * 1000, _kbdJitter * 1000);
            }
        }

        private void OnDestroy()
        {
            _running = false;
            _udp?.Close();
            if (_keyboardFeeder)
            {
                InputSystem.onEvent -= OnKeyboardEvent;
            }

            YargLogger.LogFormatInfo(
                "HGP bridge stats: packets {0}, rings {1}, dropped (unsynced) {2}, dropped (duplicate) {3}, " +
                "clamped (monotonic) {4}, clamped (future) {5}",
                _packets, _rings, _droppedUnsynced, _droppedDuplicate, _clampedMonotonic, _clampedFuture);

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var bytes = _udp.Receive(ref remote);
                    long ticks = Stopwatch.GetTimestamp();
                    _received.Enqueue(new Received { Text = Encoding.UTF8.GetString(bytes), Ticks = ticks });
                }
                catch (SocketException)
                {
                    // Closed or transient
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void Update()
        {
            double now = InputState.currentTime;
            long nowTicks = Stopwatch.GetTimestamp();

            while (_received.TryDequeue(out var item))
            {
                _packets++;
                double localReceive = now - (nowTicks - item.Ticks) / (double) Stopwatch.Frequency;
                Handle(HgpPacket.Parse(item.Text), localReceive, now);
            }

            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                var ring = _delayed[i];
                if (ring.ReleaseAt > now)
                {
                    continue;
                }

                _delayed.RemoveAt(i);
                QueueRing(ring.Device, ring.Bell, ring.Down, ring.Time, now);
            }
        }

        private void Handle(in HgpPacket packet, double localReceive, double now)
        {
            switch (packet.Kind)
            {
                case HgpPacketKind.Sync:
                    _clock.AddSample(packet.Time, localReceive);
                    break;
                case HgpPacketKind.Hello:
                    if (!_devices.ContainsKey(packet.Device))
                    {
                        AddBellDevice(packet.Device, packet.Slot, packet.Name);
                    }
                    break;
                case HgpPacketKind.Bye:
                    if (_devices.TryGetValue(packet.Device, out var leaving))
                    {
                        RemoveBellDevice(packet.Device, leaving);
                    }
                    break;
                case HgpPacketKind.Ring:
                    if (!_clock.IsSynced)
                    {
                        _droppedUnsynced++;
                        break;
                    }

                    if (_lastSequence.TryGetValue(packet.Device, out long lastSeq) && packet.Sequence <= lastSeq)
                    {
                        _droppedDuplicate++;
                        break;
                    }
                    _lastSequence[packet.Device] = packet.Sequence;

                    if (!_devices.TryGetValue(packet.Device, out var device))
                    {
                        device = AddBellDevice(packet.Device, _devices.Count, null);
                    }

                    QueueRing(device, packet.Bell, packet.Down, _clock.ToLocal(packet.Time), now);
                    break;
            }
        }

        private void QueueRing(HgpBellDevice device, int bell, bool down, double time, double now)
        {
            if (device == null || !device.added || bell < 0 || bell >= HgpRingerSlots.MAX_BELLS)
            {
                return;
            }

            // The Input System rejects events older than the device's last event, and YARG logs
            // events from the future; keep each device's timeline monotonic and in the past
            if (time > now)
            {
                time = now;
                _clampedFuture++;
            }

            if (_lastEventTime.TryGetValue(device, out double last) && time < last)
            {
                time = last;
                _clampedMonotonic++;
            }
            _lastEventTime[device] = time;

            byte state = _bellState.TryGetValue(device, out byte s) ? s : (byte) 0;
            state = down ? (byte) (state | (1 << bell)) : (byte) (state & ~(1 << bell));
            _bellState[device] = state;

            InputSystem.QueueStateEvent(device, new HgpBellState { bells = state }, time);
            _rings++;
        }

        private HgpBellDevice AddBellDevice(string id, int slot, string name)
        {
            var device = InputSystem.AddDevice(new InputDeviceDescription
            {
                interfaceName = HgpBellDevice.INTERFACE_NAME,
                manufacturer = "handbell-hero",
                product = string.IsNullOrEmpty(name) ? $"HGP Bell {id}" : name,
                serial = id,
            }) as HgpBellDevice;

            if (device == null)
            {
                YargLogger.LogFormatError("HGP: failed to create bell device {0}", id);
                return null;
            }

            device.RingerSlot = slot;
            device.LatencyBudgetSeconds = DefaultLatencyBudgetSeconds;
            _devices[id] = device;
            YargLogger.LogFormatInfo("HGP: bell device {0} joined as ringer slot {1}", id, slot);
            return device;
        }

        private void RemoveBellDevice(string id, HgpBellDevice device)
        {
            _devices.Remove(id);
            _bellState.Remove(device);
            _lastEventTime.Remove(device);
            _lastSequence.Remove(id);
            InputSystem.RemoveDevice(device);
        }

        private void OnKeyboardEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device is not Keyboard keyboard || (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()))
            {
                return;
            }

            for (int ringer = 0; ringer < KEYBOARD_RINGERS.Length; ringer++)
            {
                var keys = KEYBOARD_RINGERS[ringer];
                for (int bell = 0; bell < keys.Length; bell++)
                {
                    KeyControl key = keyboard[keys[bell]];
                    if (!key.ReadValueFromEvent(eventPtr, out float value))
                    {
                        continue;
                    }

                    bool pressed = value >= 0.5f;
                    if (pressed == _keyboardPressed[ringer, bell])
                    {
                        continue;
                    }

                    _keyboardPressed[ringer, bell] = pressed;
                    double delay = _kbdDelay + _rng.NextDouble() * _kbdJitter;
                    _delayed.Add(new DelayedRing
                    {
                        Device = _keyboardDevices[ringer],
                        Bell = bell,
                        Down = pressed,
                        Time = eventPtr.time,
                        ReleaseAt = eventPtr.time + delay,
                    });
                }
            }
        }

        private static bool HasFlag(string arg, string env)
        {
            if (Environment.GetEnvironmentVariable(env) is "1" or "true")
            {
                return true;
            }

            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a == arg)
                {
                    return true;
                }
            }

            return false;
        }

        private static double GetNumber(string arg, string env, double fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == arg && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    return v;
                }
            }

            if (double.TryParse(Environment.GetEnvironmentVariable(env), NumberStyles.Float, CultureInfo.InvariantCulture, out double e))
            {
                return e;
            }

            return fallback;
        }
    }
}
