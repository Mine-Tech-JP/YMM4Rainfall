// SPDX-License-Identifier: MPL-2.0
using YMM4Rainfall.Simulation;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class RainSizeMotionChecks
{
    private static readonly RainfallBounds Bounds = new(0, 0, 1920, 1080);

    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        foreach (var shape in new[] { RainfallShape.Streak, RainfallShape.Circle, RainfallShape.CartoonDrop })
        {
            check($"雨のサイズ連動: {shape}の小粒は遅く風へ強く反応", () =>
            {
                var parameters = Rain(shape);
                var source = new WeatherFrameSource();
                var before = source.Evaluate(Bounds, 0, 120, 60, "rain-size-linked", _ => parameters);
                var after = source.Evaluate(Bounds, 60, 120, 60, "rain-size-linked", _ => parameters);
                var observations = before.Select((stroke, index) => new
                {
                    Size = shape == RainfallShape.Streak ? stroke.Thickness : stroke.Size,
                    X = Delta(after[index].Head.X - stroke.Head.X, Bounds.Width + Margin(shape) * 2),
                    Y = Delta(after[index].Head.Y - stroke.Head.Y, Bounds.Height + Margin(shape) * 2),
                }).OrderBy(item => item.Size).ToArray();
                var small = observations.Take(observations.Length / 4).ToArray();
                var large = observations.TakeLast(observations.Length / 4).ToArray();
                require(small.Length > 10 && large.Min(item => item.Y) > small.Max(item => item.Y));
                require(small.Min(item => item.X) > large.Max(item => item.X));
                require(observations.All(item => item.Y > 0 && item.X > 0));
            });
        }

        check("雨のサイズ連動: 大きさの差0なら速度と風の差も0", () =>
        {
            var parameters = Rain(RainfallShape.Circle) with { ThicknessVariation = 0 };
            var source = new WeatherFrameSource();
            var before = source.Evaluate(Bounds, 0, 120, 60, "rain-size-uniform", _ => parameters);
            var after = source.Evaluate(Bounds, 60, 120, 60, "rain-size-uniform", _ => parameters);
            for (var index = 0; index < before.Length; index++)
            {
                require(Math.Abs(Delta(after[index].Head.X - before[index].Head.X, Bounds.Width + Margin(parameters.Shape) * 2) - 60) < 0.001);
                require(Math.Abs(Delta(after[index].Head.Y - before[index].Head.Y, Bounds.Height + Margin(parameters.Shape) * 2) - 120) < 0.001);
            }
        });

        check("雨のサイズ連動: 無効時は大きさと移動を独立して調整", () =>
        {
            var parameters = Rain(RainfallShape.Circle) with { RainSizeMotionLinked = false };
            var varied = new WeatherFrameSource().Evaluate(Bounds, 60, 120, 60, "rain-independent-varied", _ => parameters);
            var uniform = new WeatherFrameSource().Evaluate(Bounds, 60, 120, 60, "rain-independent-uniform",
                _ => parameters with { ThicknessVariation = 0 });
            require(varied.Select(stroke => stroke.Head).SequenceEqual(uniform.Select(stroke => stroke.Head)));
            require(!varied.Select(stroke => stroke.Size).SequenceEqual(uniform.Select(stroke => stroke.Size)));
        });

        check("雨のサイズ連動: 大きさのばらつきAnimationを積分し逆順シークを再現", () =>
        {
            var parameters = Rain(RainfallShape.Circle);
            RainfallParameters At(long frame) => parameters with { ThicknessVariation = frame / 60.0 * 80 };
            var source = new WeatherFrameSource();
            var before = source.Evaluate(Bounds, 0, 60, 30, "rain-size-animation", At);
            var last = source.Evaluate(Bounds, 60, 60, 30, "rain-size-animation", At);
            for (var index = 0; index < last.Length; index++)
            {
                var relativeSize = (last[index].Size / parameters.ParticleSize - 1) / 0.8;
                var x = Delta(last[index].Head.X - before[index].Head.X, Bounds.Width + Margin(parameters.Shape) * 2);
                var y = Delta(last[index].Head.Y - before[index].Head.Y, Bounds.Height + Margin(parameters.Shape) * 2);
                // 2秒間のサイズばらつき0→80%の積分値は0.8秒です。
                require(Math.Abs(x - (120 - relativeSize * 60 * 0.5 * 0.8)) < 0.001);
                require(Math.Abs(y - (240 + relativeSize * 120 * 0.6 * 0.8)) < 0.001);
            }
            foreach (var frame in new long[] { 15, 60, 0, 59, 30 })
            {
                var current = source.Evaluate(Bounds, frame, 60, 30, "rain-size-animation", At);
                var fresh = new WeatherFrameSource().Evaluate(Bounds, frame, 60, 30, "rain-size-animation", At);
                require(current.SequenceEqual(fresh));
            }
        });

        check("雨のサイズ連動: 保存・種類別保持・再計算署名", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var before = effect.MotionSignature(120, 60);
            effect.RainSizeMotionLinked = true;
            require(effect.GetParameters(0, 120, 60).RainSizeMotionLinked);
            require(effect.MotionSignature(120, 60) != before);
            effect.Kind = WeatherKind.Snow;
            require(!effect.GetParameters(0, 120, 60).RainSizeMotionLinked);
            var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            restored.Kind = WeatherKind.Rain;
            require(restored.RainSizeMotionLinked);
        });
    }

    private static RainfallParameters Rain(RainfallShape shape) => RainfallParameters.Default with
    {
        Kind = WeatherKind.Rain, Shape = shape, Amount = 10, Angle = 0, Speed = 120,
        ParticleSize = 20, ThicknessVariation = 80, SpeedVariation = 60,
        WindEnabled = true, WindAngle = 90, WindSpeed = 60, WindVariation = 0,
        WindResponseVariation = 50, RainSizeMotionLinked = true,
    };

    private static double Margin(RainfallShape shape) => shape == RainfallShape.Streak
        ? RainfallParameters.MaximumLength * 1.4 + RainfallParameters.MaximumThickness * 2
        : RainfallParameters.MaximumParticleSize * 2 + 2;

    private static double Delta(double value, double span)
    {
        if (value > span / 2) return value - span;
        if (value < -span / 2) return value + span;
        return value;
    }
}
