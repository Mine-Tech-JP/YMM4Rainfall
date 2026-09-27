// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.UndoRedo;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class SizeSpeedLinkChecks
{
    private static readonly RainfallBounds Bounds = new(0, 0, 1920, 1080);

    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        foreach (var kind in new[] { WeatherKind.Snow, WeatherKind.Bubble, WeatherKind.CustomPng })
        foreach (var shape in RainfallAppearanceBank.Shapes(kind))
        {
            var p = RainfallParameters.Default with
            {
                Kind = kind, Shape = shape, Amount = 2, ParticleSize = 20,
                PngSourceSize = shape == RainfallShape.Png ? 32 : 0,
                Speed = 100, Angle = 0, ThicknessVariation = 80, SpeedVariation = 60,
                RainSizeMotionLinked = true,
            };
            check($"サイズ速度連動: {kind}/{shape}の方向・境界値・OFF", () =>
            {
                foreach (var angle in kind == WeatherKind.Snow ? new[] { 0 } : new[] { 0, 90, 180, 270 })
                foreach (var sizeVariation in new[] { 0, 80, 100 })
                foreach (var speedVariation in new[] { 0, 60, 100 })
                foreach (var speed in new[] { 0, 100 })
                {
                    var sample = p with { Angle = angle, ThicknessVariation = sizeVariation,
                        SpeedVariation = speedVariation, Speed = speed };
                    var before = Frame(sample, 0);
                    var after = Frame(sample, 60);
                    require(before.Length > 0 && before.Length <= 4000);
                    for (var i = 0; i < before.Length; i++)
                    {
                        var relative = before[i].Size / sample.Normalize().ParticleSize;
                        var expected = WeatherFields.Direction(speed * (1 + (relative - 1) * speedVariation / 100), angle);
                        var actual = Delta(after[i].Head - before[i].Head, sample);
                        require(Math.Abs(actual.X - expected.X) < 0.002 && Math.Abs(actual.Y - expected.Y) < 0.002);
                    }
                }
                var off = p with { RainSizeMotionLinked = false };
                require(Frame(off, 60).Select(s => s.Head).SequenceEqual(
                    Frame(off with { ThicknessVariation = 0 }, 60).Select(s => s.Head)));
            });

            check($"サイズ速度連動: {kind}/{shape}のAnimation積分・逆シーク・出現開始", () =>
            {
                RainfallParameters At(long f) => p with { ThicknessVariation = f / 120.0 * 80 };
                var source = new WeatherFrameSource();
                var first = source.Evaluate(Bounds, 0, 120, 60, "animated", At);
                var last = source.Evaluate(Bounds, 120, 120, 60, "animated", At);
                for (var i = 0; i < first.Length; i++)
                {
                    var relative = last[i].Size / p.Normalize().ParticleSize - 1;
                    var delta = Delta(last[i].Head - first[i].Head, p);
                    require(Math.Abs(delta.Y - (200 + 100 * relative * 0.6)) < 0.002);
                }
                foreach (var onset in new[] { false, true })
                {
                    RainfallParameters Moving(long f) => At(f) with
                    {
                        OnsetEnabled = onset, StartSeconds = 0.25, AppearanceSeconds = 0.5,
                        Speed = 100 + f, SpeedVariation = f / 120.0 * 100,
                        PngScale = 100 + f, PngMaximumScale = 1000,
                    };
                    foreach (var f in new long[] { 120, 30, 0, 119, 60, 120 })
                    {
                        var actual = source.Evaluate(Bounds, f, 120, 60, "onset-" + onset, Moving);
                        var fresh = new WeatherFrameSource().Evaluate(Bounds, f, 120, 60, "fresh", Moving);
                        require(actual.SequenceEqual(fresh));
                    }
                }
                // 基準寸法だけの変更では移動量を変えません。PNGの配置余白は同じにします。
                require(Frame(p, 60).Select(s => s.Head).SequenceEqual(
                    Frame(p with { ParticleSize = 40, PngScale = 200 }, 60).Select(s => s.Head)));
            });
        }

        check("サイズ速度連動: 雪の風・漂い・局所運動と水泡の揺らめきは速度0で不変", () =>
        {
            foreach (var kind in new[] { WeatherKind.Snow, WeatherKind.Bubble, WeatherKind.CustomPng })
            foreach (var motion in Enum.GetValues<SnowMotionKind>())
            {
                var p = RainfallParameters.Default with
                {
                    Kind = kind, Shape = kind == WeatherKind.Snow ? RainfallShape.SnowRound : RainfallShape.Bubble,
                    Amount = 1, Speed = 0, WindEnabled = true, WindSpeed = 50,
                    WindResponseVariation = 75, ThicknessVariation = 80,
                    DriftEnabled = true, SnowMotion = motion, SwayEnabled = true,
                };
                require(Frame(p, 120).SequenceEqual(Frame(p with { RainSizeMotionLinked = true }, 120)));
            }
        });

        check("サイズ速度連動: 全10見た目の保存・Undo/Redo・署名とキャッシュ更新", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var expected = new Dictionary<(WeatherKind, RainfallShape), bool>();
            var index = 0;
            foreach (var kind in Enum.GetValues<WeatherKind>())
            foreach (var shape in RainfallAppearanceBank.Shapes(kind))
            {
                effect.Kind = kind; effect.Shape = shape;
                require(!effect.RainSizeMotionLinked);
                effect.RainSizeMotionLinked = index++ % 2 == 0;
                expected[(kind, shape)] = effect.RainSizeMotionLinked;
            }
            effect = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            foreach (var pair in expected)
            {
                effect.Kind = pair.Key.Item1; effect.Shape = pair.Key.Item2;
                require(effect.RainSizeMotionLinked == pair.Value);
                require(effect.GetParameters(0, 120, 60).RainSizeMotionLinked == pair.Value);
                var signature = effect.MotionSignature(120, 60);
                var source = new WeatherFrameSource();
                source.Evaluate(Bounds, 120, 120, 60, signature, f => effect.GetParameters(f, 120, 60));
                var manager = new UndoRedoManager();
                manager.Subscribe(effect);
                try
                {
                    effect.RainSizeMotionLinked = !pair.Value; manager.Record();
                    require(signature != effect.MotionSignature(120, 60));
                    var actual = source.Evaluate(Bounds, 120, 120, 60, effect.MotionSignature(120, 60), f => effect.GetParameters(f, 120, 60));
                    require(actual.SequenceEqual(new WeatherFrameSource().Evaluate(Bounds, 120, 120, 60, "fresh", f => effect.GetParameters(f, 120, 60))));
                    manager.UndoAsync().GetAwaiter().GetResult();
                    require(effect.RainSizeMotionLinked == pair.Value && effect.MotionSignature(120, 60) == signature);
                    manager.RedoAsync().GetAwaiter().GetResult();
                    require(effect.RainSizeMotionLinked != pair.Value);
                }
                finally { manager.UnSubscribe(effect); }
            }
        });
    }

    private static RainfallStroke[] Frame(RainfallParameters p, long f) =>
        new WeatherFrameSource().Evaluate(Bounds, f, 120, 60, "constant", _ => p);

    private static Vector2 Delta(Vector2 value, RainfallParameters p)
    {
        var margin = (p.Shape == RainfallShape.Png ? p.ParticleSizeLimit : RainfallParameters.MaximumParticleSize) * 2 + 2;
        return new((float)Wrap(value.X, Bounds.Width + margin * 2), (float)Wrap(value.Y, Bounds.Height + margin * 2));
    }

    private static double Wrap(double value, double span)
    {
        if (value > span / 2) return value - span;
        if (value < -span / 2) return value + span;
        return value;
    }
}
