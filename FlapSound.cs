using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// This file is written in plain, older C# on purpose: the same file is compiled by a test script
// that plays scheduled clacks and measures how exactly they are placed and what the player costs.
#pragma warning disable CS8600, CS8601, CS8602, CS8618, CS8625, CS0649

namespace FlipClock
{
    /// <summary>
    /// Plays the flap sound exactly when a card lands.
    ///
    /// Every clack is ordered ahead of time ("land in 82 ms"), and a small mixer writes it into one
    /// continuous sound stream at exactly that sample. That is why clacks are not late, never cut each
    /// other off, and several cards landing together are all heard.
    ///
    /// The stream is opened shortly before cards turn and closed again a few seconds after the last clack,
    /// so a resting clock keeps no sound device busy. With the sound switched off nothing runs at all.
    ///
    /// It talks to Windows' own sound engine (WASAPI). The older "waveOut" interface was measured to cost
    /// about 10 % of a processor core for as long as a stream was open.
    /// </summary>
    public static class FlapSound
    {
        private const int SampleRate = 44100;
        private const int TargetFill = SampleRate * 45 / 1000; // keep about 45 ms queued
        private const long BufferHns = 1000000;                 // room for 100 ms
        private const int IdleCloseMs = 2500;
        private const int MaxVoices = 24;
        private const int LateLimit = SampleRate * 35 / 1000;  // more than 35 ms late: do not play it

        /// <summary>
        /// How long before a flip the stream has to be woken. Opening a sound device is slow on some PCs
        /// (0.4 to 1.1 s were measured), so the clock asks well ahead.
        /// </summary>
        public const int WarmUpMs = 1700;

