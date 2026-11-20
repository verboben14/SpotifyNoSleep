using System;
using System.Runtime.InteropServices;

namespace SpotifyNoSleep
{
    public sealed class PowerManager : IDisposable
    {
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;

        private const uint PowerRequestContextVersion = 0;
        private const uint PowerRequestContextSimpleString = 0x00000001;
        private const int PowerRequestDisplayRequired = 0;
        private const int PowerRequestSystemRequired = 1;

        private const uint InputMouse = 0;
        private const uint MouseEventFMove = 0x0001;
        private const uint IdleNudgeThresholdMs = 45000;

        private readonly Logger _logger;
        private IntPtr _powerRequestHandle;
        private bool _systemRequestHeld;
        private bool _displayRequestHeld;
        private bool _isAwakeActive;
        private bool _disposed;

        public bool IsAwakeActive { get { return _isAwakeActive; } }
        public bool HasKernelPowerRequest { get { return _powerRequestHandle != IntPtr.Zero; } }

        public PowerManager(Logger logger)
        {
            _logger = logger;
            _powerRequestHandle = CreatePowerRequest();
        }

        public void ApplyState(bool keepAwake, bool preventLockScreen, bool strictLockPrevention)
        {
            if (_disposed)
            {
                return;
            }

            if (keepAwake)
            {
                SetPowerRequest(PowerRequestSystemRequired, true, ref _systemRequestHeld);
                SetPowerRequest(PowerRequestDisplayRequired, preventLockScreen, ref _displayRequestHeld);

                uint flags = EsContinuous | EsSystemRequired;
                if (preventLockScreen)
                {
                    flags |= EsDisplayRequired;
                }
                SetThreadExecutionState(flags);

                if (preventLockScreen && strictLockPrevention)
                {
                    NudgeInputIfUserIdle();
                }

                _isAwakeActive = true;
            }
            else
            {
                ReleaseAll();
            }
        }

        public void ReleaseAll()
        {
            SetPowerRequest(PowerRequestDisplayRequired, false, ref _displayRequestHeld);
            SetPowerRequest(PowerRequestSystemRequired, false, ref _systemRequestHeld);
            SetThreadExecutionState(EsContinuous);
            _isAwakeActive = false;
        }

        private IntPtr CreatePowerRequest()
        {
            try
            {
                POWER_REQUEST_CONTEXT context = new POWER_REQUEST_CONTEXT();
                context.Version = PowerRequestContextVersion;
                context.Flags = PowerRequestContextSimpleString;
                context.SimpleReasonString = "Spotify is actively playing music";

                IntPtr handle = PowerCreateRequest(ref context);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    return IntPtr.Zero;
                }
                return handle;
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.Warn("PowerCreateRequest unavailable; falling back to SetThreadExecutionState: " + ex.Message);
                }
                return IntPtr.Zero;
            }
        }

        private void SetPowerRequest(int requestType, bool desired, ref bool currentlyHeld)
        {
            if (_powerRequestHandle == IntPtr.Zero || desired == currentlyHeld)
            {
                return;
            }

            try
            {
                if (desired)
                {
                    if (PowerSetRequest(_powerRequestHandle, requestType))
                    {
                        currentlyHeld = true;
                    }
                }
                else
                {
                    if (PowerClearRequest(_powerRequestHandle, requestType))
                    {
                        currentlyHeld = false;
                    }
                }
            }
            catch
            {
                // SetThreadExecutionState provides fallback protection.
            }
        }

        private static void NudgeInputIfUserIdle()
        {
            uint idleMs = GetIdleTimeMilliseconds();
            if (idleMs < IdleNudgeThresholdMs)
            {
                return;
            }

            INPUT[] inputs = new INPUT[1];
            inputs[0].Type = InputMouse;
            inputs[0].Data.Mouse.Dx = 0;
            inputs[0].Data.Mouse.Dy = 0;
            inputs[0].Data.Mouse.MouseData = 0;
            inputs[0].Data.Mouse.Flags = MouseEventFMove;
            inputs[0].Data.Mouse.Time = 0;
            inputs[0].Data.Mouse.ExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static uint GetIdleTimeMilliseconds()
        {
            LASTINPUTINFO info = new LASTINPUTINFO();
            info.Size = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info))
            {
                return 0;
            }

            uint tickCount = unchecked((uint)Environment.TickCount);
            return unchecked(tickCount - info.Time);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseAll();

            if (_powerRequestHandle != IntPtr.Zero)
            {
                try
                {
                    CloseHandle(_powerRequestHandle);
                }
                catch
                {
                }
                _powerRequestHandle = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct POWER_REQUEST_CONTEXT
        {
            public uint Version;
            public uint Flags;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string SimpleReasonString;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint Size;
            public uint Time;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;
            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;
            [FieldOffset(0)]
            public HARDWAREINPUT Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort Vk;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint Msg;
            public ushort ParamL;
            public ushort ParamH;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr PowerCreateRequest(ref POWER_REQUEST_CONTEXT context);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    }
}
