// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Rendering;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace YMM4Rainfall.Verification;

internal static class SnowGraphicsChecks
{
    private const int Width = 640;
    private const int Height = 480;
    private static readonly Vector2 Center = new(Width / 2f, Height / 2f);

    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
            [Vortice.Direct3D.FeatureLevel.Level_11_0], out var createdDevice).CheckError();
        using var d3d = createdDevice ?? throw new InvalidOperationException("雪描画検証用Direct3Dデバイスが作成されませんでした。");
        using var dxgi = d3d.QueryInterface<IDXGIDevice>();
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using var device = factory.CreateDevice(dxgi);
        using var context = device.CreateDeviceContext(DeviceContextOptions.None);
        WeatherPresetGraphicsChecks.Run(context, check, require);
        using var input = CreateBitmap(context, BitmapOptions.Target);
        using var target = CreateBitmap(context, BitmapOptions.Target);
        using var renderer = RainfallRenderer.Create(context);
        Fill(context, input, new Color4(0, 0, 0, 0));
        var transparentInput = ReadPixels(context, input);
        renderer.SetInput(input);

        var dropletOnset = RainfallParameters.Default with
        {
            Kind = WeatherKind.Rain, Shape = RainfallShape.CartoonDrop, Amount = 0.001,
            ParticleSize = 100, Speed = 60, Angle = 0, Opacity = 100,
            ThicknessVariation = 0, SpeedVariation = 0, OnsetEnabled = true,
            StartSeconds = 2, AppearanceSeconds = 10, RampEnabled = true, RampSeconds = 10,
        };
        byte[] OnsetPixels(RainfallParameters parameters, WeatherFrameSource source, long frame)
        {
            renderer.Render(parameters, frame / 60.0,
                frameFactory: bounds => source.Evaluate(bounds, frame, 1200, 60,
                    "droplet-onset", _ => parameters));
            Draw(context, target, renderer.Output);
            return ReadPixels(context, target);
        }
        check("水滴の出現開始: 丸との対照・8方向・回転後も開始時に画面外", () =>
        {
            foreach (var shape in new[] { RainfallShape.Circle, RainfallShape.CartoonDrop })
            foreach (var angle in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
            foreach (var rotation in new[] { 0, 45, 90, 180 })
            foreach (var speed in new[] { 0, 60 })
            foreach (var ramp in new[] { false, true })
            {
                var parameters = dropletOnset with
                {
                    Shape = shape, Angle = angle, RotationAngle = rotation, Speed = speed, RampEnabled = ramp,
                };
                var source = new WeatherFrameSource();
                require(OnsetPixels(parameters, source, 119).SequenceEqual(transparentInput));
                if (!OnsetPixels(parameters, source, 120).SequenceEqual(transparentInput))
                    throw new InvalidOperationException($"開始時に画面内へ突出: {shape} 方向{angle}° 回転{rotation}° 速度{speed} 立ち上がり方式{ramp}");
            }
        });
        check("水滴の出現開始: 停止した粒を回転しても断片が残らない", () =>
        {
            foreach (var angle in new[] { 0, 90, 180, 270 })
            foreach (var spin in new[] { -360, 360 })
            {
                var parameters = dropletOnset with { Speed = 0, Angle = angle, RotationSpeed = spin };
                var source = new WeatherFrameSource();
                for (var frame = 120; frame <= 180; frame += 5)
                    require(OnsetPixels(parameters, source, frame).SequenceEqual(transparentInput));
                require(OnsetPixels(parameters, source, 120).SequenceEqual(transparentInput));
            }
        });
        check("水滴の出現開始: 先頭が早く入り、逆シーク後も画素が一致", () =>
        {
            foreach (var angle in new[] { 0, 90, 180, 270 })
            {
                var parameters = dropletOnset with { ParticleSize = 24, Speed = 480, Angle = angle, FollowDirection = true };
                var source = new WeatherFrameSource();
                var firstVisible = -1;
                for (var frame = 120; frame <= 138; frame++)
                    if (!OnsetPixels(parameters, source, frame).SequenceEqual(transparentInput)) { firstVisible = frame; break; }
                require(firstVisible > 120 && firstVisible <= 138);
                var early = OnsetPixels(parameters, source, 138);
                _ = OnsetPixels(parameters, source, 600);
                require(early.SequenceEqual(OnsetPixels(parameters, source, 138)));
                require(early.SequenceEqual(OnsetPixels(parameters, new WeatherFrameSource(), 138)));
            }
        });

        check("標準PNGの出現開始: 開始時は空で、先頭のブタが早く見える", () =>
        {
            var effect = new RainfallEffect { Kind = WeatherKind.CustomPng };
            var p = effect.GetParameters(0, 1200, 60, 256).Normalize();
            var source = new WeatherFrameSource();
            var firstVisible = -1;
            for (var frame = 0; frame <= 60; frame++)
            {
                renderer.Render(p, frame / 60.0, pngPath: RainfallBuiltInImage.PigPath,
                    frameFactory: bounds => source.Evaluate(bounds, frame, 1200, 60, "pig-onset", _ => p));
                Draw(context, target, renderer.Output);
                var pixels = ReadPixels(context, target);
                if (!pixels.SequenceEqual(transparentInput)) { firstVisible = frame; break; }
            }
            Console.WriteLine($"標準ブタの先頭表示: {firstVisible / 60.0:F3} 秒（640×480、60 FPS）");
            require(firstVisible > 0 && firstVisible <= 18);
            var stopped = p with { Speed = 0, RotationAngle = 0, InitialRotationVariation = 0 };
            renderer.Render(stopped, 1, pngPath: RainfallBuiltInImage.PigPath,
                frameFactory: bounds => new WeatherFrameSource().Evaluate(bounds, 60, 1200, 60,
                    "stopped-pig-onset", _ => stopped));
            Draw(context, target, renderer.Output);
            require(ReadPixels(context, target).SequenceEqual(transparentInput));
        });

        check("標準PNG: DLL内のブタが原本と同じ画素で描画され、左右回転を区別", () =>
        {
            var builtIn = RainfallBuiltInImage.LoadPig();
            var original = RainfallPngData.Load(Path.GetFullPath("samples/pig-side-256.png"));
            require(builtIn.Bytes.SequenceEqual(original.Bytes));
            require(builtIn.Pixels.SequenceEqual(original.Pixels));
            var parameters = RainfallParameters.Default with { Shape = RainfallShape.Png, PngScale = 100, Red = 1, Green = 1, Blue = 1 };
            byte[] Pig(string path, float rotation)
            {
                renderer.Render(parameters, 0, pngPath: path, frameFactory: _ =>
                    [new RainfallStroke(Center, Center, 1, 1, 256, rotation, false, 0)]);
                Draw(context, target, renderer.Output);
                return ReadPixels(context, target);
            }
            var normal = Pig(RainfallBuiltInImage.PigPath, 0);
            require(!normal.SequenceEqual(transparentInput));
            require(normal.SequenceEqual(Pig(Path.GetFullPath("samples/pig-side-256.png"), 0)));
            require(!Pig(RainfallBuiltInImage.PigPath, -100).SequenceEqual(Pig(RainfallBuiltInImage.PigPath, 100)));
        });

        byte[] Render(RainfallParameters parameters, int variant = 0, float rotation = 0, float size = 160)
        {
            renderer.Render(parameters, 0, frameFactory: _ =>
                [new RainfallStroke(Center, Center, 1, 1, size, rotation, false, variant)]);
            Draw(context, target, renderer.Output);
            return ReadPixels(context, target);
        }

        foreach (var shape in new[]
        {
            RainfallShape.SnowRound,
            RainfallShape.SnowClump,
            RainfallShape.SnowCrystal,
        })
        {
            check($"雪描画: {shape}を透明背景へ描画", () =>
            {
                var parameters = RainfallParameters.Default with
                {
                    Shape = shape,
                    Red = 0.35,
                    Green = 0.7,
                    Blue = 1,
                    SnowSoftness = 50,
                };
                var pixels = Render(parameters);
                require(!pixels.SequenceEqual(transparentInput));
                require(Pixels(pixels).Any(pixel => pixel.A > 0));
                require(Pixels(pixels).Any(pixel => pixel.A == 0));
                require(Pixels(pixels).All(pixel => pixel.B <= pixel.A && pixel.G <= pixel.A && pixel.R <= pixel.A));
                require(AlphaBounds(pixels).Width is > 40 and <= 160);
                require(AlphaBounds(pixels).Height is > 40 and <= 160);
                SavePng($"tmp/snow-{shape.ToString().ToLowerInvariant()}.png", pixels);
            });
        }

        check("天候描画: 種類別設定から全形状を描き、保存と再シークを再現", () =>
        {
            var shapes = new[] { RainfallShape.Streak, RainfallShape.Circle, RainfallShape.CartoonDrop,
                RainfallShape.SnowRound, RainfallShape.SnowClump, RainfallShape.SnowCrystal,
                RainfallShape.Bubble, RainfallShape.LensBubble, RainfallShape.Png };
            var pngPath = Path.GetFullPath("tmp/weather-integration-input.png");
            Fill(context, input, new Color4(0.8f, 0.5f, 0.2f, 1));
            SavePng(pngPath, ReadPixels(context, input));
            Fill(context, input, new Color4(0.04f, 0.09f, 0.16f, 1));
            var background = ReadPixels(context, input);
            foreach (var shape in shapes)
            {
                var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = shape };
                effect.AmountAnimation.SetFirstValue(15);
                effect.ParticleSize.SetFirstValue(30);
                effect.Opacity.SetFirstValue(100);
                effect.SpeedAnimation.SetFirstValue(120);
                effect.WindEnabled = effect.IsWindSupported;
                if (effect.IsSnow) effect.SnowMotion = SnowMotionKind.Vortex;
                FeatureChecks.SetLinear(effect.WindSpeed, 20, 200);
                var source = new WeatherFrameSource();
                byte[] Evaluate(RainfallEffect settings, WeatherFrameSource evaluator, long frame)
                {
                    renderer.Render(settings.GetParameters(frame, 120, 30), frame / 30.0, pngPath: pngPath,
                        frameFactory: bounds => evaluator.Evaluate(bounds, frame, 120, 30, settings.MotionSignature(120, 30),
                            sample => settings.GetParameters(sample, 120, 30)));
                    Draw(context, target, renderer.Output);
                    return ReadPixels(context, target);
                }
                var first = Evaluate(effect, source, 30);
                require(!first.SequenceEqual(background));
                var next = Evaluate(effect, source, 120);
                require(!first.SequenceEqual(next));
                require(first.SequenceEqual(Evaluate(effect, source, 30)));
                var saved = YukkuriMovieMaker.Json.Json.LoadFromText<RainfallEffect>(YukkuriMovieMaker.Json.Json.GetJsonText(effect))!;
                require(first.SequenceEqual(Evaluate(saved, new WeatherFrameSource(), 30)));
                check($"サイズ速度連動の描画: {shape}の移動・保存・逆シーク", () =>
                {
                    effect.RainSizeMotionLinked = true;
                    FeatureChecks.SetLinear(effect.ThicknessVariation, 20, 80);
                    FeatureChecks.SetLinear(effect.SpeedVariation, 20, 100);
                    var linked = Evaluate(effect, source, 30);
                    require(!linked.SequenceEqual(background));
                    require(!linked.SequenceEqual(Evaluate(effect, source, 120)));
                    require(linked.SequenceEqual(Evaluate(effect, source, 30)));
                    var restored = YukkuriMovieMaker.Json.Json.LoadFromText<RainfallEffect>(YukkuriMovieMaker.Json.Json.GetJsonText(effect))!;
                    require(linked.SequenceEqual(Evaluate(restored, new WeatherFrameSource(), 30)));
                    // 雪用PNGも同じ描画経路で連動設定と再シークを確認します。
                    if (shape == RainfallShape.Png)
                    {
                        effect.Kind = WeatherKind.Snow; effect.Shape = RainfallShape.Png;
                        effect.RainSizeMotionLinked = true;
                        var snowPng = Evaluate(effect, source, 30);
                        require(!snowPng.SequenceEqual(background));
                        _ = Evaluate(effect, source, 120);
                        require(snowPng.SequenceEqual(Evaluate(effect, source, 30)));
                    }
                });
                if (effect.IsSnow)
                {
                    // プラグインと同じ経路で描いた代表画像を、暗い背景と合成して確認します。
                    _ = Evaluate(effect, source, 30);
                    SavePng($"tmp/weather-scene-{shape.ToString().ToLowerInvariant()}.png", ReadPixels(context, target));
                }
                effect.Opacity.SetFirstValue(0);
                require(Evaluate(effect, source, 30).SequenceEqual(background));
            }
            Fill(context, input, new Color4(0, 0, 0, 0));
        });

        check("結晶描画: 6形状を描き分けて大小の比較画像を保存", () =>
        {
            var parameters = RainfallParameters.Default with { Shape = RainfallShape.SnowCrystal };
            var variants = new List<(string Name, byte[] Large, byte[] Small)>();
            var names = new[] { "6本枝", "樹枝状", "細かな樹枝状", "幅広い星形", "扇形", "六角板" };
            require(SnowCrystalDrawing.StyleCount == names.Length);
            for (var variant = 0; variant < names.Length; variant++)
            {
                var large = Render(parameters, variant, size: 160);
                var small = Render(parameters, variant, size: 48);
                require(AlphaSum(large) > 0 && AlphaSum(small) > 0);
                require(AlphaBounds(large).Width <= 160 && AlphaBounds(large).Height <= 160);
                require(variants.All(previous => !previous.Large.SequenceEqual(large) && !previous.Small.SequenceEqual(small)));
                require(Render(parameters, variant, size: 160).SequenceEqual(large));
                variants.Add((names[variant], large, small));
            }
            SaveCrystalComparison("tmp/snow-crystal-variants.png", variants);
        });

        check("天候描画: 種類切替後の標準スライダー編集を実際のProcessorへ反映", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            foreach (var bank in new[] { effect.RainSettings, effect.SnowSettings, effect.BubbleSettings, effect.PngSettings })
            {
                bank.AmountAnimation.SetFirstValue(8);
                bank.ParticleSize.SetFirstValue(24);
                bank.Opacity.SetFirstValue(100);
                bank.SpeedAnimation.SetFirstValue(120);
                bank.SpeedVariation.SetFirstValue(0);
                bank.ThicknessVariation.SetFirstValue(0);
                bank.OpacityVariation.SetFirstValue(0);
                bank.RotationSpeed = 0;
                bank.DriftEnabled = false;
            }
            var names = new[] { nameof(RainfallEffect.AmountAnimation), nameof(RainfallEffect.Opacity),
                nameof(RainfallEffect.SpeedVariation), nameof(RainfallEffect.ThicknessVariation),
                nameof(RainfallEffect.OpacityVariation), nameof(RainfallEffect.ParticleSize) };
            var editors = new Dictionary<string, (AnimationSliderAttribute Attribute, AnimationSlider Control)>();
            var cache = new PropertiesCache();
            using var processor = new RainfallEffectProcessor(context, effect);
            processor.SetInput(input);
            try
            {
                foreach (var name in names)
                {
                    var property = typeof(RainfallEffect).GetProperty(name)!;
                    var attribute = (AnimationSliderAttribute)property.GetCustomAttributes(typeof(AnimationSliderAttribute), true).Single();
                    var control = (AnimationSlider)attribute.Create();
                    attribute.SetBindings(control, [new ItemProperty(effect, effect, property, cache)]);
                    editors.Add(name, (attribute, control));
                }
                void Set(string name, double value)
                {
                    var viewModel = editors[name].Control.ViewModel
                        ?? throw new InvalidOperationException($"{name}のスライダー編集モデルがありません。");
                    viewModel.Values[0].Value = value;
                }
                byte[] Evaluate()
                {
                    processor.UpdateFrame(60, 120, 30);
                    Draw(context, target, processor.Output);
                    return ReadPixels(context, target);
                }
                void Expect(bool condition, string detail)
                {
                    if (!condition) throw new InvalidOperationException($"{effect.Kind}/{effect.Shape}: {detail}");
                    require(condition);
                }
                foreach (var shape in new[] { RainfallShape.Streak, RainfallShape.Circle, RainfallShape.CartoonDrop,
                    RainfallShape.SnowRound, RainfallShape.SnowClump, RainfallShape.SnowCrystal,
                    RainfallShape.Bubble, RainfallShape.LensBubble, RainfallShape.Circle })
                {
                    effect.Shape = shape;
                    Set(nameof(RainfallEffect.AmountAnimation), 8);
                    Set(nameof(RainfallEffect.Opacity), 100);
                    var visible = Evaluate();
                    Expect(AlphaSum(visible) > 0, "初期の粒が描画されません。");
                    Set(nameof(RainfallEffect.AmountAnimation), 0);
                    Expect(Evaluate().SequenceEqual(transparentInput), "量0が描画へ反映されません。");
                    Set(nameof(RainfallEffect.AmountAnimation), 8);
                    Set(nameof(RainfallEffect.Opacity), 25);
                    Expect(AlphaSum(Evaluate()) < AlphaSum(visible), "不透明度を下げても薄くなりません。");
                    Set(nameof(RainfallEffect.Opacity), 0);
                    Expect(Evaluate().SequenceEqual(transparentInput), "不透明度0が描画へ反映されません。");
                    Set(nameof(RainfallEffect.Opacity), 100);
                    foreach (var variation in new[] { nameof(RainfallEffect.SpeedVariation),
                        nameof(RainfallEffect.ThicknessVariation), nameof(RainfallEffect.OpacityVariation) })
                    {
                        Set(variation, 0);
                        var uniform = Evaluate();
                        Set(variation, 100);
                        Expect(!Evaluate().SequenceEqual(uniform), $"{variation}の変更が描画へ反映されません。");
                        Set(variation, 0);
                    }
                    if (shape != RainfallShape.Streak)
                    {
                        Set(nameof(RainfallEffect.ParticleSize), 16);
                        var small = Evaluate();
                        Set(nameof(RainfallEffect.ParticleSize), 40);
                        Expect(!Evaluate().SequenceEqual(small), "大きさの変更が描画へ反映されません。");
                    }
                }
            }
            finally
            {
                foreach (var editor in editors.Values) editor.Attribute.ClearBindings(editor.Control);
                processor.ClearInput();
            }
        });

        check("雪描画: variantと回転で形の単調さを抑える", () =>
        {
            foreach (var shape in new[]
            {
                RainfallShape.SnowRound,
                RainfallShape.SnowClump,
                RainfallShape.SnowCrystal,
            })
            {
                var parameters = RainfallParameters.Default with { Shape = shape, SnowSoftness = 35 };
                var variant0 = Render(parameters, 0);
                var variant1 = Render(parameters, 1);
                var rotated = Render(parameters, 0, 31);
                require(!variant0.SequenceEqual(variant1));
                require(!variant0.SequenceEqual(rotated));
                require(variant0.SequenceEqual(Render(parameters, 0)));
            }
        });

        check("天候描画: 未対応の保存形式・種類・形状で古い粒を消して入力を保持", () =>
        {
            foreach (var invalidate in new Action<RainfallEffect>[]
            {
                settings => settings.SchemaVersion = 99,
                settings => settings.Kind = (WeatherKind)99,
                settings => settings.SnowSettings.Shape = RainfallShape.Bubble,
                settings => settings.CrystalStyle = (SnowCrystalStyle)99,
            })
            {
                var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
                effect.ParticleSize.SetFirstValue(24);
                using var processor = new RainfallEffectProcessor(context, effect);
                processor.SetInput(input);
                processor.UpdateFrame(60, 120, 30);
                Draw(context, target, processor.Output);
                require(!ReadPixels(context, target).SequenceEqual(transparentInput));
                invalidate(effect);
                processor.UpdateFrame(60, 120, 30);
                Draw(context, target, processor.Output);
                require(ReadPixels(context, target).SequenceEqual(transparentInput));
                processor.ClearInput();
            }
        });

        check("雪描画: 柔らかさ0と100で縁の広がりが変わる", () =>
        {
            foreach (var shape in new[] { RainfallShape.SnowRound, RainfallShape.SnowClump })
            {
                var sharp = Render(RainfallParameters.Default with { Shape = shape, SnowSoftness = 0 });
                var soft = Render(RainfallParameters.Default with { Shape = shape, SnowSoftness = 100 });
                Console.WriteLine($"雪の柔らかさ {shape}: 有効ピクセル {AlphaPixelCount(sharp)} / {AlphaPixelCount(soft)}、最大アルファ {MaxAlpha(sharp)} / {MaxAlpha(soft)}");
                require(!sharp.SequenceEqual(soft));
                require(AlphaPixelCount(soft) > AlphaPixelCount(sharp));
                require(MaxAlpha(soft) < MaxAlpha(sharp));
                SavePng($"tmp/snow-{shape.ToString().ToLowerInvariant()}-softness-0.png", sharp);
                SavePng($"tmp/snow-{shape.ToString().ToLowerInvariant()}-softness-100.png", soft);
            }
        });

        check("丸い雪: 大きさ6・48・512と柔らかさ0・50・100の描画再現", () =>
        {
            foreach (var size in new[] { 6f, 48f, 512f })
            foreach (var softness in new[] { 0, 50, 100 })
            {
                var parameters = RainfallParameters.Default with { Shape = RainfallShape.SnowRound, SnowSoftness = softness };
                var pixels = Render(parameters, size: size);
                require(AlphaPixelCount(pixels) > 0);
                _ = Render(parameters with { SnowSoftness = (softness + 50) % 101 }, size: size);
                require(pixels.SequenceEqual(Render(parameters, size: size)));
                SavePng($"tmp/snow-round-{size}-softness-{softness}.png", pixels);
            }
        });

        check("雪描画: 同じフレームを再シークして同じピクセルを再現", () =>
        {
            var parameters = RainfallParameters.Default with { Shape = RainfallShape.SnowCrystal };
            var first = Render(parameters, 3, 47);
            _ = Render(parameters, 1, -20);
            require(first.SequenceEqual(Render(parameters, 3, 47)));
        });

        check("水泡描画: 輪郭の濃さ0・50・100だけが二重輪郭へ作用", () =>
        {
            foreach (var shape in new[] { RainfallShape.Bubble, RainfallShape.LensBubble })
            {
                var parameters = RainfallParameters.Default with
                {
                    Shape = shape,
                    LensReflection = 100,
                    Red = 0.25,
                    Green = 0.75,
                    Blue = 1,
                };
                var none = Render(parameters with { OutlineOpacity = 0 });
                var half = Render(parameters with { OutlineOpacity = 50 });
                var full = Render(parameters with { OutlineOpacity = 100 });
                require(!none.SequenceEqual(half) && !half.SequenceEqual(full));
                require(AlphaSum(none) < AlphaSum(half) && AlphaSum(half) < AlphaSum(full));

                var sample = shape == RainfallShape.Bubble
                    ? (X: (int)(Center.X + 0.27f * 160), Y: (int)(Center.Y + 0.27f * 160))
                    : (X: (int)(Center.X - 0.23f * 160), Y: (int)(Center.Y - 0.28f * 160));
                require(PixelAt(none, sample.X, sample.Y) == PixelAt(half, sample.X, sample.Y));
                require(PixelAt(none, sample.X, sample.Y) == PixelAt(full, sample.X, sample.Y));
            }
        });

        check("雪描画: キャッシュを再利用し設定変更時も上限を保つ", () =>
        {
            using var cachedRenderer = RainfallRenderer.Create(context);
            cachedRenderer.SetInput(input);
            var parameters = RainfallParameters.Default with { Shape = RainfallShape.SnowClump, SnowSoftness = 40 };
            void RenderCached(int variant, double softness)
            {
                cachedRenderer.Render(parameters with { SnowSoftness = softness }, 0, frameFactory: _ =>
                    [new RainfallStroke(Center, Center, 1, 1, 128, 0, false, variant)]);
                Draw(context, target, cachedRenderer.Output);
            }

            RenderCached(0, 40);
            var firstCreationCount = cachedRenderer.SnowCacheCreationCount;
            RenderCached(0, 40);
            require(cachedRenderer.SnowCacheCreationCount == firstCreationCount);
            RenderCached(1, 40);
            require(cachedRenderer.SnowCacheCreationCount == firstCreationCount + 1);
            require(cachedRenderer.SnowCacheEntryCount == 2);
            RenderCached(1, 100);
            require(cachedRenderer.SnowCacheCreationCount == firstCreationCount + 2);
            require(cachedRenderer.SnowCacheEntryCount == 1);
        });

        check("雪描画: 連続更新と資源解放後も借用入力を保持", () =>
        {
            var original = ReadPixels(context, input);
            var ownedRenderer = RainfallRenderer.Create(context);
            ownedRenderer.SetInput(input);
            for (var frame = 0; frame < 64; frame++)
            {
                var shape = frame % 2 == 0 ? RainfallShape.SnowRound : RainfallShape.SnowClump;
                ownedRenderer.Render(RainfallParameters.Default with
                {
                    Shape = shape,
                    SnowSoftness = frame % 101,
                }, frame / 30.0);
            }
            require(ownedRenderer.SnowCacheEntryCount is > 0 and <= 4);
            ownedRenderer.Dispose();
            ownedRenderer.Dispose();
            require(ownedRenderer.SnowCacheEntryCount == 0);
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

    private static IEnumerable<(byte B, byte G, byte R, byte A)> Pixels(byte[] pixels)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            yield return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
        }
    }

    private static (byte B, byte G, byte R, byte A) PixelAt(byte[] pixels, int x, int y)
    {
        var index = (y * Width + x) * 4;
        return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    private static (int Width, int Height) AlphaBounds(byte[] pixels)
    {
        var left = Width;
        var top = Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (pixels[(y * Width + x) * 4 + 3] == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < left ? (0, 0) : (right - left + 1, bottom - top + 1);
    }

    private static int AlphaPixelCount(byte[] pixels) => Pixels(pixels).Count(pixel => pixel.A > 0);
    private static byte MaxAlpha(byte[] pixels) => Pixels(pixels).Max(pixel => pixel.A);
    private static long AlphaSum(byte[] pixels) => Pixels(pixels).Sum(pixel => (long)pixel.A);

    private static void SavePng(string path, byte[] pixels)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void SaveCrystalComparison(string path, IReadOnlyList<(string Name, byte[] Large, byte[] Small)> variants)
    {
        const int previewWidth = 700;
        const int previewHeight = 560;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 30, 48));
            drawing.DrawRectangle(background, null, new System.Windows.Rect(0, 0, previewWidth, previewHeight));
            DrawText("雪の結晶：6形状の描画比較", 24, 16, 22);
            DrawText("大きさ160 px／右下は48 px。プラグインの描画結果です。", 24, 47, 12);
            for (var index = 0; index < variants.Count; index++)
            {
                var x = 24 + index % 3 * 224;
                var y = 84 + index / 3 * 236;
                var item = variants[index];
                DrawText(item.Name, x, y, 15);
                drawing.DrawImage(Crop(item.Large, 192), new System.Windows.Rect(x, y + 20, 192, 192));
                drawing.DrawRectangle(background, null, new System.Windows.Rect(x + 146, y + 157, 68, 68));
                drawing.DrawImage(Crop(item.Small, 64), new System.Windows.Rect(x + 148, y + 159, 64, 64));
                DrawText("48 px", x + 159, y + 216, 10);
            }

            void DrawText(string value, double x, double y, double size)
            {
                var text = new FormattedText(value, System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                    System.Windows.FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), size, Brushes.White, 1);
                drawing.DrawText(text, new System.Windows.Point(x, y));
            }
        }
        var output = new RenderTargetBitmap(previewWidth, previewHeight, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = File.Create(Path.GetFullPath(path));
        encoder.Save(stream);

        static BitmapSource Crop(byte[] pixels, int size)
        {
            var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, pixels, Width * 4);
            return new CroppedBitmap(bitmap, new System.Windows.Int32Rect((Width - size) / 2, (Height - size) / 2, size, size));
        }
    }
}
