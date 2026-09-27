// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using System.Text.Json.Nodes;
using YMM4Rainfall.Rendering;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.UndoRedo;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class MotionBlurChecks
{
    private static readonly RainfallBounds Bounds = new(0, 0, 1920, 1080);

    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("ブラー設定: 全10見た目の独立保存・途中点・欠落値の補完", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var index = 0;
            foreach (var kind in Enum.GetValues<WeatherKind>())
            foreach (var shape in RainfallAppearanceBank.Shapes(kind))
            {
                effect.Kind = kind; effect.Shape = shape;
                require(!effect.MotionBlurEnabled && effect.MotionBlurStrength.GetFirstValue() == 50);
                require(effect.MotionBlurMode == MotionBlurMode.Symmetric);
                effect.MotionBlurEnabled = index % 2 == 0;
                effect.MotionBlurMode = index % 3 == 0 ? MotionBlurMode.Trailing : MotionBlurMode.Symmetric;
                FeatureChecks.SetLinear(effect.MotionBlurStrength, index, 100 - index);
                index++;
            }
            effect.SetAnimationParameters(120, 60);
            var keys = new KeyFrames(); effect.SetKeyFrames(keys); keys.Insert(60);
            index = 0;
            foreach (var kind in Enum.GetValues<WeatherKind>())
            foreach (var shape in RainfallAppearanceBank.Shapes(kind))
            {
                effect.Kind = kind; effect.Shape = shape;
                require(effect.MotionBlurStrength.Values.Count == 3);
                effect.MotionBlurStrength.Values[1].Value = 20 + index++;
            }
            var json = YmmJson.GetJsonText(effect);
            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            require(restored.IsSupported && restored.SchemaVersion == 2);
            // キーフレーム位置はホストが所有し、読込後にエフェクトへ接続します。
            restored.SetAnimationParameters(120, 60);
            var restoredKeys = new KeyFrames(); restoredKeys.Insert(60); restored.SetKeyFrames(restoredKeys);
            index = 0;
            foreach (var kind in Enum.GetValues<WeatherKind>())
            foreach (var shape in RainfallAppearanceBank.Shapes(kind))
            {
                restored.Kind = kind; restored.Shape = shape;
                effect.Kind = kind; effect.Shape = shape;
                require(restored.MotionBlurEnabled == (index % 2 == 0));
                require(restored.MotionBlurMode == (index % 3 == 0 ? MotionBlurMode.Trailing : MotionBlurMode.Symmetric));
                require(restored.MotionBlurStrength.Values.Count == 3 && restored.MotionBlurStrength.Values[1].Value == 20 + index);
                foreach (var frame in new long[] { 0, 30, 60, 90, 120 })
                    require(restored.GetParameters(frame, 120, 60).MotionBlurStrength == effect.GetParameters(frame, 120, 60).MotionBlurStrength);
                index++;
            }
            var root = JsonNode.Parse(json)!.AsObject();
            foreach (var name in new[] { "RainAppearances", "SnowAppearances", "BubbleAppearances", "PngAppearances" })
            foreach (var variant in root[name]!["Variants"]!.AsArray())
            {
                variant!.AsObject().Remove("MotionBlurEnabled");
                variant.AsObject().Remove("MotionBlurStrength");
                variant.AsObject().Remove("MotionBlurMode");
            }
            var legacy = YmmJson.LoadFromText<RainfallEffect>(root.ToJsonString())!;
            foreach (var kind in Enum.GetValues<WeatherKind>())
            foreach (var shape in RainfallAppearanceBank.Shapes(kind))
            {
                legacy.Kind = kind; legacy.Shape = shape;
                require(legacy.IsSupported && !legacy.MotionBlurEnabled && legacy.MotionBlurStrength.GetFirstValue() == 50);
                require(legacy.MotionBlurMode == MotionBlurMode.Symmetric);
            }
        });

        check("後方ブラー設定: 方式だけ欠落したalpha.4を前後で読み、Undo/Redoと設定例で保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow, MotionBlurEnabled = true };
            effect.MotionBlurStrength.SetFirstValue(65);
            var root = JsonNode.Parse(YmmJson.GetJsonText(effect))!.AsObject();
            foreach (var name in new[] { "RainAppearances", "SnowAppearances", "BubbleAppearances", "PngAppearances" })
                foreach (var variant in root[name]!["Variants"]!.AsArray()) variant!.AsObject().Remove("MotionBlurMode");
            effect = YmmJson.LoadFromText<RainfallEffect>(root.ToJsonString())!;
            require(effect.MotionBlurMode == MotionBlurMode.Symmetric && effect.MotionBlurEnabled && effect.MotionBlurStrength.GetFirstValue() == 65);
            var before = effect.MotionSignature(120, 60);
            var manager = new UndoRedoManager(); manager.Subscribe(effect);
            try
            {
                effect.MotionBlurMode = MotionBlurMode.Trailing; manager.Record();
                require(effect.MotionSignature(120, 60) != before);
                manager.UndoAsync().GetAwaiter().GetResult(); require(effect.MotionBlurMode == MotionBlurMode.Symmetric);
                manager.RedoAsync().GetAwaiter().GetResult(); require(effect.MotionBlurMode == MotionBlurMode.Trailing);
                require(WeatherPresetCatalog.All.Single(p => p.Key == "quiet-snow").ApplyTo(effect));
                require(effect.MotionBlurMode == MotionBlurMode.Trailing && effect.MotionBlurStrength.GetFirstValue() == 65);
                require(effect.GetParameters(30, 120, 60).MotionBlurMode == MotionBlurMode.Trailing);
            }
            finally { manager.UnSubscribe(effect); }
        });

        check("ブラー設定: Undo/Redo・署名変更・細雪の設定例で無効化", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            var initial = effect.MotionSignature(120, 60);
            var manager = new UndoRedoManager(); manager.Subscribe(effect);
            try
            {
                effect.MotionBlurEnabled = true; manager.Record();
                require(effect.MotionSignature(120, 60) != initial);
                var enabled = effect.MotionSignature(120, 60);
                effect.MotionBlurStrength.SetFirstValue(75); manager.Record();
                require(effect.MotionSignature(120, 60) != enabled);
                manager.UndoAsync().GetAwaiter().GetResult(); require(effect.MotionBlurStrength.GetFirstValue() == 50);
                manager.UndoAsync().GetAwaiter().GetResult(); require(!effect.MotionBlurEnabled);
                manager.RedoAsync().GetAwaiter().GetResult(); manager.RedoAsync().GetAwaiter().GetResult();
                require(effect.MotionBlurEnabled && effect.MotionBlurStrength.GetFirstValue() == 75);
                require(WeatherPresetCatalog.All.Single(p => p.Key == "quiet-snow").ApplyTo(effect));
                require(!effect.MotionBlurEnabled && effect.MotionBlurStrength.GetFirstValue() == 75);
            }
            finally { manager.UnSubscribe(effect); }
        });

        check("ブラー移動: 全見た目の一定速度・静止・OFFの配置保持・FPS", () =>
        {
            foreach (var kind in Enum.GetValues<WeatherKind>())
            foreach (var shape in RainfallAppearanceBank.Shapes(kind))
            foreach (var fps in new[] { 24, 30, 60, 120 })
            {
                var p = RainfallParameters.Default with { Kind = kind, Shape = shape, Amount = 1,
                    Speed = 900, SpeedVariation = 0, Angle = 45, MotionBlurEnabled = true, MotionBlurStrength = 100 };
                var strokes = Frame(p, fps, fps);
                var expected = WeatherFields.Direction(900, kind == WeatherKind.Snow ? 0 : 45);
                require(strokes.All(s => Math.Abs(s.BlurVelocity.X - expected.X) < 0.002 && Math.Abs(s.BlurVelocity.Y - expected.Y) < 0.002));
                require(strokes.Select(s => s with { BlurVelocity = default }).SequenceEqual(Frame(p with { MotionBlurEnabled = false }, fps, fps)));
                require(Frame(p with { Speed = 0 }, fps, fps).All(s => s.BlurVelocity.Length() < 0.001));
                require(Frame(p with { MotionBlurStrength = 0 }, fps, fps).SequenceEqual(Frame(p with { MotionBlurEnabled = false }, fps, fps)));
                require(strokes.All(s => Math.Abs(RainfallBlurRenderer.StandardDeviation(s, p) - 20) < 0.001));
            }
        });

        check("ブラー移動: 風・漂い・揺らめき・渦・巻き上がりの実変位と一致", () =>
        {
            foreach (var kind in new[] { WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Bubble, WeatherKind.CustomPng })
            foreach (var motion in Enum.GetValues<SnowMotionKind>())
            {
                RainfallParameters At(long f) => RainfallParameters.Default with
                {
                    Kind = kind, Shape = kind == WeatherKind.Snow ? RainfallShape.SnowRound : RainfallShape.Bubble,
                    Amount = 1, Speed = 0, WindEnabled = true, WindSpeed = 150,
                    SnowMotion = motion, DriftEnabled = true, DriftWidth = 10 + f / 6.0,
                    SwayEnabled = true, SwayAmplitude = 100, SwayPeriod = 1,
                    MotionBlurEnabled = true, MotionBlurStrength = 50,
                };
                var source = new WeatherFrameSource();
                var before = source.Evaluate(Bounds, 59, 120, 60, "fields", At);
                var after = source.Evaluate(Bounds, 60, 120, 60, "fields", At);
                var matched = 0;
                for (var i = 0; i < after.Length; i++)
                {
                    var delta = after[i].Head - before[i].Head;
                    if (delta.Length() > 100) continue;
                    require(Vector2.Distance(delta * 60, after[i].BlurVelocity) < 0.035);
                    matched++;
                }
                require(matched > 0 && after.Any(s => s.BlurVelocity.Length() > 1));
            }
        });

        check("ブラー移動: 出現開始・循環・先頭末尾・逆シーク・署名更新", () =>
        {
            foreach (var onset in new[] { false, true })
            foreach (var fps in new[] { 30, 60 })
            {
                var source = new WeatherFrameSource();
                RainfallParameters At(long f) => RainfallParameters.Default with { Kind = WeatherKind.Snow,
                    Shape = RainfallShape.SnowRound, Amount = 1, Speed = 900, SpeedVariation = 0,
                    DriftEnabled = true, SnowMotion = SnowMotionKind.Vortex, OnsetEnabled = onset,
                    StartSeconds = 0, AppearanceSeconds = 0.1, MotionBlurEnabled = true };
                foreach (var f in new long[] { 240, 1, 0, 59, 120, 60, 240 })
                {
                    var actual = source.Evaluate(Bounds, f, 240, fps, "sequence", At);
                    var expected = new WeatherFrameSource().Evaluate(Bounds, f, 240, fps, "fresh", At);
                    require(actual.SequenceEqual(expected));
                    require(actual.All(s => float.IsFinite(s.BlurVelocity.X) && s.BlurVelocity.Length() < 4000));
                }
                var off = source.Evaluate(Bounds, 60, 240, fps, "off", f => At(f) with { MotionBlurEnabled = false });
                require(off.All(s => s.BlurVelocity == default));
            }
        });

        check("ブラー境界: 無効・0%・非有限値・最大強度・極小PNG", () =>
        {
            var p = RainfallParameters.Default with { MotionBlurEnabled = true, MotionBlurStrength = 100 };
            var stroke = new RainfallStroke(default, default, 1, 1) { BlurVelocity = new(1e20f, 1e20f) };
            require(RainfallBlurRenderer.StandardDeviation(stroke, p) == 32);
            require(RainfallBlurRenderer.StandardDeviation(stroke, p with { MotionBlurEnabled = false }) == 0);
            require(RainfallBlurRenderer.StandardDeviation(stroke, p with { MotionBlurStrength = 0 }) == 0);
            require(RainfallBlurRenderer.StandardDeviation(stroke with { BlurVelocity = new(float.NaN, 0) }, p) == 0);
            require((p with { MotionBlurStrength = double.NaN }).Normalize().MotionBlurStrength == 50);
            require((p with { MotionBlurStrength = -1 }).Normalize().MotionBlurStrength == 0);
            require((p with { MotionBlurStrength = 101 }).Normalize().MotionBlurStrength == 100);
            require((p with { MotionBlurMode = (MotionBlurMode)99 }).Normalize().MotionBlurMode == MotionBlurMode.Symmetric);
            var png = (p with { Shape = RainfallShape.Png, PngSourceSize = 1, PngScale = 100, PngMaximumScale = 100 }).Normalize();
            require(RainfallBlurRenderer.StandardDeviation(stroke with { Size = 1 }, png) < 1);
        });
    }

    private static RainfallStroke[] Frame(RainfallParameters p, long f, int fps) =>
        new WeatherFrameSource().Evaluate(Bounds, f, fps * 2, fps, "constant", _ => p);
}
