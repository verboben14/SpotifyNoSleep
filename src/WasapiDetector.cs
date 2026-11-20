using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpotifyNoSleep
{
    public sealed class WasapiDetector : IWasapiDetector
    {
        private const int ERender = 0;
        private const int DeviceStateActive = 0x00000001;
        private const int ClsCtxAll = 23;
        private static Guid IidAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        public WasapiSnapshot Detect()
        {
            WasapiSnapshot snapshot = new WasapiSnapshot();
            HashSet<uint> spotifyPids = GetSpotifyProcessIds();
            snapshot.SpotifyProcessDetected = spotifyPids.Count > 0;

            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out devices);
                if (hr != 0 || devices == null)
                {
                    snapshot.IsAvailable = false;
                    snapshot.ErrorMessage = string.Format("EnumAudioEndpoints failed (0x{0:X8})", hr);
                    return snapshot;
                }

                snapshot.IsAvailable = true;

                int deviceCount;
                hr = devices.GetCount(out deviceCount);
                if (hr != 0)
                {
                    return snapshot;
                }

                for (int i = 0; i < deviceCount; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        if (devices.Item(i, out device) == 0 && device != null)
                        {
                            InspectDeviceSessions(device, spotifyPids, snapshot);
                        }
                    }
                    finally
                    {
                        ReleaseCom(device);
                    }
                }
            }
            catch (Exception ex)
            {
                snapshot.IsAvailable = false;
                snapshot.ErrorMessage = ex.Message;
            }
            finally
            {
                ReleaseCom(devices);
                ReleaseCom(enumerator);
            }

            return snapshot;
        }

        private static void InspectDeviceSessions(
            IMMDevice device,
            HashSet<uint> spotifyPids,
            WasapiSnapshot snapshot)
        {
            object managerObj = null;
            IAudioSessionManager2 sessionManager = null;
            IAudioSessionEnumerator sessionEnumerator = null;

            try
            {
                Guid iid = IidAudioSessionManager2;
                int hr = device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out managerObj);
                if (hr != 0 || managerObj == null)
                {
                    return;
                }

                sessionManager = managerObj as IAudioSessionManager2;
                if (sessionManager == null)
                {
                    return;
                }

                hr = sessionManager.GetSessionEnumerator(out sessionEnumerator);
                if (hr != 0 || sessionEnumerator == null)
                {
                    return;
                }

                int count;
                if (sessionEnumerator.GetCount(out count) != 0)
                {
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl control = null;
                    try
                    {
                        if (sessionEnumerator.GetSession(i, out control) != 0 || control == null)
                        {
                            continue;
                        }

                        IAudioSessionControl2 control2 = control as IAudioSessionControl2;
                        if (control2 == null)
                        {
                            continue;
                        }

                        if (!IsSpotifySession(control2, spotifyPids))
                        {
                            continue;
                        }

                        snapshot.SpotifyProcessDetected = true;

                        AudioSessionState state;
                        if (control2.GetState(out state) == 0 && state == AudioSessionState.Active)
                        {
                            snapshot.HasActiveSession = true;
                            snapshot.ActiveSessionCount++;

                            IAudioMeterInformation meter = control as IAudioMeterInformation;
                            if (meter != null)
                            {
                                float peak;
                                if (meter.GetPeakValue(out peak) == 0 && peak > snapshot.MaxPeakLevel)
                                {
                                    snapshot.MaxPeakLevel = peak;
                                }
                            }
                        }
                    }
                    finally
                    {
                        ReleaseCom(control);
                    }
                }
            }
            finally
            {
                ReleaseCom(sessionEnumerator);
                ReleaseCom(sessionManager);
                if (managerObj != null && !ReferenceEquals(managerObj, sessionManager))
                {
                    ReleaseCom(managerObj);
                }
            }
        }

        private static bool IsSpotifySession(IAudioSessionControl2 control2, HashSet<uint> spotifyPids)
        {
            uint pid;
            if (control2.GetProcessId(out pid) == 0 && pid != 0 && spotifyPids.Contains(pid))
            {
                return true;
            }

            string sessionId;
            if (control2.GetSessionIdentifier(out sessionId) == 0 && !string.IsNullOrEmpty(sessionId))
            {
                if (sessionId.IndexOf("spotify.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<uint> GetSpotifyProcessIds()
        {
            HashSet<uint> pids = new HashSet<uint>();
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName("Spotify");
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        pids.Add((uint)processes[i].Id);
                    }
                    finally
                    {
                        processes[i].Dispose();
                    }
                }
            }
            catch
            {
                // Ignore transient process enumeration failures.
            }
            return pids;
        }

        private static void ReleaseCom(object comObj)
        {
            if (comObj != null && Marshal.IsComObject(comObj))
            {
                try
                {
                    Marshal.ReleaseComObject(comObj);
                }
                catch
                {
                }
            }
        }

        private enum AudioSessionState
        {
            Inactive = 0,
            Active = 1,
            Expired = 2
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out int pcDevices);

            [PreserveSig]
            int Item(int nDevice, out IMMDevice ppDevice);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(
                ref Guid iid,
                int dwClsCtx,
                IntPtr pActivationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig]
            int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);

            [PreserveSig]
            int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr audioVolume);

            [PreserveSig]
            int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig]
            int GetCount(out int sessionCount);

            [PreserveSig]
            int GetSession(int sessionIndex, out IAudioSessionControl session);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            [PreserveSig]
            int GetState(out AudioSessionState pRetVal);

            [PreserveSig]
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

            [PreserveSig]
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

            [PreserveSig]
            int GetGroupingParam(out Guid pRetVal);

            [PreserveSig]
            int SetGroupingParam(ref Guid overrideGuid, ref Guid eventContext);

            [PreserveSig]
            int RegisterAudioSessionNotification(IntPtr newNotifications);

            [PreserveSig]
            int UnregisterAudioSessionNotification(IntPtr newNotifications);
        }

        [ComImport]
        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            [PreserveSig]
            int GetState(out AudioSessionState pRetVal);

            [PreserveSig]
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

            [PreserveSig]
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

            [PreserveSig]
            int GetGroupingParam(out Guid pRetVal);

            [PreserveSig]
            int SetGroupingParam(ref Guid overrideGuid, ref Guid eventContext);

            [PreserveSig]
            int RegisterAudioSessionNotification(IntPtr newNotifications);

            [PreserveSig]
            int UnregisterAudioSessionNotification(IntPtr newNotifications);

            [PreserveSig]
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int GetProcessId(out uint pRetVal);

            [PreserveSig]
            int IsSystemSoundsSession();

            [PreserveSig]
            int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport]
        [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            [PreserveSig]
            int GetPeakValue(out float pfPeak);
        }
    }
}
