// SPDX-License-Identifier: MPL-2.0
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Rendering;
using YMM4Rainfall.Simulation;
using PixelFormat = Vortice.DCommon.PixelFormat;
using BitmapSource = System.Windows.Media.Imaging.BitmapSource;

namespace YMM4Rainfall.Verification;

internal static class MotionBlurGraphicsChecks
{
    private const int Width = 320, Height = 240;
    private static readonly Vector2 Center = new(160, 120);
    private static readonly string OutputDirectory = Path.GetFullPath("tmp/motion-blur-alpha7-graphics");

    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        Directory.CreateDirectory(OutputDirectory);
        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
            [Vortice.Direct3D.FeatureLevel.Level_11_0], out var createdDevice).CheckError();
        using var d3d = createdDevice ?? throw new InvalidOperationException("ブラー検証用デバイスを作成できませんでした。");
        using var dxgi = d3d.QueryInterface<IDXGIDevice>();
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using var device = factory.CreateDevice(dxgi);
        using var context = device.CreateDeviceContext(DeviceContextOptions.None);
        using var input = Bitmap(context, Width, Height, BitmapOptions.Target);
        using var target = Bitmap(context, Width, Height, BitmapOptions.Target);
        using var renderer = RainfallRenderer.Create(context);
        Fill(context, input, new(0, 0, 0, 0));
        renderer.SetInput(input);
        var p = RainfallParameters.Default with { Shape = RainfallShape.Circle, MotionBlurEnabled = true, MotionBlurStrength = 100 };
        var particle = new RainfallStroke(Center - new Vector2(0, 24), Center, 2, 0.8f, 24)
        { BlurVelocity = new(3600, 0) };
        byte[] Render(RainfallParameters parameters, RainfallStroke[]? strokes = null, string png = "")
        {
            renderer.Render(parameters, 1, pngPath: png, frameFactory: _ => strokes ?? [particle]);
            Draw(context, target, renderer.Output);
            return Read(context, target);
        }

        check("後方ブラー: 先頭を越えず逆方向へ尾を引き、色とアルファを保つ", () =>
        {
            var colored = p with { MotionBlurMode = MotionBlurMode.Trailing, Red = 0.3, Green = 0.6, Blue = 1 };
            var baseline = Render(colored with { MotionBlurEnabled = false });
            foreach (var velocity in new[] { new Vector2(3600, 0), new(-3600, 0), new(0, 3600), new(0, -3600), new(2545, 2545), new(-2545, -2545) })
            {
                var pixels = Render(colored, [particle with { BlurVelocity = velocity }]);
                var sum = Moments(pixels).Sum;
                Console.WriteLine($"後方ブラー診断 {velocity}: alpha={sum:F0}/{Moments(baseline).Sum:F0}");
                Save($"trailing-{velocity.X}-{velocity.Y}.png", pixels);
                require(sum / Moments(baseline).Sum is > 0.88 and < 1.06);
                var direction = Vector2.Normalize(velocity);
                double ahead = 0, behind = 0, green = 0;
                for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
                {
                    var i = (y * Width + x) * 4;
                    var distance = Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - Center, direction);
                    if (distance > 14) ahead += pixels[i + 3];
                    if (distance < -14) behind += pixels[i + 3];
                    green += pixels[i + 1];
                }
                Console.WriteLine($"後方ブラー診断: 前方={ahead:F0} 後方={behind:F0} G/A={green / sum:F3}");
                require(ahead < 50 && behind > 1000);
                require(Math.Abs(green / sum - 0.6) < 0.025);
                var tip = Center + direction * 8;
                var tipIndex = ((int)tip.Y * Width + (int)tip.X) * 4 + 3;
                require(pixels[tipIndex] >= baseline[tipIndex] * 0.30);
                if (velocity.Y == 0 && velocity.X > 0)
                    require(pixels[(120 * Width + 130) * 4 + 3] > pixels[(120 * Width + 110) * 4 + 3]);
            }
        });

        check("ブラー描画: OFF・0%は従来画素と一致、背景とホスト状態を保持", () =>
        {
            var originalInput = Read(context, input);
            foreach (var shape in Enum.GetValues<RainfallShape>().Where(s => s != RainfallShape.Png))
            {
                var off = Render(p with { Shape = shape, MotionBlurEnabled = false });
                require(off.SequenceEqual(Render(p with { Shape = shape, MotionBlurStrength = 0 })));
                require(off.SequenceEqual(Render(p with { Shape = shape }, [particle with { BlurVelocity = default }])));
            }
            context.Target = target;
            context.Transform = Matrix3x2.CreateTranslation(3, 7);
            context.AntialiasMode = AntialiasMode.Aliased;
            try
            {
                renderer.Render(p, 0, frameFactory: _ => [particle]);
                using var retained = context.Target;
                require(retained!.NativePointer == target.NativePointer && context.Transform == Matrix3x2.CreateTranslation(3, 7));
                require(context.AntialiasMode == AntialiasMode.Aliased);
                require(Read(context, input).SequenceEqual(originalInput));
            }
            finally { context.Target = null; context.Transform = Matrix3x2.Identity; context.AntialiasMode = AntialiasMode.PerPrimitive; }
        });

        check("ブラー描画: 水平・垂直・斜めの方向、速度比例とアルファ総量", () =>
        {
            var baseline = Render(p with { MotionBlurEnabled = false });
            var normal = Moments(baseline);
            foreach (var velocity in new[] { new Vector2(3600, 0), new(0, 3600), new(2545, 2545), new(2545, -2545) })
            {
                var pixels = Render(p, [particle with { BlurVelocity = velocity }]);
                var m = Moments(pixels);
                require(m.Sum / normal.Sum is > 0.90 and < 1.05);
                if (velocity.Y == 0) require(m.XX > normal.XX + 200 && Math.Abs(m.YY - normal.YY) < 2);
                else if (velocity.X == 0) require(m.YY > normal.YY + 200 && Math.Abs(m.XX - normal.XX) < 2);
                else require(Math.Sign(m.XY) == Math.Sign(velocity.X * velocity.Y) && Math.Abs(m.XY) > 100);
            }
            var half = Moments(Render(p with { MotionBlurStrength = 12.5 }));
            var full = Moments(Render(p));
            require(full.XX > half.XX && half.XX > normal.XX);
            Save("circle-horizontal.png", Render(p));
            Save("circle-diagonal.png", Render(p, [particle with { BlurVelocity = new(2545, 2545) }]));
        });

        check("ブラー描画: 粒ごとの異なる方向と強度が混ざらず、再描画が一致", () =>
        {
            var first = particle with { Head = new(80, 70), Tail = new(80, 70), BlurVelocity = new(1800, 0) };
            var second = particle with { Head = new(240, 170), Tail = new(240, 170), BlurVelocity = new(0, 3600) };
            foreach (var mode in Enum.GetValues<MotionBlurMode>())
            {
                var parameters = p with { MotionBlurMode = mode };
                var together = Render(parameters, [first, second]);
                var one = Render(parameters, [first]); var two = Render(parameters, [second]);
                for (var i = 0; i < together.Length; i++) require(Math.Abs(together[i] - one[i] - two[i]) <= 1);
                require(together.SequenceEqual(Render(parameters, [first, second])));
            }
        });

        check("ブラー描画: 全形状・PNG・水泡Bの映り込みと背景保護", () =>
        {
            var sourcePixels = new byte[32 * 24 * 4];
            for (var y = 4; y < 20; y++)
            for (var x = 5; x < 27; x++)
            {
                var i = (y * 32 + x) * 4;
                sourcePixels[i] = 40; sourcePixels[i + 1] = 120; sourcePixels[i + 2] = 230; sourcePixels[i + 3] = 255;
            }
            var pngPath = Path.Combine(OutputDirectory, "sample.png");
            SavePng(pngPath, sourcePixels, 32, 24);
            var samples = new List<(string Label, byte[] Pixels)>();
            foreach (var shape in Enum.GetValues<RainfallShape>())
            {
                var parameters = p with { Shape = shape, LensReflection = 100, SnowSoftness = 50,
                    PngSourceSize = shape == RainfallShape.Png ? 32 : 0, PngScale = 200, PngMaximumScale = 200 };
                var stroke = particle with { Size = shape == RainfallShape.Streak ? 24 : 64, BlurVelocity = new(2000, 1400), Rotation = 25 };
                var off = Render(parameters with { MotionBlurEnabled = false }, [stroke], pngPath);
                var on = Render(parameters, [stroke], pngPath);
                require(Moments(on).Sum > 0 && !on.SequenceEqual(off));
                require(on.SequenceEqual(Render(parameters, [stroke], pngPath)));
                samples.Add(($"{WeatherPresetCatalog.ShapeName(shape)} OFF", off));
                samples.Add(($"{WeatherPresetCatalog.ShapeName(shape)} 前後", on));
                var trailingParameters = parameters with { MotionBlurMode = MotionBlurMode.Trailing };
                var trailingPixels = Render(trailingParameters, [stroke], pngPath);
                require(Moments(trailingPixels).Sum > 0 && !trailingPixels.SequenceEqual(on));
                require(trailingPixels.SequenceEqual(Render(trailingParameters, [stroke], pngPath)));
                require(off.SequenceEqual(Render(trailingParameters with { MotionBlurStrength = 0 }, [stroke], pngPath)));
                samples.Add(($"{WeatherPresetCatalog.ShapeName(shape)} 後方", trailingPixels));
            }
            SaveComparison(samples);
            FillPattern(context, input);
            var original = Read(context, input);
            var lens = Render(p with { Shape = RainfallShape.LensBubble, LensReflection = 100, MotionBlurMode = MotionBlurMode.Trailing }, [particle with { Size = 80 }]);
            require(!lens.SequenceEqual(original));
            require(original.SequenceEqual(Read(context, input)));
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (x < 30 || x >= Width - 30 || y < 30 || y >= Height - 30)
                {
                    var i = (y * Width + x) * 4;
                    require(lens.AsSpan(i, 4).SequenceEqual(original.AsSpan(i, 4)));
                }
            Save("lens-background.png", lens);
            Fill(context, input, new(0, 0, 0, 0));
        });

        check("ブラー描画: 画面外からのぼかし・負座標・入力範囲のクリップ", () =>
        {
            var outside = particle with { Head = new(-8, 120), Tail = new(-8, 120), Size = 8 };
            require(Moments(Render(p, [outside])).Sum > 0);
            using var shift = new AffineTransform2D(context) { TransformMatrix = Matrix3x2.CreateTranslation(-70, -30) };
            shift.SetInput(0, input, true);
            using var shifted = shift.Output;
            renderer.SetInput(shifted);
            var pixels = Render(p, [particle with { Head = new(247, 120), Tail = new(247, 120) }]);
            require(Moments(pixels).Sum > 0);
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (x >= Width - 70 || y >= Height - 30) require(pixels[(y * Width + x) * 4 + 3] == 0);
            renderer.SetInput(input);
            shift.SetInput(0, null, true);
        });

        check("後方ブラー: 微小・最大強度・サブピクセル速度と端で有限な出力", () =>
        {
            foreach (var strength in new[] { 0.00001, 0.1, 1, 50, 100 })
            foreach (var speed in new[] { 0.00001f, 60, 7200 })
            {
                var parameters = p with { MotionBlurMode = MotionBlurMode.Trailing, MotionBlurStrength = strength };
                var pixels = Render(parameters, [particle with { BlurVelocity = new(speed, 0) }]);
                require(Moments(pixels).Sum > 0);
            }
            var outside = particle with { Head = new(Width + 8, 120), Tail = new(Width + 8, 120), Size = 8 };
            require(Moments(Render(p with { MotionBlurMode = MotionBlurMode.Trailing }, [outside])).Sum > 0);
        });

        check("ブラー描画: 作業画像の再利用・サイズ変更・上限粒数・資源再生成", () =>
        {
            var expected = Render(p);
            var count = renderer.MotionBlurSurfaceCreationCount;
            for (var i = 0; i < 20; i++) require(expected.SequenceEqual(Render(p)));
            require(renderer.MotionBlurSurfaceCreationCount == count);
            var many = Enumerable.Range(0, 4000).Select(i => particle with
            {
                Head = new(8 + i % 80 * 3.8f, 8 + i / 80 * 4.4f), Tail = default, Size = 2,
                Opacity = 0.4f, BlurVelocity = new(500, 200),
            }).ToArray();
            foreach (var mode in Enum.GetValues<MotionBlurMode>())
            {
                var timer = Stopwatch.StartNew();
                _ = Render(p with { MotionBlurMode = mode }, many);
                timer.Stop();
                require(renderer.MotionBlurDrawnParticleCount == 4000);
                Console.WriteLine($"ブラー負荷 WARP {mode}: 320x240・4000粒・1フレーム {timer.Elapsed.TotalMilliseconds:F1} ms。実GPUの再生FPSではありません。");
            }
            using var largerInput = Bitmap(context, Width * 2, Height * 2, BitmapOptions.Target);
            Fill(context, largerInput, new(0, 0, 0, 0)); renderer.SetInput(largerInput);
            _ = Render(p);
            require(renderer.MotionBlurSurfaceCreationCount == count + 1);
            renderer.SetInput(input); require(expected.SequenceEqual(Render(p)));
            require(renderer.MotionBlurSurfaceCreationCount == count + 2);
            using var fresh = RainfallRenderer.Create(context);
            fresh.SetInput(input); fresh.Render(p, 1, frameFactory: _ => [particle]);
            Draw(context, target, fresh.Output);
            require(expected.SequenceEqual(Read(context, target)));
            renderer.ClearRain(); Draw(context, target, renderer.Output);
            require(Read(context, target).All(value => value == 0));
            require(expected.SequenceEqual(Render(p)));
        });
    }

    private static ID2D1Bitmap1 Bitmap(ID2D1DeviceContext context, int width, int height, BitmapOptions options)
        => context.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, options));

    private static void Fill(ID2D1DeviceContext context, ID2D1Bitmap1 bitmap, Color4 color)
    {
        context.Target = bitmap; context.BeginDraw();
        try { context.Clear(color); }
        finally { try { context.EndDraw().CheckError(); } finally { context.Target = null; } }
    }

    private static void FillPattern(ID2D1DeviceContext context, ID2D1Bitmap1 bitmap)
    {
        context.Target = bitmap; context.BeginDraw();
        using var brush = context.CreateSolidColorBrush(new Color4(0.1f, 0.25f, 0.4f, 1));
        try
        {
            context.Clear(new Color4(0.6f, 0.2f, 0.1f, 1));
            for (var x = 0; x < Width; x += 32) context.FillRectangle(new(x, 0, x + 16, Height), brush);
        }
        finally { try { context.EndDraw().CheckError(); } finally { context.Target = null; } }
    }

    private static void Draw(ID2D1DeviceContext context, ID2D1Bitmap1 target, ID2D1Image image)
    {
        context.Target = target; context.BeginDraw();
        try { context.Clear(new Color4(0, 0, 0, 0)); context.DrawImage(image); }
        finally { try { context.EndDraw().CheckError(); } finally { context.Target = null; } }
    }

    private static byte[] Read(ID2D1DeviceContext context, ID2D1Bitmap1 bitmap)
    {
        using var copy = Bitmap(context, Width, Height, BitmapOptions.CpuRead | BitmapOptions.CannotDraw);
        copy.CopyFromBitmap(bitmap);
        var map = copy.Map(MapOptions.Read);
        try
        {
            var pixels = new byte[Width * Height * 4];
            for (var y = 0; y < Height; y++) Marshal.Copy(IntPtr.Add(map.Bits, checked(y * (int)map.Pitch)), pixels, y * Width * 4, Width * 4);
            return pixels;
        }
        finally { copy.Unmap(); }
    }

    private static (double Sum, double XX, double YY, double XY) Moments(byte[] pixels)
    {
        double sum = 0, xSum = 0, ySum = 0;
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        { var a = pixels[(y * Width + x) * 4 + 3]; sum += a; xSum += x * a; ySum += y * a; }
        if (sum == 0) return default;
        double xx = 0, yy = 0, xy = 0;
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        { var a = pixels[(y * Width + x) * 4 + 3]; var dx = x - xSum / sum; var dy = y - ySum / sum; xx += dx * dx * a; yy += dy * dy * a; xy += dx * dy * a; }
        return (sum, xx / sum, yy / sum, xy / sum);
    }

    private static void Save(string name, byte[] pixels) => SavePng(Path.Combine(OutputDirectory, name), pixels, Width, Height);
    private static void SavePng(string path, byte[] pixels, int width, int height)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4)));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void SaveComparison(List<(string Label, byte[] Pixels)> samples)
    {
        var visual = new DrawingVisual();
            var rows = (samples.Count + 2) / 3;
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 30, 42)), null, new System.Windows.Rect(0, 0, 960, rows * 270));
            for (var i = 0; i < samples.Count; i++)
            {
                var x = i % 3 * Width; var y = i / 3 * 270;
                drawing.DrawText(new FormattedText(samples[i].Label, System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                    System.Windows.FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), 18, Brushes.White, 1), new(x + 10, y + 5));
                drawing.DrawImage(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, samples[i].Pixels, Width * 4), new System.Windows.Rect(x, y + 30, Width, Height));
            }
        }
        var image = new RenderTargetBitmap(960, rows * 270, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(OutputDirectory, "comparison.png")); encoder.Save(stream);
    }
}
