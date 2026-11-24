using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace OffClock
{
    internal enum Mascot
    {
        None,
        Cat,
        Bear,
        Rabbit,
        Penguin,
        Chick
    }

    internal sealed class ClockPalette
    {
        private static readonly Dictionary<ClockTheme, ClockPalette> Themes = new Dictionary<ClockTheme, ClockPalette>
        {
            { ClockTheme.Dark, new ClockPalette(0x1A202B, 0x3F4B5D, 0xF3F8FF, 0xB1BFD3, 0x7BE8B8, 0xFFC286, 0xFF6A70, 0x3C485A) },
            { ClockTheme.Light, new ClockPalette(0xF6F8FB, 0xB8C6D6, 0x18283D, 0x43536A, 0x167256, 0x9A4812, 0xB51E36, 0xD6E0EB) },
            { ClockTheme.Sakura, new ClockPalette(0xFFF0F5, 0xEDBFD1, 0x4A1E33, 0x7E4A63, 0xA63F84, 0x9A4812, 0xC0152F, 0xF2D3DF) },
            { ClockTheme.Ocean, new ClockPalette(0x0F2236, 0x2A4B6B, 0xEAF6FF, 0x9FBCD6, 0x5CC8FF, 0xFFC46B, 0xFF6B6B, 0x22405D) },
            { ClockTheme.Forest, new ClockPalette(0x14261B, 0x2E4C38, 0xEEF7EE, 0xA8C3AE, 0x8FD67E, 0xF5C451, 0xFF7A6B, 0x294333) },
            { ClockTheme.Sunset, new ClockPalette(0x2B1A2F, 0x563A5C, 0xFFF1EA, 0xD4B4C9, 0xF7A1C8, 0xFFCB66, 0xFF5C61, 0x4A3150) },
            {
                ClockTheme.Cat, new ClockPalette(0x23222A, 0x46444F, 0xF5F3FA, 0xBEBACB, 0x7FDBCB, 0xFFC286, 0xFF6A70, 0x3D3B47,
                    Mascot.Cat, 0xC9C4D4, 0xC9C4D4, 0xF4A7B9, 0xF4A7B9, 0x23222A, 0xFF8FA8)
            },
            {
                ClockTheme.Bear, new ClockPalette(0xFBF4EA, 0xE2CDB0, 0x3A291B, 0x76604B, 0x2F7546, 0x9A4812, 0xB51E36, 0xEADBC6,
                    Mascot.Bear, 0x9C6B43, 0x9C6B43, 0xEBCFA8, 0x2B1D12, 0x2B1D12, 0xF28B82)
            },
            {
                ClockTheme.Rabbit, new ClockPalette(0xF7F3FC, 0xD3C6E6, 0x2E2340, 0x66587D, 0x7B54C4, 0x9A4812, 0xB51E36, 0xE6DDF2,
                    Mascot.Rabbit, 0xFFFFFF, 0xA99CBF, 0xF5A3BD, 0xF28FAF, 0x2E2340, 0xF48FB1)
            },
            {
                ClockTheme.Penguin, new ClockPalette(0xEEF6FB, 0xBCD5E6, 0x13283D, 0x47607A, 0x1A6AA8, 0x9A4812, 0xB51E36, 0xD5E6F1,
                    Mascot.Penguin, 0x23313F, 0x23313F, 0xFFFFFF, 0xF2A33A, 0x13283D, 0xFF9DB0)
            },
            {
                ClockTheme.Chick, new ClockPalette(0xFFF8DD, 0xEBD9A0, 0x3F3210, 0x6E5F2E, 0x2E7D32, 0xA0480E, 0xB51E36, 0xF1E6B8,
                    Mascot.Chick, 0xFFD23F, 0xD9A400, 0xFFD23F, 0xF08A24, 0x3F3210, 0xFF9A8A)
            }
        };

        public readonly Color Background, Border, Digits, Muted, Normal, Soon, Urgent, Track;
        public readonly Mascot Mascot;
        // Fur is the main body, Detail the inner ears/muzzle/face, Feature the nose or beak.
        public readonly Color Fur, Outline, Detail, Feature, Eyes, Blush;

        private ClockPalette(int background, int border, int digits, int muted, int normal, int soon, int urgent, int track,
            Mascot mascot = Mascot.None, int fur = 0, int outline = 0, int detail = 0, int feature = 0, int eyes = 0, int blush = 0)
        {
            Background = Opaque(background);
            Border = Opaque(border);
            Digits = Opaque(digits);
            Muted = Opaque(muted);
            Normal = Opaque(normal);
            Soon = Opaque(soon);
            Urgent = Opaque(urgent);
            Track = Opaque(track);
            Mascot = mascot;
            Fur = Opaque(fur);
            Outline = Opaque(outline);
            Detail = Opaque(detail);
            Feature = Opaque(feature);
            Eyes = Opaque(eyes);
            Blush = Opaque(blush);
        }

        private static Color Opaque(int rgb) { return Color.FromArgb(unchecked((int)0xFF000000) | rgb); }

        public static ClockPalette For(ClockTheme theme)
        {
            ClockPalette palette;
            return Themes.TryGetValue(theme, out palette) ? palette : Themes[ClockTheme.Dark];
        }

        public Color Accent(CountdownStage stage)
        {
            if (stage == CountdownStage.Urgent) return Urgent;
            if (stage == CountdownStage.Soon) return Soon;
            return Normal;
        }

        // ColorDialog custom slots take 0x00BBGGRR.
        public static int[] AccentSwatches()
        {
            return Enum.GetValues(typeof(ClockTheme)).Cast<ClockTheme>()
                .Select(theme => For(theme).Normal)
                .Select(color => color.R | color.G << 8 | color.B << 16)
                .ToArray();
        }
    }

    // Draws inside x 6..28, y 2..24 of the 235x34 strip, where the clock icon sits.
    internal static class MascotPainter
    {
        private static readonly Color Sweat = Color.FromArgb(91, 184, 245);

        public static void Draw(Graphics g, ClockPalette palette, CountdownStage stage)
        {
            using (var fur = new SolidBrush(palette.Fur))
            using (var detail = new SolidBrush(palette.Detail))
            using (var feature = new SolidBrush(palette.Feature))
            using (var outline = palette.Outline == palette.Fur ? null : new Pen(palette.Outline, 1))
            {
                switch (palette.Mascot)
                {
                    case Mascot.Cat:
                        Polygon(g, fur, outline, P(9.6f, 12), P(10.8f, 4.2f), P(15.2f, 8.6f));
                        Polygon(g, fur, outline, P(23.4f, 12), P(22.2f, 4.2f), P(17.8f, 8.6f));
                        g.FillPolygon(detail, new[] { P(11.4f, 9.6f), P(11.9f, 6.4f), P(14, 8.6f) });
                        g.FillPolygon(detail, new[] { P(21.6f, 9.6f), P(21.1f, 6.4f), P(19, 8.6f) });
                        Ellipse(g, fur, outline, 9, 7.5f, 15, 13.5f);
                        Face(g, palette, stage, 13.5f, 19.5f, 13.6f);
                        g.FillPolygon(feature, new[] { P(15.6f, 15.9f), P(17.4f, 15.9f), P(16.5f, 17) });
                        break;
                    case Mascot.Bear:
                        Ellipse(g, fur, outline, 9.2f, 6.2f, 5.6f, 5.6f);
                        Ellipse(g, fur, outline, 18.2f, 6.2f, 5.6f, 5.6f);
                        g.FillEllipse(detail, 10.6f, 7.6f, 2.8f, 2.8f);
                        g.FillEllipse(detail, 19.6f, 7.6f, 2.8f, 2.8f);
                        Ellipse(g, fur, outline, 9, 8, 15, 13.5f);
                        g.FillEllipse(detail, 13.2f, 14.4f, 6.6f, 5.2f);
                        Face(g, palette, stage, 13.1f, 19.9f, 12.9f);
                        g.FillEllipse(feature, 15.3f, 14.9f, 2.4f, 1.8f);
                        break;
                    case Mascot.Rabbit:
                        Ellipse(g, fur, outline, 11.3f, 2.7f, 3.8f, 10);
                        Ellipse(g, fur, outline, 17.9f, 2.7f, 3.8f, 10);
                        g.FillEllipse(detail, 12.3f, 4.2f, 1.8f, 6.8f);
                        g.FillEllipse(detail, 18.9f, 4.2f, 1.8f, 6.8f);
                        Ellipse(g, fur, outline, 9.5f, 9.2f, 14, 12.8f);
                        Face(g, palette, stage, 13.7f, 19.3f, 14.6f);
                        g.FillEllipse(feature, 15.7f, 16.6f, 1.6f, 1.3f);
                        break;
                    case Mascot.Penguin:
                        Ellipse(g, fur, outline, 9, 6.5f, 15, 15.5f);
                        g.FillEllipse(detail, 10.9f, 9.6f, 11.2f, 11.2f);
                        Face(g, palette, stage, 14, 19, 13.6f);
                        g.FillPolygon(feature, new[] { P(15.1f, 15.3f), P(17.9f, 15.3f), P(16.5f, 17.6f) });
                        break;
                    case Mascot.Chick:
                        using (var tuft = new Pen(palette.Outline, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(tuft, 16, 8.2f, 15, 5.2f);
                            g.DrawLine(tuft, 17.2f, 8.2f, 18.6f, 5.6f);
                        }
                        Ellipse(g, fur, outline, 9, 7.5f, 15, 14.5f);
                        Face(g, palette, stage, 13.6f, 19.4f, 13.4f);
                        g.FillPolygon(feature, new[] { P(15, 15.2f), P(18, 15.2f), P(16.5f, 17.6f) });
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("palette");
                }
            }

            if (stage != CountdownStage.Urgent) return;
            using (var sweat = new SolidBrush(Sweat))
            {
                g.FillPolygon(sweat, new[] { P(24.3f, 10.6f), P(26.9f, 10.6f), P(25.6f, 7.4f) });
                g.FillEllipse(sweat, 24.1f, 9.4f, 3, 3.3f);
            }
        }

        // Cheeks blush from 30 minutes out, and the eyes close into a smile once the shift is over.
        private static void Face(Graphics g, ClockPalette palette, CountdownStage stage, float left, float right, float y)
        {
            if (stage != CountdownStage.Running)
            {
                using (var blush = new SolidBrush(palette.Blush))
                {
                    g.FillEllipse(blush, left - 2.6f, y + 1.5f, 2.8f, 1.7f);
                    g.FillEllipse(blush, right - 0.2f, y + 1.5f, 2.8f, 1.7f);
                }
            }

            if (stage == CountdownStage.Finished)
            {
                using (var eyes = new Pen(palette.Eyes, 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(eyes, left - 1.5f, y - 0.9f, 3, 2.8f, 200, 140);
                    g.DrawArc(eyes, right - 1.5f, y - 0.9f, 3, 2.8f, 200, 140);
                }
                return;
            }

            using (var eyes = new SolidBrush(palette.Eyes))
            {
                g.FillEllipse(eyes, left - 1.1f, y - 1.1f, 2.2f, 2.2f);
                g.FillEllipse(eyes, right - 1.1f, y - 1.1f, 2.2f, 2.2f);
            }
        }

        private static void Ellipse(Graphics g, Brush fill, Pen outline, float x, float y, float width, float height)
        {
            g.FillEllipse(fill, x, y, width, height);
            if (outline != null) g.DrawEllipse(outline, x, y, width, height);
        }

        private static void Polygon(Graphics g, Brush fill, Pen outline, params PointF[] points)
        {
            g.FillPolygon(fill, points);
            if (outline != null) g.DrawPolygon(outline, points);
        }

        private static PointF P(float x, float y) { return new PointF(x, y); }
    }
}
