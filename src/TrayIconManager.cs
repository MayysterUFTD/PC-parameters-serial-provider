using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HardwareMonitorTray
{
    /// <summary>
    /// Manages dynamic tray icons with temperature/status display
    /// </summary>
    public class TrayIconManager : IDisposable
    {
        private readonly int _iconSize = 16;

        public enum IconState
        {
            Stopped,
            Running,
            Warning,
            Error,
            Hot
        }

        /// <summary>
        /// Creates a status icon with colored background
        /// </summary>
        public Icon CreateStatusIcon(IconState state)
        {
            using var bitmap = new Bitmap(_iconSize, _iconSize);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Background color based on state
            Color bgColor = state switch
            {
                IconState.Stopped => Color.FromArgb(100, 100, 100),  // Gray
                IconState.Running => Color.FromArgb(46, 204, 113),   // Green
                IconState.Warning => Color.FromArgb(241, 196, 15),   // Yellow
                IconState.Error => Color.FromArgb(231, 76, 60),      // Red
                IconState.Hot => Color.FromArgb(230, 126, 34),       // Orange
                _ => Color.FromArgb(52, 152, 219)                    // Blue
            };

            // Draw rounded rectangle background
            using var bgBrush = new SolidBrush(bgColor);
            using var path = CreateRoundedRectangle(0, 0, _iconSize - 1, _iconSize - 1, 3);
            g.FillPath(bgBrush, path);

            // Draw icon symbol
            using var whitePen = new Pen(Color.White, 1.5f);
            using var whiteBrush = new SolidBrush(Color.White);

            switch (state)
            {
                case IconState.Stopped:
                    // Square (stop symbol)
                    g.FillRectangle(whiteBrush, 5, 5, 6, 6);
                    break;

                case IconState.Running:
                    // Play triangle / pulse
                    DrawPulseIcon(g, whiteBrush);
                    break;

                case IconState.Warning:
                    // Exclamation mark
                    DrawExclamationIcon(g, whiteBrush);
                    break;

                case IconState.Error:
                    // X mark
                    g.DrawLine(whitePen, 4, 4, 11, 11);
                    g.DrawLine(whitePen, 11, 4, 4, 11);
                    break;

                case IconState.Hot:
                    // Flame icon
                    DrawFlameIcon(g, whiteBrush);
                    break;
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        /// <summary>
        /// Creates an icon displaying temperature value
        /// </summary>
        public Icon CreateTemperatureIcon(float temperature)
        {
            using var bitmap = new Bitmap(_iconSize, _iconSize);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Color based on temperature
            Color bgColor;
            if (temperature < 50)
                bgColor = Color.FromArgb(46, 204, 113);      // Green - cool
            else if (temperature < 70)
                bgColor = Color.FromArgb(241, 196, 15);      // Yellow - warm
            else if (temperature < 85)
                bgColor = Color.FromArgb(230, 126, 34);      // Orange - hot
            else
                bgColor = Color.FromArgb(231, 76, 60);       // Red - critical

            // Background
            using var bgBrush = new SolidBrush(bgColor);
            using var path = CreateRoundedRectangle(0, 0, _iconSize - 1, _iconSize - 1, 3);
            g.FillPath(bgBrush, path);

            // Temperature text
            string tempText = temperature >= 100 ? "!" : ((int)temperature).ToString();

            using var font = new Font("Segoe UI", temperature >= 100 ? 9f : 7f, FontStyle.Bold);
            using var whiteBrush = new SolidBrush(Color.White);

            var textSize = g.MeasureString(tempText, font);
            float x = (_iconSize - textSize.Width) / 2;
            float y = (_iconSize - textSize.Height) / 2;

            g.DrawString(tempText, font, whiteBrush, x, y);

            return Icon.FromHandle(bitmap.GetHicon());
        }

        /// <summary>
        /// Creates an icon with CPU/GPU load bar
        /// </summary>
        public Icon CreateLoadIcon(float cpuLoad, float gpuLoad)
        {
            using var bitmap = new Bitmap(_iconSize, _iconSize);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Dark background
            using var bgBrush = new SolidBrush(Color.FromArgb(44, 62, 80));
            using var path = CreateRoundedRectangle(0, 0, _iconSize - 1, _iconSize - 1, 3);
            g.FillPath(bgBrush, path);

            // CPU bar (left)
            DrawVerticalBar(g, 2, cpuLoad, Color.FromArgb(52, 152, 219));  // Blue

            // GPU bar (right)
            DrawVerticalBar(g, 9, gpuLoad, Color.FromArgb(155, 89, 182)); // Purple

            return Icon.FromHandle(bitmap.GetHicon());
        }

        /// <summary>
        /// Creates a modern hardware monitor icon
        /// </summary>
        public Icon CreateModernIcon(bool isRunning, float? cpuTemp = null)
        {
            using var bitmap = new Bitmap(_iconSize, _iconSize);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Gradient background
            Color color1, color2;
            if (!isRunning)
            {
                color1 = Color.FromArgb(100, 100, 100);
                color2 = Color.FromArgb(60, 60, 60);
            }
            else if (cpuTemp.HasValue)
            {
                if (cpuTemp.Value < 60)
                {
                    color1 = Color.FromArgb(46, 204, 113);
                    color2 = Color.FromArgb(39, 174, 96);
                }
                else if (cpuTemp.Value < 80)
                {
                    color1 = Color.FromArgb(241, 196, 15);
                    color2 = Color.FromArgb(243, 156, 18);
                }
                else
                {
                    color1 = Color.FromArgb(231, 76, 60);
                    color2 = Color.FromArgb(192, 57, 43);
                }
            }
            else
            {
                color1 = Color.FromArgb(52, 152, 219);
                color2 = Color.FromArgb(41, 128, 185);
            }

            using var gradientBrush = new LinearGradientBrush(
                new Rectangle(0, 0, _iconSize, _iconSize),
                color1, color2,
                LinearGradientMode.ForwardDiagonal);

            using var path = CreateRoundedRectangle(0, 0, _iconSize - 1, _iconSize - 1, 3);
            g.FillPath(gradientBrush, path);

            // Draw chip/CPU symbol
            using var whitePen = new Pen(Color.White, 1f);
            using var whiteBrush = new SolidBrush(Color.White);

            // Center square (chip)
            g.FillRectangle(whiteBrush, 5, 5, 6, 6);

            // Pins
            g.DrawLine(whitePen, 3, 6, 3, 6);
            g.DrawLine(whitePen, 3, 9, 3, 9);
            g.DrawLine(whitePen, 12, 6, 12, 6);
            g.DrawLine(whitePen, 12, 9, 12, 9);
            g.DrawLine(whitePen, 6, 3, 6, 3);
            g.DrawLine(whitePen, 9, 3, 9, 3);
            g.DrawLine(whitePen, 6, 12, 6, 12);
            g.DrawLine(whitePen, 9, 12, 9, 12);

            return Icon.FromHandle(bitmap.GetHicon());
        }

        /// <summary>
        /// Creates animated pulse icon (for active transmission)
        /// </summary>
        public Icon CreatePulseIcon(int frame)
        {
            using var bitmap = new Bitmap(_iconSize, _iconSize);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background
            using var bgBrush = new SolidBrush(Color.FromArgb(52, 152, 219));
            using var path = CreateRoundedRectangle(0, 0, _iconSize - 1, _iconSize - 1, 3);
            g.FillPath(bgBrush, path);

            // Animated pulse dots
            using var whiteBrush = new SolidBrush(Color.White);
            using var dimBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));

            int[] dotX = { 3, 7, 11 };
            for (int i = 0; i < 3; i++)
            {
                var brush = (i == frame % 3) ? whiteBrush : dimBrush;
                int size = (i == frame % 3) ? 3 : 2;
                int y = (i == frame % 3) ? 6 : 7;
                g.FillEllipse(brush, dotX[i], y, size, size);
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        private void DrawPulseIcon(Graphics g, Brush brush)
        {
            // Simple pulse/heartbeat line
            using var pen = new Pen(Color.White, 1.5f);

            Point[] points = {
                new Point(2, 8),
                new Point(5, 8),
                new Point(6, 4),
                new Point(8, 12),
                new Point(10, 6),
                new Point(11, 8),
                new Point(14, 8)
            };

            g.DrawLines(pen, points);
        }

        private void DrawExclamationIcon(Graphics g, Brush brush)
        {
            // Exclamation mark
            g.FillRectangle(brush, 7, 3, 2, 6);
            g.FillEllipse(brush, 7, 11, 2, 2);
        }

        private void DrawFlameIcon(Graphics g, Brush brush)
        {
            // Simple flame shape
            Point[] flame = {
                new Point(8, 2),
                new Point(11, 6),
                new Point(10, 8),
                new Point(12, 10),
                new Point(11, 13),
                new Point(8, 11),
                new Point(5, 13),
                new Point(4, 10),
                new Point(6, 8),
                new Point(5, 6)
            };

            g.FillPolygon(brush, flame);
        }

        private void DrawVerticalBar(Graphics g, int x, float percentage, Color color)
        {
            int barHeight = (int)(12 * (percentage / 100f));
            int barY = 14 - barHeight;

            using var barBrush = new SolidBrush(color);
            g.FillRectangle(barBrush, x, barY, 5, barHeight);

            // Border
            using var borderPen = new Pen(Color.FromArgb(100, 255, 255, 255), 0.5f);
            g.DrawRectangle(borderPen, x, 2, 5, 12);
        }

        private GraphicsPath CreateRoundedRectangle(int x, int y, int width, int height, int radius)
        {
            var path = new GraphicsPath();

            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();

            return path;
        }

        public void Dispose()
        {
            // Icons are disposed by the caller
        }
    }
}