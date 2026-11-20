using System;

namespace SpotifyNoSleep
{
    public enum SmtcState
    {
        Unavailable,
        NotFound,
        Playing,
        Paused,
        Stopped,
        Other
    }

    public sealed class SmtcSnapshot
    {
        public SmtcState State { get; set; }
        public string SourceAppId { get; set; }
        public string TrackTitle { get; set; }
        public string TrackArtist { get; set; }
        public string ErrorMessage { get; set; }

        public SmtcSnapshot()
        {
            State = SmtcState.Unavailable;
            SourceAppId = string.Empty;
            TrackTitle = string.Empty;
            TrackArtist = string.Empty;
            ErrorMessage = string.Empty;
        }

        public string GetDisplayTrack()
        {
            bool hasArtist = !string.IsNullOrWhiteSpace(TrackArtist);
            bool hasTitle = !string.IsNullOrWhiteSpace(TrackTitle);

            if (hasArtist && hasTitle)
            {
                return TrackArtist.Trim() + " - " + TrackTitle.Trim();
            }
            if (hasTitle)
            {
                return TrackTitle.Trim();
            }
            return string.Empty;
        }
    }

    public sealed class WasapiSnapshot
    {
        public bool IsAvailable { get; set; }
        public bool SpotifyProcessDetected { get; set; }
        public bool HasActiveSession { get; set; }
        public float MaxPeakLevel { get; set; }
        public int ActiveSessionCount { get; set; }
        public string ErrorMessage { get; set; }

        public WasapiSnapshot()
        {
            ErrorMessage = string.Empty;
        }
    }

    public sealed class WindowTitleSnapshot
    {
        public bool SpotifyRunning { get; set; }
        public bool AppearsPlaying { get; set; }
        public string RawTitle { get; set; }

        public WindowTitleSnapshot()
        {
            RawTitle = string.Empty;
        }
    }

    public sealed class PlaybackSnapshot
    {
        public DateTime UtcNow { get; set; }
        public SmtcSnapshot Smtc { get; set; }
        public WasapiSnapshot Wasapi { get; set; }
        public WindowTitleSnapshot WindowTitle { get; set; }

        public PlaybackSnapshot()
        {
            UtcNow = DateTime.UtcNow;
            Smtc = new SmtcSnapshot();
            Wasapi = new WasapiSnapshot();
            WindowTitle = new WindowTitleSnapshot();
        }
    }

    public sealed class PlaybackDecision
    {
        public bool ShouldKeepAwake { get; set; }
        public bool IsActivelyPlaying { get; set; }
        public bool InGracePeriod { get; set; }
        public string Reason { get; set; }
        public string CurrentTrack { get; set; }

        public PlaybackDecision()
        {
            Reason = string.Empty;
            CurrentTrack = string.Empty;
        }
    }

    public interface ISmtcDetector
    {
        SmtcSnapshot Detect();
    }

    public interface IWasapiDetector
    {
        WasapiSnapshot Detect();
    }

    public interface IWindowTitleDetector
    {
        WindowTitleSnapshot Detect();
    }
}
