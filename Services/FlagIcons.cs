using System;
using System.Windows;
using System.Windows.Media;

namespace dlss_updater.Services
{
    internal static class FlagIcons
    {
        private const double Width = 30;
        private const double Height = 20;

        public static ImageSource Create(string code)
        {
            DrawingGroup flag = new DrawingGroup();

            switch (code)
            {
                case "en":
                    DrawUnitedKingdom(flag);
                    break;
                case "de":
                    Horizontal(flag, "#000000", "#DD0000", "#FFCE00");
                    break;
                case "fr":
                    Vertical(flag, "#002395", "#FFFFFF", "#ED2939");
                    break;
                case "es":
                    Fill(flag, "#AA151B", 0, 0, Width, Height);
                    Fill(flag, "#F1BF00", 0, Height / 4, Width, Height / 2);
                    break;
                case "it":
                    Vertical(flag, "#009246", "#FFFFFF", "#CE2B37");
                    break;
                case "pl":
                    Horizontal(flag, "#FFFFFF", "#DC143C");
                    break;
                case "uk":
                    Horizontal(flag, "#0057B7", "#FFD700");
                    break;
                case "zh":
                    Fill(flag, "#EE1C25", 0, 0, Width, Height);
                    Shape(flag, "#FFFF00", Star(5, 5, 3));
                    Shape(flag, "#FFFF00", Star(10, 2, 1));
                    Shape(flag, "#FFFF00", Star(12, 4, 1));
                    Shape(flag, "#FFFF00", Star(12, 7, 1));
                    Shape(flag, "#FFFF00", Star(10, 9, 1));
                    break;
                case "ru":
                    Horizontal(flag, "#FFFFFF", "#0039A6", "#D52B1E");
                    break;
                case "ja":
                    Fill(flag, "#FFFFFF", 0, 0, Width, Height);
                    Shape(flag, "#BC002D", new EllipseGeometry(new Point(15, 10), 6, 6));
                    break;
                case "ko":
                    DrawKorea(flag);
                    break;
                case "cs":
                    Horizontal(flag, "#FFFFFF", "#D7141A");
                    Shape(flag, "#11457E", Polygon(
                        new Point(0, 0),
                        new Point(15, 10),
                        new Point(0, 20)));
                    break;
                case "nl":
                    Horizontal(flag, "#AE1C28", "#FFFFFF", "#21468B");
                    break;
                case "pt":
                    Fill(flag, "#006600", 0, 0, 12, Height);
                    Fill(flag, "#FF0000", 12, 0, Width - 12, Height);
                    Shape(flag, "#FFCC00", new EllipseGeometry(new Point(12, 10), 4.5, 4.5));
                    break;
                case "id":
                    Horizontal(flag, "#CE1126", "#FFFFFF");
                    break;
                case "th":
                    Horizontal(flag, "#A51931", "#F4F5F8", "#2D2A4A", "#2D2A4A", "#F4F5F8", "#A51931");
                    break;
                case "tr":
                    Fill(flag, "#E30A17", 0, 0, Width, Height);
                    Shape(flag, "#FFFFFF", new EllipseGeometry(new Point(10, 10), 5, 5));
                    Shape(flag, "#E30A17", new EllipseGeometry(new Point(11.25, 10), 4, 4));
                    Shape(flag, "#FFFFFF", Star(17.4, 10, 2.5, 5, 0.382, Math.PI));
                    break;
                case "vi":
                    Fill(flag, "#DA251D", 0, 0, Width, Height);
                    Shape(flag, "#FFFF00", Star(15, 10.5, 6));
                    break;
                case "pt-BR":
                    Fill(flag, "#009C3B", 0, 0, Width, Height);
                    Shape(flag, "#FFDF00", Polygon(
                        new Point(2.5, 10),
                        new Point(15, 1.7),
                        new Point(27.5, 10),
                        new Point(15, 18.3)));
                    Shape(flag, "#002776", new EllipseGeometry(new Point(15, 10), 5, 5));
                    break;
                case "zh-Hant":
                    Fill(flag, "#FE0000", 0, 0, Width, Height);
                    Fill(flag, "#000095", 0, 0, 15, 10);
                    Shape(flag, "#FFFFFF", Star(7.5, 5, 3.75, 12, 0.55));
                    Shape(flag, "#000095", new EllipseGeometry(new Point(7.5, 5), 2.1, 2.1));
                    Shape(flag, "#FFFFFF", new EllipseGeometry(new Point(7.5, 5), 1.8, 1.8));
                    break;
                default:
                    Fill(flag, "#596359", 0, 0, Width, Height);
                    break;
            }

            flag.ClipGeometry = new RectangleGeometry(new Rect(0, 0, Width, Height));
            DrawingImage image = new DrawingImage(flag);
            image.Freeze();
            return image;
        }