        private const uint AUTOCONVERT = 0x80000000, GOOD_RESAMPLING = 0x08000000;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class DeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WAVEFORMATEX format, IntPtr session);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint frames);
            [PreserveSig] int IsFormatSupported(int shareMode, ref WAVEFORMATEX format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioRenderClient
        {
            [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
            [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
        }

        private sealed class Voice
        {
            public short[] Data = new short[0];
            public int Anchor;      // where inside the clip the hit itself sits
            public long DueTicks;   // when the card lands, on the stopwatch
            public long Start = -1; // the clip's first sample as a position in the sound stream, once known
            public float Gain;
        }

        private static readonly object Gate = new object();
        private static readonly List<Voice> Voices = new List<Voice>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static readonly Random Rnd = new Random(5);

        private static short[][] _clips = new short[0][];
        private static int[] _anchors = new int[0];
        private static Thread _thread;
        private static volatile bool _enabled, _open;
        private static bool _warm;
        private static int _nextClip;

        // touched only by the sound thread
        private static IAudioClient _client;
        private static IAudioRenderClient _render;
        private static uint _bufferFrames;
        private static long _written, _lastUse;
        private static double _speakerLeadMs = 12;
        private static int[] _sum = new int[0];
        private static short[] _out = new short[0];

        /// <summary>Test hook: receives one line per event. Not set in the clock.</summary>
        public static Action<string> Log;

        public static bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; } // clacks already ordered simply finish; new ones are not ordered
        }

        /// <summary>True while the sound stream is running and a clack can be placed exactly.</summary>
        public static bool IsOpen { get { return _open; } }

        public static bool HasClips { get { lock (Gate) return _clips.Length > 0; } }

        /// <summary>
        /// The recorded clacks as plain samples (mono, 16 bit, 44.1 kHz). For each one the player notes
        /// where its hit sits, so that the hit, not the start of the file, falls on the landing card.
        /// </summary>
        public static void SetClips(short[][] clips)
        {
            if (clips == null) clips = new short[0][];
            int[] anchors = new int[clips.Length];
            for (int c = 0; c < clips.Length; c++)
            {
                int peak = 0;
                for (int i = 0; i < clips[c].Length; i++) peak = Math.Max(peak, Math.Abs((int)clips[c][i]));
                for (int i = 0; i < clips[c].Length; i++)
                    if (Math.Abs((int)clips[c][i]) * 2 >= peak) { anchors[c] = i; break; }
            }
            lock (Gate) { _clips = clips; _anchors = anchors; _nextClip = 0; }
        }

        /// <summary>Cards are about to turn: get the sound stream running so the first clack is not late.</summary>
        public static void Warm()
        {
            if (!_enabled) return;
            WarmAlways();
        }

        /// <summary>Wake the stream even if the flap sound is switched off (the alarm needs it).</summary>
        public static void WarmAlways()
        {
            lock (Gate) { _warm = true; EnsureThread(); }
            Wake.Set();
        }

        /// <summary>
        /// Play any sound in <paramref name="delayMs"/> milliseconds, whether or not the flap sound is on.
        /// Used for the alarm beep.
        /// </summary>
        public static void Play(short[] sound, double delayMs, float gain)
        {
            if (sound == null || sound.Length == 0) return;
            lock (Gate)
            {
                if (Voices.Count >= MaxVoices) return;
                Voice v = new Voice();
                v.Data = sound;
                v.Anchor = 0;
                v.DueTicks = Clock.ElapsedTicks + (long)(delayMs * Stopwatch.Frequency / 1000.0);
                v.Gain = gain;
                Voices.Add(v);
                EnsureThread();
            }
            Wake.Set();
        }

        /// <summary>A card will land in <paramref name="delayMs"/> milliseconds: order its clack for exactly then.</summary>
        public static void Hit(double delayMs)
        {
            if (!_enabled) return;
            lock (Gate)
            {
                if (_clips.Length == 0 || Voices.Count >= MaxVoices) return;
                Voice v = new Voice();
                v.Data = _clips[_nextClip];
                v.Anchor = _anchors[_nextClip];
                _nextClip = (_nextClip + 1) % _clips.Length;
                v.DueTicks = Clock.ElapsedTicks + (long)(delayMs * Stopwatch.Frequency / 1000.0);
                v.Gain = 0.86f + 0.14f * (float)Rnd.NextDouble();
                Voices.Add(v);
                EnsureThread();
            }
            Wake.Set();
        }

        private static void EnsureThread()
        {
            if (_thread != null) return;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "Flap sound";
            _thread.Priority = ThreadPriority.AboveNormal;
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private static void Run()
        {
            while (true)
            {
                // stream closed: sleep until somebody orders a clack. Stream open: top it up every few ms.
                Wake.WaitOne(_client != null ? 10 : Timeout.Infinite);
                try { Pump(); }
                catch (Exception e)
                {
                    // device unplugged, sound service restarted, ...: go quiet, try again at the next flip
                    if (Log != null) Log("error: " + e.Message);
                    CloseDevice();
                    lock (Gate) Voices.Clear();
                }
            }
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException(what + " failed (0x" + hr.ToString("X8") + ")");
        }

        private static void Pump()
        {
            bool wanted;
            lock (Gate)
            {
                wanted = Voices.Count > 0 || _warm;
                _warm = false;
            }
            if (wanted) _lastUse = Clock.ElapsedMilliseconds;

            if (_client == null)
            {
                if (!wanted) return;
                OpenDevice();
            }

            uint padding;
            Check(_client.GetCurrentPadding(out padding), "GetCurrentPadding");
            int n = Math.Min(TargetFill - (int)padding, (int)_bufferFrames - (int)padding);
            if (n > 0)
            {
                IntPtr target;
                Check(_render.GetBuffer((uint)n, out target), "GetBuffer");
                Mix(n, _written - padding);
                Marshal.Copy(_out, 0, target, n);
                Check(_render.ReleaseBuffer((uint)n, 0), "ReleaseBuffer");
                _written += n;
            }

            bool silent;
            lock (Gate) silent = Voices.Count == 0;
            if (silent && Clock.ElapsedMilliseconds - _lastUse > IdleCloseMs) CloseDevice();
        }

        /// <param name="n">samples to produce</param>
        /// <param name="played">how many samples the sound engine has taken so far</param>
        private static void Mix(int n, long played)
        {
            if (_sum.Length < n) { _sum = new int[n]; _out = new short[n]; }
            Array.Clear(_sum, 0, n);
            long from = _written, to = _written + n;
            long now = Clock.ElapsedTicks;

            lock (Gate)
            {
                for (int i = Voices.Count - 1; i >= 0; i--)
                {
                    Voice v = Voices[i];
                    if (v.Start < 0)
                    {
                        double msLeft = (v.DueTicks - now) * 1000.0 / Stopwatch.Frequency - _speakerLeadMs;
                        long wish = played + (long)(msLeft * SampleRate / 1000.0) - v.Anchor;
                        if (wish >= to) continue; // belongs into a later piece: decide then, with a fresher clock
                        if (from - wish > LateLimit)
                        {
                            // The stream was not ready in time. A clack that comes late is worse than none.
                            if (Log != null) Log("clack dropped, it would have been " + ((from - wish) * 1000.0 / SampleRate).ToString("0") + " ms late");
                            Voices.RemoveAt(i);
                            continue;
                        }
                        v.Start = wish < from ? from : wish;
                        if (Log != null)
                            Log("clack placed " + ((v.Start - wish) * 1000.0 / SampleRate).ToString("0.0") + " ms after its exact moment");
                    }
                    long a = Math.Max(from, v.Start), b = Math.Min(to, v.Start + v.Data.Length);
                    for (long p = a; p < b; p++) _sum[p - from] += (int)(v.Data[p - v.Start] * v.Gain);
                    if (v.Start + v.Data.Length <= to) Voices.RemoveAt(i);
                }
            }

            for (int i = 0; i < n; i++)
            {
                int s = _sum[i];
                _out[i] = (short)(s > 32767 ? 32767 : s < -32768 ? -32768 : s);
            }
        }

        private static void OpenDevice()
        {
            long t0 = Clock.ElapsedTicks;
            IMMDeviceEnumerator devices = (IMMDeviceEnumerator)new DeviceEnumerator();
            IMMDevice device;
            Check(devices.GetDefaultAudioEndpoint(0, 0, out device), "no sound output");

            Guid clientId = typeof(IAudioClient).GUID;
            object o;
            Check(device.Activate(ref clientId, 23, IntPtr.Zero, out o), "Activate");
            IAudioClient client = (IAudioClient)o;

            WAVEFORMATEX f = new WAVEFORMATEX();
            f.wFormatTag = 1;
            f.nChannels = 1;
            f.nSamplesPerSec = SampleRate;
            f.nAvgBytesPerSec = SampleRate * 2;
            f.nBlockAlign = 2;
            f.wBitsPerSample = 16;
            f.cbSize = 0;
            // shared with every other program; Windows converts our simple format to whatever the device uses
            Check(client.Initialize(0, AUTOCONVERT | GOOD_RESAMPLING, BufferHns, 0, ref f, IntPtr.Zero), "Initialize");
            Check(client.GetBufferSize(out _bufferFrames), "GetBufferSize");

            long latency;
            if (client.GetStreamLatency(out latency) >= 0) _speakerLeadMs = Math.Max(5, Math.Min(60, latency / 10000.0));

            Guid renderId = typeof(IAudioRenderClient).GUID;
            Check(client.GetService(ref renderId, out o), "GetService");
            _render = (IAudioRenderClient)o;
            Check(client.Start(), "Start");

            _client = client;
            _written = 0;
            _open = true;
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(devices);
            if (Log != null) Log("stream opened in " + ((Clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms, device delay " + _speakerLeadMs.ToString("0") + " ms");
        }

        private static void CloseDevice()
        {
            _open = false;
            try
            {
                if (_client != null) _client.Stop();
                if (_render != null) Marshal.ReleaseComObject(_render);
                if (_client != null) Marshal.ReleaseComObject(_client);
            }
            catch { }
            bool was = _client != null;
            _render = null;
            _client = null;
            if (was && Log != null) Log("stream closed");
        }
    }
}
