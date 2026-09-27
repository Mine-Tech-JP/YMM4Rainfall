// SPDX-License-Identifier: MPL-2.0
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Rendering;
using YMM4Rainfall.Simulation;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace YMM4Rainfall.Verification;

internal static class GraphicsChecks
{
    private const int Width = 960;
    private const int Height = 540;

    public static void Run(Action<string, Action> check, Action<bool> require, bool exportPreview)
    {
        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
            [Vortice.Direct3D.FeatureLevel.Level_11_0], out var createdDevice).CheckError();
        using var d3d = createdDevice ?? throw new InvalidOperationException("検証用Direct3Dデバイスが作成されませんでした。");
        using var dxgi = d3d.QueryInterface<IDXGIDevice>();
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using var device = factory.CreateDevice(dxgi);
        using var context = device.CreateDeviceContext(DeviceContextOptions.None);
        using var input = CreateBitmap(context, BitmapOptions.Target);
        using var target = CreateBitmap(context, BitmapOptions.Target);
        using var renderer = RainfallRenderer.Create(context);
        Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1));
        var original = ReadPixels(context, input);
        renderer.SetInput(input);
        var outputPointer = renderer.Output.NativePointer;

        byte[] Render(double time, RainfallParameters? parameters = null)
        {
            renderer.Render(parameters ?? RainfallParameters.Default, time);
            Draw(context, target, renderer.Output);
            return ReadPixels(context, target);
        }

        check("描画: 雨の量0で元映像と一致", () =>
            require(Render(1, RainfallParameters.Default with { Amount = 0 }).SequenceEqual(original)));
        var rainy = Render(1);
        check("描画: 雨を重ねるとピクセルが変わる", () =>
        {
            require(!rainy.SequenceEqual(original));
            require(Enumerable.Range(0, Width * Height).All(index => rainy[index * 4 + 3] == 255));
        });
        check("描画: シーク後に同じピクセルを再現", () =>
        {
            _ = Render(2);
            require(Render(1).SequenceEqual(rainy));
        });
        check("描画: 時刻に応じて雨が移動", () => require(!Render(1.1).SequenceEqual(rainy)));
        check("描画: 不透明度0で古い雨が残らない", () =>
            require(Render(1, RainfallParameters.Default with { Opacity = 0 }).SequenceEqual(original)));
        check("描画: 出力オブジェクトがフレーム間で安定", () =>
        {
            _ = Render(3);
            require(renderer.Output.NativePointer == outputPointer);
        });
        check("描画: ホストの描画状態を変更しない", () =>
        {
            context.Target = input;
            context.Transform = Matrix3x2.CreateTranslation(17, 29);
            context.AntialiasMode = AntialiasMode.Aliased;
            try
            {
                renderer.Render(RainfallParameters.Default, 4);
                using var retainedTarget = context.Target;
                require(retainedTarget?.NativePointer == input.NativePointer);
                require(context.Transform == Matrix3x2.CreateTranslation(17, 29));
                require(context.AntialiasMode == AntialiasMode.Aliased);
            }
            finally
            {
                context.Target = null;
                context.Transform = Matrix3x2.Identity;
                context.AntialiasMode = AntialiasMode.PerPrimitive;
            }
        });
        check("描画: 入力の境界からはみ出さない", () =>
        {
            using var offset = new AffineTransform2D(context) { TransformMatrix = Matrix3x2.CreateTranslation(-70, -30) };
            using var offsetOutput = offset.Output;
            try
            {
                offset.SetInput(0, input, true);
                renderer.SetInput(offsetOutput);
                renderer.Render(RainfallParameters.Default with { Length = 200, Thickness = 8, Angle = -80 }, 1);
                var bounds = context.GetImageLocalBounds(renderer.Output);
                require(bounds.Left >= -70 && bounds.Top >= -30 && bounds.Right <= Width - 70 && bounds.Bottom <= Height - 30);
                Draw(context, target, renderer.Output);
                var clipped = ReadPixels(context, target);
                require(Enumerable.Range(0, Width * Height)
                    .Where(index => index % Width >= Width - 70 || index / Width >= Height - 30)
                    .All(index => clipped[index * 4 + 3] == 0));
            }
            finally
            {
                renderer.SetInput(input);
                offset.SetInput(0, null, true);
            }
        });
        check("描画: 無効な入力領域から雨を切り離す", () =>
        {
            using var flood = new Flood(context) { Color = new Vector4(0, 0, 0, 1) };
            using var floodOutput = flood.Output;
            renderer.SetInput(floodOutput);
            try
            {
                renderer.Render(RainfallParameters.Default, 1);
                Draw(context, target, renderer.Output);
                var pixels = ReadPixels(context, target);
                require(Enumerable.Range(0, Width * Height).All(index =>
                    pixels[index * 4] == 0 && pixels[index * 4 + 1] == 0 && pixels[index * 4 + 2] == 0 && pixels[index * 4 + 3] == 255));
            }
            finally
            {
                renderer.SetInput(input);
            }
        });
        check("描画: 透明な入力にも雨のアルファを生成", () =>
        {
            Fill(context, input, new Color4(0, 0, 0, 0));
            var transparentRain = Render(1);
            require(Enumerable.Range(0, Width * Height).Any(index => transparentRain[index * 4 + 3] > 0));
            require(Enumerable.Range(0, Width * Height).Any(index => transparentRain[index * 4 + 3] == 0));
            require(Enumerable.Range(0, Width * Height).All(index =>
                transparentRain[index * 4] <= transparentRain[index * 4 + 3] &&
                transparentRain[index * 4 + 1] <= transparentRain[index * 4 + 3] &&
                transparentRain[index * 4 + 2] <= transparentRain[index * 4 + 3]));
            Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1));
        });
        check("描画: 入力を外すと雨も消える", () =>
        {
            renderer.SetInput(null);
            require(Render(1).All(value => value == 0));
            renderer.SetInput(input);
        });
        check("描画: 連続更新後も入力画像を保持", () =>
        {
            for (var frame = 0; frame < 120; frame++)
            {
                renderer.Render(RainfallParameters.Default, frame / 30.0);
            }

            require(ReadPixels(context, input).SequenceEqual(original));
        });

        check("描画: 赤・青への変更と白への復帰、アルファの維持", () =>
        {
            Fill(context, input, new Color4(0, 0, 0, 0));
            try
            {
                var white = Render(1);
                var red = Render(1, RainfallParameters.Default with { Red = 1, Green = 0, Blue = 0 });
                var blue = Render(1, RainfallParameters.Default with { Red = 0, Green = 0, Blue = 1 });
                require(Enumerable.Range(0, Width * Height).Any(i => red[i * 4 + 2] > 0));
                require(Enumerable.Range(0, Width * Height).All(i => red[i * 4] == 0 && red[i * 4 + 1] == 0 &&
                    red[i * 4 + 2] <= red[i * 4 + 3] && red[i * 4 + 3] == white[i * 4 + 3]));
                require(Enumerable.Range(0, Width * Height).Any(i => blue[i * 4] > 0));
                require(Enumerable.Range(0, Width * Height).All(i => blue[i * 4 + 1] == 0 && blue[i * 4 + 2] == 0));
                require(white.SequenceEqual(Render(1)));
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });
        check("描画: 降り始め前は元映像、出現完了後は雨", () =>
        {
            var p = RainfallParameters.Default with { OnsetEnabled = true, StartSeconds = 1, AppearanceSeconds = 1 };
            require(Render(0.5, p).SequenceEqual(original));
            require(!Render(2, p).SequenceEqual(original));
            require(Render(0.5, p).SequenceEqual(original));
        });
        check("描画: Animationの再シークで同じ色と位置を再現", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            FeatureChecks.SetLinear(effect.SpeedAnimation, 300, 1000);
            FeatureChecks.SetLinear(effect.AngleAnimation, -90, 720);
            FeatureChecks.SetLinear(effect.Red, 100, 0);
            FeatureChecks.SetLinear(effect.SpeedVariation, 0, 100);
            var motion = new RainfallMotion();
            byte[] Animated(long frame)
            {
                renderer.Render(effect.GetParameters(frame, 600, 60), frame / 60.0,
                    motion.Evaluate(frame, 60, effect.MotionSignature(600, 60), f => effect.GetVelocity(f, 600, 60)));
                Draw(context, target, renderer.Output);
                return ReadPixels(context, target);
            }
            var first = Animated(300);
            _ = Animated(600);
            _ = Animated(0);
            require(first.SequenceEqual(Animated(300)));
        });
        check("描画: 出生時は透明、上端から入った粒だけが見える", () =>
        {
            Fill(context, input, new Color4(0, 0, 0, 0));
            try
            {
                var p = RainfallParameters.Default with
                {
                    OnsetEnabled = true,
                    StartSeconds = 1,
                    AppearanceSeconds = 0,
                    Speed = 100,
                    SpeedVariation = 0,
                    Angle = 0
                };
                require(Render(1, p).All(value => value == 0));
                var pixels = Render(1.2, p);
                require(Enumerable.Range(0, Width * 12).Any(i => pixels[i * 4 + 3] > 0));
                require(Enumerable.Range(Width * 20, Width * (Height - 20)).All(i => pixels[i * 4 + 3] == 0));
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });
        check("描画: 3形状の差、切替後の復帰と比較画像", () =>
        {
            var p = RainfallParameters.Default with { ParticleSize = 18, Opacity = 75 };
            var streak = Render(1, p);
            var circle = Render(1, p with { Shape = RainfallShape.Circle });
            var drop = Render(1, p with { Shape = RainfallShape.CartoonDrop });
            require(!streak.SequenceEqual(circle) && !circle.SequenceEqual(drop));
            require(streak.SequenceEqual(Render(1, p)));
            SavePng(Path.GetFullPath("tmp/rainfall-shape-streak.png"), streak);
            SavePng(Path.GetFullPath("tmp/rainfall-shape-circle.png"), circle);
            SavePng(Path.GetFullPath("tmp/rainfall-shape-droplet.png"), drop);
        });
        check("描画: 全形状のアルファ・シーク・出生時の不可視と入力保持", () =>
        {
            Fill(context, input, new Color4(0, 0, 0, 0));
            try
            {
                foreach (var shape in Enum.GetValues<RainfallShape>().Where(shape => shape != RainfallShape.Png))
                {
                    var p = RainfallParameters.Default with { Shape = shape, ParticleSize = 24, Red = 0.2, Green = 0.5, Blue = 1 };
                    var first = Render(1, p);
                    require(first.Any(value => value > 0));
                    require(Enumerable.Range(0, Width * Height).All(i => first[i * 4] <= first[i * 4 + 3] &&
                        first[i * 4 + 1] <= first[i * 4 + 3] && first[i * 4 + 2] <= first[i * 4 + 3]));
                    _ = Render(5, p);
                    require(first.SequenceEqual(Render(1, p)));
                    require(Render(1, p with { Opacity = 0 }).All(value => value == 0));
                    foreach (var angle in new[] { 0, 45, 90, 180, 270 })
                        require(Render(1, p with
                        {
                            OnsetEnabled = true,
                            StartSeconds = 1,
                            AppearanceSeconds = 0,
                            ParticleSize = 64,
                            ThicknessVariation = 100,
                            Angle = angle
                        }).All(value => value == 0));
                }
                require(ReadPixels(context, input).All(value => value == 0));
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });

        check("描画: PNG倍率50・100・200%と512px超の実寸", () =>
        {
            var path = Path.GetFullPath("tmp/png-scale-800x400.png");
            var sourcePixels = Enumerable.Repeat((byte)255, 800 * 400 * 4).ToArray();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(800, 400, 96, 96,
                PixelFormats.Bgra32, null, sourcePixels, 800 * 4)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Fill(context, input, new Color4(0, 0, 0, 0));
            try
            {
                foreach (var scale in new[] { 50, 100, 200 })
                {
                    var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.CustomPng };
                    effect.PngScale.SetFirstValue(scale);
                    effect.ThicknessVariation.SetFirstValue(0);
                    var parameters = effect.GetParameters(0, 60, 30, 800).Normalize();
                    require(parameters.ParticleSize == 800 * scale / 100.0);
                    var source = new WeatherFrameSource();
                    renderer.Render(parameters, 0, pngPath: path, frameFactory: bounds =>
                    {
                        var particle = source.Evaluate(bounds, 0, 60, 30, scale.ToString(),
                            frame => effect.GetParameters(frame, 60, 30, 800))[0];
                        return [particle with { Head = new(Width / 2, Height / 2), Rotation = 0 }];
                    });
                    Draw(context, target, renderer.Output);
                    var rendered = ReadPixels(context, target);
                    var visible = Enumerable.Range(0, Width * Height).Where(i => rendered[i * 4 + 3] > 0).ToArray();
                    require(visible.Length > 0);
                    var actualWidth = visible.Max(i => i % Width) - visible.Min(i => i % Width) + 1;
                    var actualHeight = visible.Max(i => i / Width) - visible.Min(i => i / Width) + 1;
                    require(actualWidth == Math.Min(Width, 800 * scale / 100));
                    require(actualHeight == Math.Min(Height, 400 * scale / 100));
                    SavePng(Path.GetFullPath($"tmp/png-scale-{scale}.png"), rendered);
                }
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });

        check("描画: PNGの透明度・色・再シーク・出生・欠落と復帰", () =>
        {
            var path = Path.GetFullPath("tmp/png-test-source.png");
            var pixels = new byte[32 * 16 * 4];
            for (var y = 2; y < 14; y++)
                for (var x = 2; x < 30; x++)
                {
                    var i = (y * 32 + x) * 4;
                    pixels[i] = 32; pixels[i + 1] = 96; pixels[i + 2] = 128; pixels[i + 3] = 128;
                }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(32, 16, 96, 96, PixelFormats.Pbgra32, null, pixels, 128)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            byte[] Png(RainfallParameters p, double time = 1, string? selected = null)
            {
                renderer.Render(p, time, pngPath: selected ?? path);
                Draw(context, target, renderer.Output);
                return ReadPixels(context, target);
            }
            var p = RainfallParameters.Default with { Shape = RainfallShape.Png, PngScale = 200, PngSourceSize = 32, Opacity = 100 };
            Fill(context, input, new Color4(0, 0, 0, 0));
            try
            {
                var first = Png(p);
                require(first.Any(v => v > 0));
                require(renderer.PngStatus == "32 × 16 px");
                require(Enumerable.Range(0, Width * Height).All(i => first[i * 4] <= first[i * 4 + 3] &&
                    first[i * 4 + 1] <= first[i * 4 + 3] && first[i * 4 + 2] <= first[i * 4 + 3]));
                SavePng(Path.GetFullPath("tmp/rainfall-shape-png.png"), first);
                _ = Png(p, 5);
                require(first.SequenceEqual(Png(p)));
                var red = Png(p with { Green = 0, Blue = 0 });
                require(red.Any(v => v > 0));
                require(Enumerable.Range(0, Width * Height).All(i => red[i * 4] == 0 && red[i * 4 + 1] == 0));
                require(first.SequenceEqual(Png(p)));
                require(Png(p with { Opacity = 0 }).All(v => v == 0));
                foreach (var angle in new[] { 0, 45, 90, 180, 270 })
                    require(Png(p with
                    {
                        OnsetEnabled = true,
                        StartSeconds = 1,
                        AppearanceSeconds = 0,
                        ThicknessVariation = 100,
                        Angle = angle
                    }).All(v => v == 0));
                require(Png(p, selected: path + ".missing").All(v => v == 0));
                require(renderer.PngStatus.Contains("見つかりません"));
                require(first.SequenceEqual(Png(p)));
                using (var unlocked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) require(unlocked.Length > 0);
                var broken = Path.GetFullPath("tmp/png-test-broken.png");
                File.WriteAllText(broken, "broken");
                require(Png(p, selected: broken).All(v => v == 0));
                require(renderer.PngStatus.Contains("読み込めません"));
                File.Copy(path, broken, true);
                require(first.SequenceEqual(Png(p, selected: broken)));
                require(Png(p, selected: "").All(v => v == 0));
                var oversized = Path.GetFullPath("tmp/png-test-oversized.png");
                var header = File.ReadAllBytes(path);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), 2049);
                File.WriteAllBytes(oversized, header);
                require(Png(p, selected: oversized).All(v => v == 0));
                require(renderer.PngStatus.Contains("2048"));
                var hugeFile = Path.GetFullPath("tmp/png-test-large-file.png");
                using (var stream = File.Create(hugeFile)) stream.SetLength(16 * 1024 * 1024 + 1);
                require(Png(p, selected: hugeFile).All(v => v == 0));
                require(renderer.PngStatus.Contains("16 MB"));
                var wrongExtension = Path.GetFullPath("tmp/png-test-wrong.txt");
                File.Copy(path, wrongExtension, true);
                require(Png(p, selected: wrongExtension).All(v => v == 0));
                require(renderer.PngStatus.Contains("PNG形式"));
                require(ReadPixels(context, input).All(v => v == 0));
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });

        check("描画: PNGの完全不透明と中心回転、最大サイズでの出生と折り返し", () =>
        {
            var path = Path.GetFullPath("tmp/png-test-opaque.png");
            var pixels = new byte[32 * 16 * 4];
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 32; x++)
                {
                    var i = (y * 32 + x) * 4;
                    pixels[i + (x < 16 ? 2 : 1)] = 255;
                    pixels[i + 3] = 255;
                }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(32, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 128)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            byte[] Png(RainfallParameters p, double time = 0)
            {
                renderer.Render(p, time, pngPath: path);
                Draw(context, target, renderer.Output);
                return ReadPixels(context, target);
            }
            var p = RainfallParameters.Default with
            {
                Shape = RainfallShape.Png,
                PngScale = 400, PngSourceSize = 32,
                ThicknessVariation = 0,
                Opacity = 100,
                OpacityVariation = 0,
                Speed = 0,
                Amount = 0.01
            };
            // 画面中央寄りの単一粒を使い、回転中心と縦横寸法をピクセルで確認します。
            for (var seed = 0; seed <= RainfallParameters.MaximumSeed; seed++)
            {
                p = p with { Seed = seed };
                var strokes = RainfallSimulation.CreateFrame(new RainfallBounds(0, 0, Width, Height), p, 0);
                require(strokes.Length == 1);
                if (strokes[0].Head.X > 260 && strokes[0].Head.X < Width - 260 &&
                    strokes[0].Head.Y > 100 && strokes[0].Head.Y < Height - 100) break;
            }
            (int Left, int Top, int Right, int Bottom) Extent(byte[] data)
            {
                var visible = Enumerable.Range(0, Width * Height).Where(i => data[i * 4 + 3] > 0).ToArray();
                require(visible.Length > 0);
                return (visible.Min(i => i % Width), visible.Min(i => i / Width), visible.Max(i => i % Width), visible.Max(i => i / Width));
            }
            Fill(context, input, new Color4(0, 0, 0, 0));
            try
            {
                var first = Png(p);
                var box = Extent(first);
                var centerX = (box.Left + box.Right) / 2;
                var centerY = (box.Top + box.Bottom) / 2;
                var center = (centerY * Width + centerX) * 4;
                require(first[center + 3] == 255);
                require(Png(p with { Opacity = 50 })[center + 3] is >= 127 and <= 128);
                var rotated = Png(p with { RotationAngle = 90 });
                var turned = Extent(rotated);
                require(Math.Abs((box.Right - box.Left) - (turned.Bottom - turned.Top)) <= 1);
                require(Math.Abs((box.Bottom - box.Top) - (turned.Right - turned.Left)) <= 1);
                require(Math.Abs(box.Left + box.Right - turned.Left - turned.Right) <= 1);
                require(Math.Abs(box.Top + box.Bottom - turned.Top - turned.Bottom) <= 1);
                // 元画像の左の赤が、時計回り90度で上に移ります。
                require(rotated[((centerY - 24) * Width + centerX) * 4 + 2] > 250);
                require(rotated[((centerY + 24) * Width + centerX) * 4 + 1] > 250);
                require(rotated.SequenceEqual(Png(p with { RotationSpeed = 90 }, 1)));
                require(first.SequenceEqual(Png(p with { RotationSpeed = 90 }, 4)));
                require(first.SequenceEqual(Png(p with { Angle = 90, FollowDirection = false })));
                require(Png(p with { RotationAngle = -90 }).SequenceEqual(Png(p with { Angle = 90, FollowDirection = true })));
                require(Png(p with { RotationAngle = 120, KeepUpright = true }).SequenceEqual(Png(p with { RotationAngle = -60 })));
                require(Png(p with { RotationSpeed = 180, KeepUpright = true }, 1).SequenceEqual(first));
                var mirrored = Png(p with { RotationAngle = 180, KeepUpright = true, MirrorWhenUpright = true });
                require(mirrored[(centerY * Width + centerX - 24) * 4 + 1] > 250);
                require(mirrored[(centerY * Width + centerX + 24) * 4 + 2] > 250);
                require(Png(p with { KeepUpright = true, MirrorWhenUpright = true }).SequenceEqual(first));
                var large = Extent(Png(p with { PngScale = 1000 }));
                require(large.Right - large.Left >= 319 && large.Bottom - large.Top >= 159);
                foreach (var shape in Enum.GetValues<RainfallShape>().Where(s => s != RainfallShape.Streak))
                    foreach (var angle in new[] { 0, 45, 90, 180, 270 })
                        require(Png(p with
                        {
                            Shape = shape,
                            ParticleSize = 512,
                            ThicknessVariation = 100,
                            Angle = angle,
                            RotationAngle = 45,
                            OnsetEnabled = true,
                            StartSeconds = 1,
                            AppearanceSeconds = 0
                        }, 1).All(v => v == 0));
                // 折り返しを挟む瞬間の両側で、最大のPNGが完全に画面外にあることを確認します。
                var bounds = new RainfallBounds(0, 0, Width, Height);
                var wrap = p with { PngScale = 1000, ThicknessVariation = 100, Angle = 0, Speed = 900, SpeedVariation = 0 };
                var head = RainfallSimulation.CreateFrame(bounds, wrap, 0)[0].Head;
                var margin = wrap.ParticleSizeLimit * 2 + 2;
                var wrapTime = (Height + margin - head.Y) / 900;
                require(Png(wrap, wrapTime - 0.0001).All(v => v == 0));
                require(Png(wrap, wrapTime + 0.0001).All(v => v == 0));
                var pig = Path.GetFullPath("samples/pig-side-256.png");
                if (File.Exists(pig))
                {
                    renderer.Render(p with { ParticleSize = 256, RotationAngle = 25 }, 0, pngPath: pig);
                    Draw(context, target, renderer.Output);
                    SavePng(Path.GetFullPath("tmp/rainfall-pig-rotation.png"), ReadPixels(context, target));
                }
                require(ReadPixels(context, input).All(v => v == 0));
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });

        check("描画: 揺らめき3形状の位置変化・一周期後と再シークの一致", () =>
        {
            var original = ReadPixels(context, input);
            foreach (var shape in new[] { RainfallShape.Bubble, RainfallShape.LensBubble, RainfallShape.Png })
            {
                var p = RainfallParameters.Default with
                {
                    Shape = shape,
                    Speed = 0,
                    Angle = 180,
                    ParticleSize = 128,
                    ThicknessVariation = 0,
                    Amount = 0.01,
                    Opacity = 100,
                    SwayEnabled = true,
                    SwayAmplitude = 40,
                    SwayPeriod = 2, PngSourceSize = 256, PngScale = 50, PngMaximumScale = 50
                };
                var found = false;
                for (var seed = 0; seed <= RainfallParameters.MaximumSeed; seed++)
                {
                    p = p with { Seed = seed };
                    var a = RainfallSimulation.CreateFrame(new RainfallBounds(0, 0, Width, Height), p, 0.5)[0].Head;
                    var b = RainfallSimulation.CreateFrame(new RainfallBounds(0, 0, Width, Height), p, 1.5)[0].Head;
                    if (a.X > 130 && a.X < Width - 130 && a.Y > 130 && a.Y < Height - 130 && Vector2.Distance(a, b) > 10)
                    { found = true; break; }
                }
                require(found);
                byte[] SwayFrame(double seconds)
                {
                    renderer.Render(p, seconds, pngPath: Path.GetFullPath("samples/pig-side-256.png"));
                    Draw(context, target, renderer.Output);
                    return ReadPixels(context, target);
                }
                var first = SwayFrame(0.5);
                require(!first.SequenceEqual(original));
                require(!first.SequenceEqual(SwayFrame(1.5)));
                require(first.SequenceEqual(SwayFrame(2.5)));
                require(first.SequenceEqual(SwayFrame(0.5)));
                require(original.SequenceEqual(ReadPixels(context, input)));
            }
        });
        check("描画: 水泡の中央は透明・輪郭と反射光・中心回転・再シーク", () =>
        {
            var p = RainfallParameters.Default with
            {
                Shape = RainfallShape.Bubble,
                ParticleSize = 128,
                ThicknessVariation = 0,
                Opacity = 100,
                Speed = 0,
                Amount = 0.01
            };
            Vector2 center = default;
            for (var seed = 0; seed <= RainfallParameters.MaximumSeed; seed++)
            {
                p = p with { Seed = seed };
                center = RainfallSimulation.CreateFrame(new RainfallBounds(0, 0, Width, Height), p, 0)[0].Head;
                if (center.X > 100 && center.X < Width - 100 && center.Y > 100 && center.Y < Height - 100) break;
            }
            Fill(context, input, new Color4(0, 0, 0, 0));
            try
            {
                var first = Render(0, p);
                var pixel = ((int)center.Y * Width + (int)center.X) * 4;
                require(first[pixel + 3] == 0);
                require(first.Any(v => v > 0));
                var turned = Render(0, p with { RotationAngle = 90 });
                require(turned[pixel + 3] == 0 && !turned.SequenceEqual(first));
                require(first.SequenceEqual(Render(0, p)));
                require(Render(0, p with { Opacity = 0 }).All(v => v == 0));
                var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.Bubble };
                var restored = YukkuriMovieMaker.Json.Json.LoadFromText<RainfallEffect>(YukkuriMovieMaker.Json.Json.GetJsonText(effect))!;
                require(restored.Shape == RainfallShape.Bubble && restored.IsParticle && !restored.IsPng);
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
            SavePng(Path.GetFullPath("tmp/rainfall-shape-bubble.png"), Render(1, p with { Amount = 3, ParticleSize = 128, ThicknessVariation = 40, Red = 0.4, Green = 0.8, Blue = 1 }));
        });

        check("描画: レンズ水泡の背景反転・映り込み強度・範囲外保持・再シーク", () =>
        {
            var p = RainfallParameters.Default with
            {
                Shape = RainfallShape.LensBubble,
                ParticleSize = 128,
                ThicknessVariation = 0,
                Opacity = 100,
                Speed = 0,
                Amount = 0.01,
                LensReflection = 100
            };
            Vector2 center = default;
            for (var seed = 0; seed <= RainfallParameters.MaximumSeed; seed++)
            {
                p = p with { Seed = seed };
                center = RainfallSimulation.CreateFrame(new RainfallBounds(0, 0, Width, Height), p, 0)[0].Head;
                if (center.X > 150 && center.X < Width - 150 && center.Y > 150 && center.Y < Height - 150) break;
            }
            var cx = (int)center.X; var cy = (int)center.Y;
            context.Target = input;
            context.BeginDraw();
            using (var b = context.CreateSolidColorBrush(new Color4(1, 0, 0, 1)))
            {
                context.FillRectangle(new Rect(0, 0, cx, cy), b);
                b.Color = new Color4(0, 1, 0, 1);
                context.FillRectangle(new Rect(cx, 0, Width - cx, cy), b);
                b.Color = new Color4(0, 0, 1, 1);
                context.FillRectangle(new Rect(0, cy, cx, Height - cy), b);
                b.Color = new Color4(1, 1, 0, 1);
                context.FillRectangle(new Rect(cx, cy, Width - cx, Height - cy), b);
            }
            context.EndDraw().CheckError();
            context.Target = null;
            try
            {
                var baseline = ReadPixels(context, input);
                var first = Render(0, p);
                var lowerRight = ((cy + 20) * Width + cx + 20) * 4;
                var upperRight = ((cy - 20) * Width + cx + 20) * 4;
                require(first[lowerRight + 2] > 230 && first[lowerRight + 1] < 30);
                require(first[upperRight] > 230 && first[upperRight + 1] < 30);
                require(Render(0, p with { LensReflection = 0 })[lowerRight + 1] > 230);
                require(first.SequenceEqual(Render(0, p)));
                require(first.Take(4).SequenceEqual(baseline.Take(4)));
                require(Render(0, p with { Opacity = 0 }).SequenceEqual(baseline));
                require(ReadPixels(context, input).SequenceEqual(baseline));
                SavePng(Path.GetFullPath("tmp/rainfall-shape-lens-bubble.png"), Render(0, p with { ParticleSize = 256, LensReflection = 35 }));
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });

        check("描画: レンズ水泡をエフェクト出力・描画コマンド入力へ重ねる", () =>
        {
            var p = RainfallParameters.Default with { Shape = RainfallShape.LensBubble };
            var direct = Render(1, p);
            using var upstream = new Composite(context) { Mode = CompositeMode.SourceOver };
            upstream.SetInput(0, input, true);
            upstream.SetInput(1, input, true);
            using var upstreamOutput = upstream.Output;
            using var commands = context.CreateCommandList();
            context.Target = commands;
            context.BeginDraw();
            context.DrawImage(upstreamOutput);
            context.EndDraw().CheckError();
            context.Target = null;
            commands.Close();
            try
            {
                renderer.SetInput(upstreamOutput);
                require(Render(1, p).SequenceEqual(direct));
                renderer.SetInput(commands);
                require(Render(1, p).SequenceEqual(direct));
            }
            finally { renderer.SetInput(input); }
        });

        check("描画: 映り込み用画像は負の原点と入力の更新を保持", () =>
        {
            using var moved = new AffineTransform2D(context) { TransformMatrix = Matrix3x2.CreateTranslation(-70, -30) };
            moved.SetInput(0, input, true);
            using var shifted = moved.Output;
            var bounds = new RainfallBounds(-70, -30, Width - 70, Height - 30);
            using var snapshot = RainfallBackgroundSnapshot.Capture(context, shifted, bounds);
            require(ReadPixels(context, snapshot.Bitmap).SequenceEqual(original));
            require(snapshot.ToWorld == Matrix3x2.CreateTranslation(-70, -30));
            Fill(context, input, new Color4(1, 0, 0, 1));
            try
            {
                using var updated = RainfallBackgroundSnapshot.Capture(context, shifted, bounds);
                require(!ReadPixels(context, updated.Bitmap).SequenceEqual(original));
                require(ReadPixels(context, snapshot.Bitmap).SequenceEqual(original));
            }
            finally { Fill(context, input, new Color4(0.08f, 0.12f, 0.18f, 1)); }
        });

        var previewPath = Path.GetFullPath(Path.Combine("tmp", "rainfall-preview.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
        SavePng(previewPath, Render(1));
        Console.WriteLine($"描画サンプル: {previewPath}");
        if (exportPreview)
        {
            var framesDirectory = Path.GetFullPath(Path.Combine("tmp", "rainfall-preview-frames"));
            Directory.CreateDirectory(framesDirectory);
            var sample = new RainfallEffect(useNewItemDefaults: false) { OnsetEnabled = true, StartSeconds = 0.2, AppearanceSeconds = 1.2, RampEnabled = true, RampSeconds = 2.5 };
            sample.AngleAnimation.SetFirstValue(0);
            FeatureChecks.SetLinear(sample.SpeedAnimation, 300, 1200);
            FeatureChecks.SetLinear(sample.Red, 100, 20);
            FeatureChecks.SetLinear(sample.AmountAnimation, 30, 80);
            FeatureChecks.SetLinear(sample.ThicknessVariation, 0, 100);
            FeatureChecks.SetLinear(sample.SpeedVariation, 0, 100);
            sample.ThicknessAnimation.Values[0].Value = 2;
            var motion = new RainfallMotion();
            for (var frame = 0; frame < 90; frame++)
            {
                renderer.Render(sample.GetParameters(frame, 90, 30), frame / 30.0,
                    motion.Evaluate(frame, 30, sample.MotionSignature(90, 30), f => sample.GetVelocity(f, 90, 30)), sample.CreateEmission(motion, 90, 30));
                Draw(context, target, renderer.Output);
                SavePng(Path.Combine(framesDirectory, $"frame-{frame:D4}.png"), ReadPixels(context, target));
            }

            Console.WriteLine($"動画用フレーム: {framesDirectory}");
        }
        check("描画: 二重解放を許容し、借用入力を破棄しない", () =>
        {
            renderer.Dispose();
            renderer.Dispose();
            require(ReadPixels(context, input).SequenceEqual(original));
        });
    }

    private static ID2D1Bitmap1 CreateBitmap(ID2D1DeviceContext context, BitmapOptions options) =>
        context.CreateBitmap(new SizeI(Width, Height), new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, options));

    private static void Fill(ID2D1DeviceContext context, ID2D1Bitmap1 target, Color4 color)
    {
        context.Target = target;
        try
        {
            context.BeginDraw();
            context.Clear(color);
            context.EndDraw().CheckError();
        }
        finally
        {
            context.Target = null;
        }
    }

    private static void Draw(ID2D1DeviceContext context, ID2D1Bitmap1 target, ID2D1Image image)
    {
        context.Target = target;
        try
        {
            context.BeginDraw();
            context.Clear(new Color4(0, 0, 0, 0));
            context.DrawImage(image);
            context.EndDraw().CheckError();
        }
        finally
        {
            context.Target = null;
        }
    }

    private static byte[] ReadPixels(ID2D1DeviceContext context, ID2D1Bitmap1 bitmap)
    {
        using var readback = CreateBitmap(context, BitmapOptions.CpuRead | BitmapOptions.CannotDraw);
        readback.CopyFromBitmap(bitmap);
        var mapped = readback.Map(MapOptions.Read);
        try
        {
            var pixels = new byte[Width * Height * 4];
            for (var row = 0; row < Height; row++)
            {
                Marshal.Copy(IntPtr.Add(mapped.Bits, checked(row * (int)mapped.Pitch)), pixels, row * Width * 4, Width * 4);
            }

            return pixels;
        }
        finally
        {
            readback.Unmap();
        }
    }

    private static void SavePng(string path, byte[] pixels)
    {
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