        private static void DrawUnitedKingdom(DrawingGroup flag)
        {
            Fill(flag, "#012169", 0, 0, Width, Height);
            Line(flag, "#FFFFFF", 4, new Point(0, 0), new Point(Width, Height));
            Line(flag, "#FFFFFF", 4, new Point(0, Height), new Point(Width, 0));
            Line(flag, "#C8102E", 1.4, new Point(0, 0), new Point(Width, Height));
            Line(flag, "#C8102E", 1.4, new Point(0, Height), new Point(Width, 0));
            Fill(flag, "#FFFFFF", 11.7, 0, 6.6, Height);
            Fill(flag, "#FFFFFF", 0, 6.7, Width, 6.6);
            Fill(flag, "#C8102E", 13, 0, 4, Height);
            Fill(flag, "#C8102E", 0, 8, Width, 4);
        }

        private static void DrawKorea(DrawingGroup flag)
        {
            Fill(flag, "#FFFFFF", 0, 0, Width, Height);

            Point center = new Point(15, 10);
            const double radius = 5;
            Shape(flag, "#CD2E3A", HalfCircle(center, radius, true));
            Shape(flag, "#0047A0", HalfCircle(center, radius, false));

            foreach (Point corner in new[]
            {
                new Point(5, 3.5),
                new Point(25, 3.5),
                new Point(5, 16.5),
                new Point(25, 16.5)
            })
            {
                double angle = (corner.X < 15) == (corner.Y < 10) ? 34 : -34;
                RectangleGeometry bar = new RectangleGeometry(
                    new Rect(corner.X - 2.5, corner.Y - 1.5, 5, 3),
                    0,
                    0,
                    new RotateTransform(angle, corner.X, corner.Y));
                Shape(flag, "#000000", bar);
            }
        }

        private static void Horizontal(DrawingGroup flag, params string[] colors)
        {
            double stripe = Height / colors.Length;
            for (int i = 0; i < colors.Length; i++)
            {
                Fill(flag, colors[i], 0, i * stripe, Width, stripe);
            }
        }

        private static void Vertical(DrawingGroup flag, params string[] colors)
        {
            double stripe = Width / colors.Length;
            for (int i = 0; i < colors.Length; i++)
            {
                Fill(flag, colors[i], i * stripe, 0, stripe, Height);
            }
        }

        private static void Fill(
            DrawingGroup flag,
            string color,
            double x,
            double y,
            double width,
            double height)
        {
            Shape(flag, color, new RectangleGeometry(new Rect(x, y, width, height)));
        }

        private static void Line(
            DrawingGroup flag,
            string color,
            double thickness,
            Point start,
            Point end)
        {
            flag.Children.Add(new GeometryDrawing(
                null,
                new Pen(Paint(color), thickness),
                new LineGeometry(start, end)));
        }

        private static void Shape(
            DrawingGroup flag,
            string color,
            Geometry geometry)
        {
            flag.Children.Add(new GeometryDrawing(Paint(color), null, geometry));
        }

        // A star with its first point at the given angle (straight up by
        // default). The inner corners sit at inner x radius.
        private static Geometry Star(
            double centerX,
            double centerY,
            double radius,
            int tips = 5,
            double inner = 0.382,
            double rotation = -Math.PI / 2)
        {
            Point[] points = new Point[tips * 2];
            for (int i = 0; i < points.Length; i++)
            {
                double length = i % 2 == 0 ? radius : radius * inner;
                double angle = rotation + i * Math.PI / tips;
                points[i] = new Point(
                    centerX + length * Math.Cos(angle),
                    centerY + length * Math.Sin(angle));
            }

            return Polygon(points);
        }

        private static Geometry HalfCircle(Point center, double radius, bool top)
        {
            PathFigure figure = new PathFigure
            {
                StartPoint = new Point(center.X - radius, center.Y),
                IsClosed = true
            };
            figure.Segments.Add(new ArcSegment(
                new Point(center.X + radius, center.Y),
                new Size(radius, radius),
                0,
                false,
                top ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                true));

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static Geometry Polygon(params Point[] points)
        {
            PathFigure figure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = true
            };
            for (int i = 1; i < points.Length; i++)
            {
                figure.Segments.Add(new LineSegment(points[i], true));
            }

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static Brush Paint(string color)
        {
            SolidColorBrush brush =
                (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            return brush;
        }
    }
}
