using System;

namespace SpotifyNoSleep
{
    public sealed class PlaybackDecisionEngine
    {
        private DateTime? _lastConfirmedPlayingUtc;
        private string _lastTrack;

        public int GracePeriodSeconds { get; set; }
        public float AudioPeakThreshold { get; set; }

        public PlaybackDecisionEngine(int gracePeriodSeconds, float audioPeakThreshold)
        {
            GracePeriodSeconds = gracePeriodSeconds < 0 ? 0 : gracePeriodSeconds;
            AudioPeakThreshold = audioPeakThreshold < 0f ? 0f : audioPeakThreshold;
            _lastTrack = string.Empty;
        }

        public PlaybackDecisionEngine()
            : this(10, 0.0001f)
        {
        }

        public void Reset()
        {
            _lastConfirmedPlayingUtc = null;
            _lastTrack = string.Empty;
        }

        public PlaybackDecision Evaluate(PlaybackSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            SmtcSnapshot smtc = snapshot.Smtc ?? new SmtcSnapshot();
            WasapiSnapshot wasapi = snapshot.Wasapi ?? new WasapiSnapshot();
            WindowTitleSnapshot window = snapshot.WindowTitle ?? new WindowTitleSnapshot();

            string track = smtc.GetDisplayTrack();
            if (string.IsNullOrWhiteSpace(track) && window.AppearsPlaying && !string.IsNullOrWhiteSpace(window.RawTitle))
            {
                track = window.RawTitle.Trim();
            }

            bool activelyPlaying = false;
            string reason = string.Empty;

            // 1. Authoritative SMTC "Playing" state (covers Desktop, Store/MSIX, muted or quiet passages)
            if (smtc.State == SmtcState.Playing)
            {
                activelyPlaying = true;
                reason = "SMTC reports Spotify is playing";
            }
            // 2. Real non-zero WASAPI audio peak from Spotify process (catches playback even if SMTC is stale or disabled)
            else if (wasapi.IsAvailable && wasapi.HasActiveSession && wasapi.MaxPeakLevel > AudioPeakThreshold)
            {
                activelyPlaying = true;
                reason = "WASAPI detects active Spotify audio output";
            }
            // 3. Explicit SMTC Paused/Stopped with no audible WASAPI peak
            else if (smtc.State == SmtcState.Paused || smtc.State == SmtcState.Stopped)
            {
                activelyPlaying = false;
                reason = smtc.State == SmtcState.Paused
                    ? "SMTC reports Spotify is paused"
                    : "SMTC reports Spotify is stopped";
            }
            // 4. SMTC unavailable or no Spotify SMTC session (e.g., overlay disabled in Spotify settings)
            else
            {
                if (wasapi.IsAvailable && wasapi.HasActiveSession)
                {
                    bool windowExplicitlyIdle = window.SpotifyRunning
                        && !window.AppearsPlaying
                        && !string.IsNullOrWhiteSpace(window.RawTitle);

                    if (!windowExplicitlyIdle)
                    {
                        activelyPlaying = true;
                        reason = "WASAPI reports active Spotify audio session";
                    }
                    else
                    {
                        activelyPlaying = false;
                        reason = "Spotify audio session is silent and window is idle";
                    }
                }
                else if (window.SpotifyRunning && window.AppearsPlaying)
                {
                    activelyPlaying = true;
                    reason = "Spotify window title indicates active playback";
                }
            }

            if (activelyPlaying)
            {
                _lastConfirmedPlayingUtc = snapshot.UtcNow;
                if (!string.IsNullOrWhiteSpace(track))
                {
                    _lastTrack = track;
                }

                return new PlaybackDecision
                {
                    ShouldKeepAwake = true,
                    IsActivelyPlaying = true,
                    InGracePeriod = false,
                    Reason = reason,
                    CurrentTrack = !string.IsNullOrWhiteSpace(track) ? track : _lastTrack
                };
            }

            bool spotifyPresent = (smtc.State != SmtcState.Unavailable && smtc.State != SmtcState.NotFound)
                || wasapi.SpotifyProcessDetected
                || window.SpotifyRunning;

            if (!spotifyPresent)
            {
                _lastConfirmedPlayingUtc = null;
                _lastTrack = string.Empty;

                return new PlaybackDecision
                {
                    ShouldKeepAwake = false,
                    IsActivelyPlaying = false,
                    InGracePeriod = false,
                    Reason = "Spotify is not running",
                    CurrentTrack = string.Empty
                };
            }

            if (_lastConfirmedPlayingUtc.HasValue && GracePeriodSeconds > 0)
            {
                double elapsedSeconds = (snapshot.UtcNow - _lastConfirmedPlayingUtc.Value).TotalSeconds;
                if (elapsedSeconds >= 0 && elapsedSeconds < GracePeriodSeconds)
                {
                    int remaining = (int)Math.Ceiling(GracePeriodSeconds - elapsedSeconds);
                    return new PlaybackDecision
                    {
                        ShouldKeepAwake = true,
                        IsActivelyPlaying = false,
                        InGracePeriod = true,
                        Reason = string.Format("Grace period ({0}s remaining) after playback pause/track change", remaining),
                        CurrentTrack = _lastTrack
                    };
                }
            }

            _lastConfirmedPlayingUtc = null;
            _lastTrack = string.Empty;

            return new PlaybackDecision
            {
                ShouldKeepAwake = false,
                IsActivelyPlaying = false,
                InGracePeriod = false,
                Reason = string.IsNullOrEmpty(reason) ? "Spotify is idle" : reason,
                CurrentTrack = string.Empty
            };
        }
    }
}
