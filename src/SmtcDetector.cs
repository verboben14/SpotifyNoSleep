using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;

namespace SpotifyNoSleep
{
    public sealed class SmtcDetector : ISmtcDetector
    {
        private readonly object _syncRoot = new object();
        private GlobalSystemMediaTransportControlsSessionManager _sessionManager;

        public SmtcSnapshot Detect()
        {
            SmtcSnapshot snapshot = new SmtcSnapshot();
            try
            {
                string errorMessage;
                GlobalSystemMediaTransportControlsSessionManager manager = GetSessionManager(out errorMessage);
                if (manager == null)
                {
                    snapshot.State = SmtcState.Unavailable;
                    snapshot.ErrorMessage = errorMessage;
                    return snapshot;
                }

                IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = manager.GetSessions();
                if (sessions == null || sessions.Count == 0)
                {
                    snapshot.State = SmtcState.NotFound;
                    return snapshot;
                }

                GlobalSystemMediaTransportControlsSession bestSession = null;
                SmtcState bestState = SmtcState.NotFound;

                for (int i = 0; i < sessions.Count; i++)
                {
                    GlobalSystemMediaTransportControlsSession session = sessions[i];
                    if (session == null)
                    {
                        continue;
                    }

                    string appId = session.SourceAppUserModelId ?? string.Empty;
                    if (appId.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    SmtcState state = MapPlaybackStatus(session);

                    // Prefer an actively playing session if multiple Spotify sessions exist.
                    if (bestSession == null || state == SmtcState.Playing)
                    {
                        bestSession = session;
                        bestState = state;
                        if (state == SmtcState.Playing)
                        {
                            break;
                        }
                    }
                }

                if (bestSession == null)
                {
                    snapshot.State = SmtcState.NotFound;
                    return snapshot;
                }

                snapshot.State = bestState;
                snapshot.SourceAppId = bestSession.SourceAppUserModelId ?? string.Empty;

                if (bestState == SmtcState.Playing)
                {
                    PopulateTrackMetadata(bestSession, snapshot);
                }

                return snapshot;
            }
            catch (Exception ex)
            {
                lock (_syncRoot)
                {
                    _sessionManager = null;
                }
                snapshot.State = SmtcState.Unavailable;
                snapshot.ErrorMessage = ex.Message;
                return snapshot;
            }
        }

        private GlobalSystemMediaTransportControlsSessionManager GetSessionManager(out string errorMessage)
        {
            errorMessage = string.Empty;
            lock (_syncRoot)
            {
                if (_sessionManager != null)
                {
                    return _sessionManager;
                }
            }

            GlobalSystemMediaTransportControlsSessionManager result = WaitForAsyncOperation(
                GlobalSystemMediaTransportControlsSessionManager.RequestAsync(),
                1500,
                out errorMessage);

            if (result == null)
            {
                return null;
            }

            lock (_syncRoot)
            {
                _sessionManager = result;
            }
            return result;
        }

        private static SmtcState MapPlaybackStatus(GlobalSystemMediaTransportControlsSession session)
        {
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
            if (info == null)
            {
                return SmtcState.Other;
            }

            switch (info.PlaybackStatus)
            {
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing:
                    return SmtcState.Playing;
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused:
                    return SmtcState.Paused;
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped:
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed:
                    return SmtcState.Stopped;
                default:
                    return SmtcState.Other;
            }
        }

        private static void PopulateTrackMetadata(
            GlobalSystemMediaTransportControlsSession session,
            SmtcSnapshot snapshot)
        {
            try
            {
                string ignored;
                GlobalSystemMediaTransportControlsSessionMediaProperties props = WaitForAsyncOperation(
                    session.TryGetMediaPropertiesAsync(),
                    600,
                    out ignored);

                if (props != null)
                {
                    snapshot.TrackTitle = props.Title ?? string.Empty;
                    snapshot.TrackArtist = props.Artist ?? string.Empty;
                }
            }
            catch
            {
                // Metadata is purely informational; ignore transient WinRT errors.
            }
        }

        private static T WaitForAsyncOperation<T>(
            IAsyncOperation<T> operation,
            int timeoutMs,
            out string errorMessage) where T : class
        {
            errorMessage = string.Empty;
            if (operation == null)
            {
                errorMessage = "Null WinRT async operation";
                return null;
            }

            if (operation.Status == AsyncStatus.Completed)
            {
                return operation.GetResults();
            }

            ManualResetEventSlim waitHandle = new ManualResetEventSlim(false);
            try
            {
                operation.Completed = delegate(IAsyncOperation<T> op, AsyncStatus status)
                {
                    try
                    {
                        waitHandle.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                };

                if (operation.Status == AsyncStatus.Started)
                {
                    waitHandle.Wait(timeoutMs);
                }

                AsyncStatus finalStatus = operation.Status;
                if (finalStatus == AsyncStatus.Completed)
                {
                    return operation.GetResults();
                }
                if (finalStatus == AsyncStatus.Error)
                {
                    Exception err = operation.ErrorCode;
                    int hresult = err != null ? err.HResult : 0;
                    errorMessage = string.Format(
                        "WinRT async error (0x{0:X8}): {1}",
                        unchecked((uint)hresult),
                        err != null ? err.Message : "unknown");
                    return null;
                }

                errorMessage = string.Format("WinRT async operation ended with status {0}", finalStatus);
                return null;
            }
            finally
            {
                waitHandle.Dispose();
            }
        }
    }
}
