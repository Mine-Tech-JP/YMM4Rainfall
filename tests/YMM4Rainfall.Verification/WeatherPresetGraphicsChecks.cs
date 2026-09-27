// SPDX-License-Identifier: MPL-2.0
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Rendering;
using YMM4Rainfall.Simulation;
using Rect = System.Windows.Rect;

namespace YMM4Rainfall.Verification;

internal static class WeatherPresetGraphicsChecks
{
    private const int Width = 1920, Height = 1080;

    public static void Run(ID2D1DeviceContext context, Action<string, Action> check, Action<bool> require)
    {
        check("設定例描画: Full HDの共通背景で10例を描き、逆シークを再現", () =>
        {
            using var input = Bitmap(BitmapOptions.Target);
            using var target = Bitmap(BitmapOptions.Target);
            using var reader = Bitmap(BitmapOptions.CpuRead | BitmapOptions.CannotDraw);
            using var brush = context.CreateSolidColorBrush(new Color4(0.12f, 0.21f, 0.29f, 1));
            context.Target = input;
            try
            {
                context.BeginDraw();
                context.Clear(new Color4(0.06f, 0.12f, 0.18f, 1));
                for (var i = 0; i < 8; i++)
                    context.FillRectangle(new(i * 260, 600 - i % 3 * 120, i * 260 + 160, Height), brush);
                context.EndDraw().CheckError();
            }
            finally { context.Target = null; }
            var background = Read(input);
            using var renderer = RainfallRenderer.Create(context);
            renderer.SetInput(input);
            var directory = Path.GetFullPath("tmp/preset-review-20260913/rendered");
            Directory.CreateDirectory(directory);
            var images = new List<(string Name, BitmapSource Image)>();
            foreach (var preset in WeatherPresetCatalog.All)
            {
                var effect = new RainfallEffect { Kind = preset.Kind, Shape = preset.Shape };
                require(preset.ApplyTo(effect));
                // 比較時のみ出現開始を無効にし、色・種・解像度を統一します。
                effect.OnsetEnabled = false;
                effect.Seed = 0;
                effect.Red.SetFirstValue(100);
                effect.Green.SetFirstValue(100);
                effect.Blue.SetFirstValue(100);
                var source = new WeatherFrameSource();
                byte[] Frame(long frame)
                {
                    renderer.Render(effect.GetParameters(frame, 360, 60), frame / 60.0,
                        frameFactory: bounds => source.Evaluate(bounds, frame, 360, 60, preset.Key,
                            sample => effect.GetParameters(sample, 360, 60)));
                    context.Target = target;
                    try
                    {
                        context.BeginDraw();
                        context.Clear(new Color4(0, 0, 0, 0));
                        context.DrawImage(renderer.Output);
                        context.EndDraw().CheckError();
                    }
                    finally { context.Target = null; }
                    return Read(target);
                }
                var pixels = Frame(120);
                require(!pixels.SequenceEqual(background));
                _ = Frame(180);
                require(Frame(120).SequenceEqual(pixels));
                var image = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, pixels, Width * 4);
                Save(Path.Combine(directory, preset.Key + ".png"), image);
                images.Add((preset.Name, image));
            }
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 30, 48)), null, new Rect(0, 0, 1010, 1640));
                Text("設定例の描画比較（Full HDから縮小）", 20, 15, 22);
                Text("共通背景・乱数0・白・開始2秒。比較用に出現開始のみ無効。", 20, 48, 14);
                for (var i = 0; i < images.Count; i++)
                {
                    var x = 20 + i % 2 * 495;
                    var y = 85 + i / 2 * 307;
                    Text(images[i].Name, x, y, 16);
                    drawing.DrawImage(images[i].Image, new Rect(x, y + 26, 475, 267.1875));
                }
                void Text(string value, int x, int y, int size) => drawing.DrawText(new FormattedText(value,
                    System.Globalization.CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
                    new Typeface("Yu Gothic UI"), size, Brushes.White, 1), new Point(x, y));
            }
            var board = new RenderTargetBitmap(1010, 1640, 96, 96, PixelFormats.Pbgra32);
            board.Render(visual);
            Save(Path.Combine(directory, "comparison.png"), board);

            ID2D1Bitmap1 Bitmap(BitmapOptions options) => context.CreateBitmap(new SizeI(Width, Height),
                new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                    Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, options));
            byte[] Read(ID2D1Bitmap1 bitmap)
            {
                reader.CopyFromBitmap(bitmap);
                var mapped = reader.Map(MapOptions.Read);
                try
                {
                    var pixels = new byte[Width * Height * 4];
                    for (var y = 0; y < Height; y++)
                        Marshal.Copy(IntPtr.Add(mapped.Bits, checked(y * (int)mapped.Pitch)), pixels, y * Width * 4, Width * 4);
                    return pixels;
                }
                finally { reader.Unmap(); }
            }
        });
    }

    private static void Save(string path, BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
