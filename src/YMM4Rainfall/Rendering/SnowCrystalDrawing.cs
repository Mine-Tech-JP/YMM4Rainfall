// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using Vortice.Direct2D1;
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall.Rendering;

/// <summary>六角形・主枝・側枝から、採用済みの6種類の結晶図案を描きます。</summary>
internal static class SnowCrystalDrawing
{
    public const int StyleCount = (int)SnowCrystalStyle.HexagonalPlate;

    public static void Draw(ID2D1DeviceContext context, Vector2 center, int variant, ID2D1SolidColorBrush brush)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(brush);
        var d = new Drawing(context, center, brush);
        var opacity = brush.Opacity;
        try
        {
            switch ((variant % StyleCount + StyleCount) % StyleCount)
            {
                case 0: Simple(d); break;
                case 1: Dendrite(d, false); break;
                case 2: Dendrite(d, true); break;
                case 3: BroadStar(d); break;
                case 4: Fans(d); break;
                default: Plate(d); break;
            }
        }
        finally { brush.Opacity = opacity; }
    }

    private static void Simple(Drawing d)
    {
        d.Polygon(Hexagon(7), 0x33 / 255f, 1.2f);
        for (var arm = 0; arm < 6; arm++)
            d.Polygon(Rotate([new(5, -1.5f), new(36, -1), new(46, 0), new(36, 1), new(5, 1.5f)], arm), 1, 0.4f);
    }

    private static void Dendrite(Drawing d, bool fine)
    {
        d.Polygon(Hexagon(fine ? 6 : 8), 0x33 / 255f, 1);
        ReadOnlySpan<(float Position, float Length)> branches = fine
            ? [(10, 9), (15, 13), (20, 17), (25, 16), (30, 13), (35, 10), (40, 6)]
            : [(13, 14), (23, 17), (33, 11), (40, 5)];
        ReadOnlySpan<float> factors = [1, 0.97f, 1.02f, 0.98f, 1, 0.96f];
        for (var arm = 0; arm < 6; arm++)
        {
            var factor = fine ? factors[arm] : 1;
            d.ArmLine(arm, new(5, 0), new(46 * factor, 0), fine ? 1.4f : 2);
            foreach (var branch in branches)
            {
                var length = branch.Length * factor;
                for (var side = -1; side <= 1; side += 2)
                {
                    // 側枝は主枝の先端側へ60度で伸ばします。
                    d.ArmLine(arm, new(branch.Position, 0),
                        new(branch.Position + length / 2, side * length * MathF.Sqrt(3) / 2), fine ? 1 : 1.45f);
                    if (fine && length >= 13)
                    {
                        var start = new Vector2(branch.Position + length * 0.25f, side * length * 0.433f);
                        d.ArmLine(arm, start, start + new Vector2(4, 0), 0.8f);
                    }
                }
            }
        }
    }

    private static void BroadStar(Drawing d)
    {
        // 比較案4B。六つの腕を単一の輪郭でつなぎます。
        ReadOnlySpan<Vector2> sector = [new(13, -7.506f), new(23, -7.506f), new(26, -12.702f),
            new(35, -12.702f), new(39, -5.774f), new(49, 0), new(39, 5.774f),
            new(35, 12.702f), new(26, 12.702f), new(23, 7.506f), new(13, 7.506f)];
        var outline = new Vector2[sector.Length * 6];
        for (var arm = 0; arm < 6; arm++) Rotate(sector, arm).CopyTo(outline, arm * sector.Length);
        d.Polygon(outline, 0x26 / 255f, 1.15f);
        d.Polygon(Hexagon(8), 0x18 / 255f, 0.8f);
        for (var arm = 0; arm < 6; arm++)
        {
            d.ArmLine(arm, new(9, 0), new(45, 0), 1, 0.8f);
            d.ArmLine(arm, new(24, 0), new(31, 10.5f), 0.7f, 0.55f);
            d.ArmLine(arm, new(24, 0), new(31, -10.5f), 0.7f, 0.55f);
        }
    }

    private static void Fans(Drawing d)
    {
        // 比較案5B。中心から板を広げ、独立した細い軸は設けません。
        d.Polygon(Hexagon(9), 0x22 / 255f, 0.9f);
        for (var arm = 0; arm < 6; arm++)
        {
            d.Polygon(Rotate([new(7, -2), new(30, -15.3f), new(43, -7.8f),
                new(43, 7.8f), new(30, 15.3f), new(7, 2)], arm), 0x18 / 255f, 1.15f);
            d.ArmLine(arm, new(9, 0), new(42, 0), 1.1f, 0.85f);
            d.ArmLine(arm, new(19, 0), new(34, 10.4f), 0.7f, 0.65f);
            d.ArmLine(arm, new(19, 0), new(34, -10.4f), 0.7f, 0.65f);
        }
    }

    private static void Plate(Drawing d)
    {
        d.Polygon(Hexagon(44, MathF.PI / 6), 0x15 / 255f, 1.8f);
        d.Polygon(Hexagon(39, MathF.PI / 6), 0, 0.65f, 0.7f);
        d.Polygon(Hexagon(23, MathF.PI / 6), 0, 1, 0.8f);
        d.Polygon(Hexagon(17, MathF.PI / 6), 0x18 / 255f, 0.65f, 0.6f);
        for (var arm = 0; arm < 6; arm++) d.ArmLine(arm, new(0, 24), new(0, 36), 0.7f, 0.45f);
    }

    private static Vector2[] Hexagon(float radius, float rotation = 0)
        => Enumerable.Range(0, 6).Select(i => new Vector2(
            MathF.Cos(rotation + i * MathF.PI / 3) * radius,
            MathF.Sin(rotation + i * MathF.PI / 3) * radius)).ToArray();

    private static Vector2 Rotate(Vector2 p, int arm)
    {
        var angle = arm * MathF.PI / 3;
        return new(p.X * MathF.Cos(angle) - p.Y * MathF.Sin(angle),
            p.X * MathF.Sin(angle) + p.Y * MathF.Cos(angle));
    }

    private static Vector2[] Rotate(ReadOnlySpan<Vector2> points, int arm)
    {
        var result = new Vector2[points.Length];
        for (var i = 0; i < points.Length; i++) result[i] = Rotate(points[i], arm);
        return result;
    }

    // テクスチャ生成時だけ使い、呼び出し元の座標変換とブラシ色は変更しません。
    private sealed class Drawing(ID2D1DeviceContext context, Vector2 center, ID2D1SolidColorBrush brush)
    {
        private readonly float initialOpacity = brush.Opacity;
        public void ArmLine(int arm, Vector2 start, Vector2 end, float width, float opacity = 1)
        {
            brush.Opacity = initialOpacity * opacity;
            context.DrawLine(center + Rotate(start, arm), center + Rotate(end, arm), brush, width);
        }

        public void Polygon(ReadOnlySpan<Vector2> points, float fill, float width, float opacity = 1)
        {
            using var factory = context.Factory;
            using var geometry = factory.CreatePathGeometry();
            using (var sink = geometry.Open())
            {
                sink.SetFillMode(FillMode.Winding);
                sink.BeginFigure(center + points[0], FigureBegin.Filled);
                for (var i = 1; i < points.Length; i++) sink.AddLine(center + points[i]);
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }
            if (fill > 0)
            {
                brush.Opacity = initialOpacity * fill * opacity;
                context.FillGeometry(geometry, brush);
            }
            if (width > 0)
            {
                brush.Opacity = initialOpacity * opacity;
                context.DrawGeometry(geometry, brush, width);
            }
        }
    }
}
