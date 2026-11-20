using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SpotifyNoSleep
{
    public enum TrayVisualState
    {
        Idle,
        Active,
        Paused
    }

    public static class TrayIconFactory
    {
        public static Icon CreateIcon(TrayVisualState state)
        {
            const int size = 32;
            using (Bitmap bitmap = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Color circleColor;
                Color glyphColor = Color.White;

                switch (state)
                {
                    case TrayVisualState.Active:
                        circleColor = Color.FromArgb(29, 185, 84); // Spotify Green
                        break;
                    case TrayVisualState.Paused:
                        circleColor = Color.FromArgb(229, 160, 13); // Warm Amber
                        break;
                    default:
                        circleColor = Color.FromArgb(110, 118, 129); // Neutral Slate
                        break;
                }

                using (SolidBrush bgBrush = new SolidBrush(circleColor))
                {
                    g.FillEllipse(bgBrush, 1, 1, size - 2, size - 2);
                }

                using (Pen rimPen = new Pen(Color.FromArgb(90, 0, 0, 0), 1.2f))
                {
                    g.DrawEllipse(rimPen, 1.5f, 1.5f, size - 3f, size - 3f);
                }

                if (state == TrayVisualState.Paused)
                {
                    using (SolidBrush barBrush = new SolidBrush(glyphColor))
                    {
                        g.FillRectangle(barBrush, 10, 9, 4, 14);
                        g.FillRectangle(barBrush, 18, 9, 4, 14);
                    }
                }
                else
                {
                    using (Pen wavePen = new Pen(glyphColor, 2.6f))
                    {
                        wavePen.StartCap = LineCap.Round;
                        wavePen.EndCap = LineCap.Round;

                        g.DrawArc(wavePen, 5.5f, 8.5f, 21f, 14f, 212f, 116f);
                        g.DrawArc(wavePen, 7.5f, 13.5f, 17f, 11f, 212f, 116f);
                        g.DrawArc(wavePen, 9.5f, 18.2f, 13f, 8f, 212f, 116f);
                    }

                    if (state == TrayVisualState.Active)
                    {
                        using (SolidBrush haloBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                        {
                            g.FillEllipse(haloBrush, 21, 1, 10, 10);
                        }
                        using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(255, 232, 102)))
                        {
                            g.FillEllipse(dotBrush, 22.5f, 2.5f, 7f, 7f);
                        }
                    }
                }

                IntPtr hIcon = bitmap.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tempIcon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
