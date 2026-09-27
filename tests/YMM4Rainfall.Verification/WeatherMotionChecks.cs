// SPDX-License-Identifier: MPL-2.0
using System.Diagnostics;
using System.Numerics;
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall.Verification;

internal static class WeatherMotionChecks
{
    private static readonly RainfallBounds Bounds = new(0, 0, 1920, 1080);

    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("定常風を基準移動へ加算", () =>
        {
            var parameters = Snow() with
            {
                Speed = 60,
                WindEnabled = true,
                WindAngle = 90,
                WindSpeed = 100,
                WindVariation = 0,
            };
            var wind = WeatherFields.WindVelocity(parameters, 2);
            require(Math.Abs(wind.X - 100) < 1e-10 && Math.Abs(wind.Y) < 1e-10);

            var source = new WeatherFrameSource();
            var first = source.Evaluate(Bounds, 0, 600, 60, "constant-wind", _ => parameters);
            var after = source.Evaluate(Bounds, 120, 600, 60, "constant-wind", _ => parameters);
            var spanX = Bounds.Width + ParticleMargin * 2;
            var spanY = Bounds.Height + ParticleMargin * 2;
            require(first.Length == after.Length && first.Length > 0);
            require(first.Zip(after).All(pair =>
                Math.Abs(WrappedDelta(pair.Second.Head.X - pair.First.Head.X, spanX) - 200) < 0.001 &&
                Math.Abs(WrappedDelta(pair.Second.Head.Y - pair.First.Head.Y, spanY) - 120) < 0.001));
        });

        check("風の無効化・強さ0・角度の周回", () =>
        {
            var parameters = Snow() with { WindEnabled = true, WindAngle = 90, WindSpeed = 100, WindVariation = 0 };
            require(WeatherFields.WindVelocity(parameters, 1) == WeatherFields.WindVelocity(parameters with { WindAngle = 450 }, 1));
            require(WeatherFields.WindVelocity(parameters with { WindEnabled = false }, 1) == default);
            require(WeatherFields.WindVelocity(parameters with { WindSpeed = 0 }, 1) == default);
        });

        check("渦は中心と境界で0、半径3分の1で指定速度", () =>
        {
            var bounds = new RainfallBounds(-500, -500, 500, 500);
            var parameters = Snow() with { SnowMotion = SnowMotionKind.Vortex, VortexRadius = 400, VortexSpeed = 150 };
            require(WeatherFields.VortexVelocity(bounds, parameters, 0, 0) == default);
            var third = WeatherFields.VortexVelocity(bounds, parameters, 400.0 / 3, 0);
            require(Math.Abs(third.X) < 1e-10 && Math.Abs(third.Y - 150) < 1e-10);
            var reverse = WeatherFields.VortexVelocity(bounds, parameters with { VortexClockwise = false }, 400.0 / 3, 0);
            require(reverse == third * -1);
            var near = WeatherFields.VortexVelocity(bounds, parameters, 399.9, 0);
            require(near.LengthSquared < 0.001);
            require(WeatherFields.VortexVelocity(bounds, parameters, 400, 0) == default);
            require(WeatherFields.VortexVelocity(bounds, parameters, 401, 0) == default);
        });

        check("巻き上がりは中央上昇・上側散開・側方下降の循環流", () =>
        {
            var bounds = new RainfallBounds(-500, -500, 500, 500);
            var parameters = Snow() with
            {
                SnowMotion = SnowMotionKind.Updraft,
                UpdraftWidth = 800,
                UpdraftHeight = 400,
                UpdraftSpeed = 200,
            };
            require(WeatherFields.UpdraftVelocity(bounds, parameters, 0, 0) == new WeatherVector(0, -200));
            var upperRight = WeatherFields.UpdraftVelocity(bounds, parameters, 100, -100);
            var upperLeft = WeatherFields.UpdraftVelocity(bounds, parameters, -100, -100);
            var side = WeatherFields.UpdraftVelocity(bounds, parameters, 300, 0);
            require(upperRight.X > 0 && upperLeft.X < 0);
            require(side.Y > 0);
            const double difference = 0.001;
            var divergence =
                (WeatherFields.UpdraftVelocity(bounds, parameters, 100 + difference, -50).X
                    - WeatherFields.UpdraftVelocity(bounds, parameters, 100 - difference, -50).X) / (2 * difference)
                + (WeatherFields.UpdraftVelocity(bounds, parameters, 100, -50 + difference).Y
                    - WeatherFields.UpdraftVelocity(bounds, parameters, 100, -50 - difference).Y) / (2 * difference);
            require(Math.Abs(divergence) < 1e-7);
            var near = WeatherFields.UpdraftVelocity(bounds, parameters, 399.9, 0);
            require(near.LengthSquared < 0.001 * 0.001);
            require(WeatherFields.UpdraftVelocity(bounds, parameters, 400, 0) == default);
            require(WeatherFields.UpdraftVelocity(bounds, parameters, 401, 0) == default);
            require(WeatherFields.UpdraftVelocity(bounds, parameters with { UpdraftSpeed = 0 }, 0, 0) == default);
            var stronger = WeatherFields.UpdraftVelocity(bounds, parameters with { UpdraftSpeed = 400 }, 100, -100);
            require((stronger - upperRight * 2).LengthSquared < 1e-20);

            foreach (var size in new[] { (Width: 8192.0, Height: 1.0), (Width: 1.0, Height: 8192.0) })
            {
                var extreme = parameters with
                {
                    UpdraftWidth = size.Width,
                    UpdraftHeight = size.Height,
                    UpdraftSpeed = 2000,
                };
                foreach (var sample in new[] { (-0.75, -0.75), (-0.25, 0.5), (0.0, 0.0), (0.25, -0.5), (0.75, 0.75) })
                {
                    var velocity = WeatherFields.UpdraftVelocity(bounds, extreme,
                        sample.Item1 * size.Width / 2, sample.Item2 * size.Height / 2);
                    require(double.IsFinite(velocity.X) && double.IsFinite(velocity.Y));
                }
            }
        });

        check("巻き上がりは標準強度でも停止帯へ粒を集積しない", () =>
        {
            foreach (var speed in new[] { 200.0, 400.0 })
            {
                var metrics = SimulateUpdraftGrid(speed);
                require(metrics.Inside < 100);
                require(metrics.NearStill <= 10);
                require(metrics.MaximumBin <= 10);
            }
        });

        check("巻き上がりAnimationは30・60 FPSで再シーク再現", () =>
        {
            foreach (var fps in new[] { 30, 60 })
            {
                var animated = Snow() with
                {
                    Amount = 0.1,
                    Speed = 60,
                    SnowMotion = SnowMotionKind.Updraft,
                };
                RainfallParameters At(long frame)
                {
                    var progress = Math.Clamp(frame / (fps * 4.0), 0, 1);
                    return animated with
                    {
                        LocalCenterX = -100 + 200 * progress,
                        LocalCenterY = 50 - 100 * progress,
                        UpdraftWidth = 400 + 400 * progress,
                        UpdraftHeight = 800 - 400 * progress,
                        UpdraftSpeed = 100 + 300 * progress,
                    };
                }

                var duration = fps * 10L;
                var frames = new long[] { 0, fps, fps * 4L, duration };
                var signature = $"animated-updraft-{fps}";
                var source = new WeatherFrameSource();
                var expected = frames.ToDictionary(frame => frame,
                    frame => source.Evaluate(Bounds, frame, duration, fps, signature, At));
                foreach (var frame in new long[] { duration, fps, fps * 4L, 0, duration })
                {
                    require(source.Evaluate(Bounds, frame, duration, fps, signature, At)
                        .SequenceEqual(expected[frame]));
                    require(new WeatherFrameSource().Evaluate(Bounds, frame, duration, fps, signature, At)
                        .SequenceEqual(expected[frame]));
                }
                require(expected.Values.SelectMany(strokes => strokes).All(IsFinite));
            }
        });

        check("不規則な漂いは滑らかで指定幅内に収まる", () =>
        {
            var irregular = Snow() with { DriftEnabled = true, DriftWidth = 15, DriftRate = 1, DriftIrregularity = 100 };
            var regular = irregular with { DriftIrregularity = 0 };
            var samples = Enumerable.Range(0, 241)
                .Select(frame => WeatherFields.Drift(irregular, 3, frame / 60.0, false)).ToArray();
            require(samples.All(sample => Math.Abs(sample.Offset.X) <= 15.000001));
            require(samples.Zip(samples.Skip(1)).All(pair => Math.Abs(pair.Second.Offset.X - pair.First.Offset.X) < 1));
            require(samples.SequenceEqual(Enumerable.Range(0, 241)
                .Select(frame => WeatherFields.Drift(irregular, 3, frame / 60.0, false))));
            require(!samples.Select(sample => sample.Offset).SequenceEqual(Enumerable.Range(0, 241)
                .Select(frame => WeatherFields.Drift(regular, 3, frame / 60.0, false).Offset)));
            require(WeatherFields.Drift(irregular, 3, 0, true).Offset == default);
        });

        check("水泡とPNGの揺らめきを新しい評価経路でも保持", () =>
        {
            foreach (var shape in new[] { RainfallShape.Bubble, RainfallShape.LensBubble, RainfallShape.Png })
            {
                var parameters = RainfallParameters.Default with
                {
                    Kind = shape == RainfallShape.Png ? WeatherKind.CustomPng : WeatherKind.Bubble,
                    Shape = shape,
                    Amount = 1,
                    ParticleSize = 32,
                    Speed = 0,
                    SpeedVariation = 0,
                    ThicknessVariation = 0,
                    Opacity = 100,
                    SwayEnabled = true,
                    SwayAmplitude = 20,
                    SwayPeriod = 2,
                };
                var moving = new WeatherFrameSource().Evaluate(Bounds, 30, 120, 60, $"sway-{shape}", _ => parameters);
                var still = new WeatherFrameSource().Evaluate(Bounds, 30, 120, 60, $"still-{shape}", _ => parameters with { SwayEnabled = false });
                require(moving.Length == still.Length && moving.Zip(still).Any(pair => pair.First.Head != pair.Second.Head));
                require(moving.SequenceEqual(new WeatherFrameSource().Evaluate(Bounds, 30, 120, 60, $"sway-{shape}", _ => parameters)));
            }

            var following = RainfallParameters.Default with
            {
                Kind = WeatherKind.CustomPng,
                Shape = RainfallShape.Png,
                Amount = 1,
                ParticleSize = 32,
                Speed = 100,
                Angle = 0,
                SpeedVariation = 0,
                ThicknessVariation = 0,
                Opacity = 100,
                SwayEnabled = true,
                SwayAmplitude = 200,
                SwayPeriod = 2,
                FollowDirection = true,
            };
            var followed = new WeatherFrameSource().Evaluate(Bounds, 30, 120, 60, "sway-follow", _ => following);
            require(followed.All(stroke => Math.Abs(stroke.Rotation) < 0.0001));
        });

        check("雨筋の向きへ基準速度と風を合成", () =>
        {
            var parameters = RainfallParameters.Default with
            {
                Amount = 1,
                Speed = 60,
                Angle = 0,
                SpeedVariation = 0,
                WindEnabled = true,
                WindAngle = 90,
                WindSpeed = 100,
                WindVariation = 0,
            };
            var stroke = new WeatherFrameSource().Evaluate(Bounds, 0, 60, 60, "rain-heading", _ => parameters)[0];
            var line = stroke.Head - stroke.Tail;
            require(Math.Abs(line.X / line.Y - 100.0 / 60) < 0.001);

            var stopped = parameters with { Speed = 0, WindSpeed = 0, Angle = 30 };
            var fallback = new WeatherFrameSource().Evaluate(Bounds, 0, 60, 60, "rain-fallback", _ => stopped)[0];
            var fallbackLine = Vector2.Normalize(fallback.Head - fallback.Tail);
            require(Math.Abs(fallbackLine.X - 0.5) < 0.001 && Math.Abs(fallbackLine.Y - Math.Sqrt(3) / 2) < 0.001);
        });

        check("局所作用を離れた後も位置差を保持", () =>
        {
            var bounds = new RainfallBounds(0, 0, 100, 100);
            var localBase = Snow() with
            {
                Amount = 1,
                Speed = 60,
                SpeedVariation = 0,
                SnowMotion = SnowMotionKind.Updraft,
                UpdraftWidth = 8192,
                UpdraftHeight = 8192,
                UpdraftSpeed = 200,
            };
            RainfallParameters LocalAt(long frame) => localBase with { LocalCenterX = frame < 30 ? 0 : 32768 };
            var ordinary = localBase with { SnowMotion = SnowMotionKind.Basic };
            var localSource = new WeatherFrameSource();
            var ordinarySource = new WeatherFrameSource();
            var during = localSource.Evaluate(bounds, 30, 120, 60, "updraft-moving-center", LocalAt);
            var duringBase = ordinarySource.Evaluate(bounds, 30, 120, 60, "updraft-baseline", _ => ordinary);
            var after = localSource.Evaluate(bounds, 60, 120, 60, "updraft-moving-center", LocalAt);
            var afterBase = ordinarySource.Evaluate(bounds, 60, 120, 60, "updraft-baseline", _ => ordinary);
            var span = bounds.Height + ParticleMargin * 2;
            var duringDifference = WrappedDelta(during[0].Head.Y - duringBase[0].Head.Y, span);
            var afterDifference = WrappedDelta(after[0].Head.Y - afterBase[0].Head.Y, span);
            require(Math.Abs(duringDifference) > 1);
            require(Math.Abs(afterDifference - duringDifference) < 0.01);
        });

        check("局所移動は順・逆・ランダムシークで数値一致", () =>
        {
            var parameters = Snow() with
            {
                Amount = 0.1,
                Speed = 80,
                WindEnabled = true,
                WindVariation = 25,
                WindRate = 1,
                DriftEnabled = true,
                DriftIrregularity = 60,
                SnowMotion = SnowMotionKind.Vortex,
                VortexRadius = 800,
                VortexSpeed = 150,
            };
            var frames = new long[] { 0, 6, 60, 900, 3600, 36000 };
            var source = new WeatherFrameSource();
            var expected = frames.ToDictionary(frame => frame,
                frame => source.Evaluate(Bounds, frame, 36000, 60, "seek-vortex", _ => parameters));
            foreach (var frame in new long[] { 36000, 60, 3600, 0, 900, 6, 36000, 0 })
            {
                require(source.Evaluate(Bounds, frame, 36000, 60, "seek-vortex", _ => parameters)
                    .SequenceEqual(expected[frame]));
                require(new WeatherFrameSource().Evaluate(Bounds, frame, 36000, 60, "seek-vortex", _ => parameters)
                    .SequenceEqual(expected[frame]));
            }
        });

        check("局所移動の連続120フレーム評価は新規評価と一致", () =>
        {
            var parameters = Snow() with
            {
                Amount = 1,
                Speed = 60,
                SnowMotion = SnowMotionKind.Updraft,
                UpdraftWidth = 800,
                UpdraftHeight = 400,
                UpdraftSpeed = 200,
            };
            const int fps = 60;
            const long duration = 120;
            var source = new WeatherFrameSource();
            var watch = Stopwatch.StartNew();
            RainfallStroke[] continuous = [];
            for (var frame = 0L; frame <= duration; frame++)
                continuous = source.Evaluate(Bounds, frame, duration, fps, "continuous-updraft", _ => parameters);
            var continuousMilliseconds = watch.ElapsedMilliseconds;
            var fresh = new WeatherFrameSource().Evaluate(
                Bounds, duration, duration, fps, "continuous-updraft", _ => parameters);
            require(continuous.SequenceEqual(fresh));
            require(continuous.All(IsFinite));
            watch.Restart();
            var repeated = source.Evaluate(Bounds, duration, duration, fps, "continuous-updraft", _ => parameters);
            var repeatedMilliseconds = watch.ElapsedMilliseconds;
            require(repeated.SequenceEqual(continuous));
            Console.WriteLine($"巻き上がり約60粒・連続120フレーム {continuousMilliseconds} ms、同時刻再評価 {repeatedMilliseconds} ms");
        });

        check("量と粒サイズのAnimationで既存粒の配置が跳ばない", () =>
        {
            var initial = Snow() with { Amount = 10, Speed = 0, ParticleSize = 6 };
            RainfallParameters At(long frame) => initial with
            {
                Amount = frame < 60 ? 10 : 20,
                ParticleSize = frame < 60 ? 6 : 512,
            };
            var source = new WeatherFrameSource();
            var sparse = source.Evaluate(Bounds, 0, 120, 60, "amount-size-animation", At);
            var dense = source.Evaluate(Bounds, 60, 120, 60, "amount-size-animation", At);
            require(dense.Length > sparse.Length);
            require(sparse.Select(stroke => stroke.Head).SequenceEqual(dense.Take(sparse.Length).Select(stroke => stroke.Head)));
            require(sparse.All(stroke => stroke.Size < dense[0].Size));
        });

        check("大きさのばらつきは速度と位置から独立", () =>
        {
            var uniform = Snow() with { Amount = 10, Speed = 100, ThicknessVariation = 0, SpeedVariation = 75 };
            var varied = uniform with { ThicknessVariation = 100 };
            var uniformFrame = new WeatherFrameSource().Evaluate(Bounds, 120, 120, 60, "size-independent-uniform", _ => uniform);
            var variedFrame = new WeatherFrameSource().Evaluate(Bounds, 120, 120, 60, "size-independent-varied", _ => varied);
            require(uniformFrame.Select(stroke => stroke.Head).SequenceEqual(variedFrame.Select(stroke => stroke.Head)));
            require(!uniformFrame.Select(stroke => stroke.Size).SequenceEqual(variedFrame.Select(stroke => stroke.Size)));
        });

        check("粒ごとの初期角度と回転速度のばらつき", () =>
        {
            var parameters = Snow() with
            {
                Amount = 10,
                Speed = 0,
                RotationSpeed = 100,
                InitialRotationVariation = 100,
                RotationSpeedVariation = 100,
                Shape = RainfallShape.SnowCrystal,
            };
            var first = new WeatherFrameSource().Evaluate(Bounds, 60, 120, 60, "rotation-variation", _ => parameters);
            require(first.Select(stroke => stroke.Rotation).Distinct().Count() > first.Length / 2);
            require(first.Select(stroke => stroke.Variant).Distinct().Order().SequenceEqual(Enumerable.Range(0, 6)));
        });

        check("出現開始と最大4000粒の境界", () =>
        {
            var onset = Snow() with { Amount = 30, OnsetEnabled = true, StartSeconds = 2, AppearanceSeconds = 3 };
            var source = new WeatherFrameSource();
            require(source.Evaluate(Bounds, 119, 600, 60, "onset", _ => onset).Length == 0);
            var complete = source.Evaluate(Bounds, 300, 600, 60, "onset", _ => onset);
            require(complete.Length > 0);
            var maximum = new WeatherFrameSource().Evaluate(
                new RainfallBounds(0, 0, 32768, 32768), 0, 1, 60, "maximum",
                _ => Snow() with { Amount = 100 });
            require(maximum.Length == RainfallSimulation.MaximumStrokeCount);
        });

        check("長尺シークの計算時間を粒数別に記録", () =>
        {
            var basic = Snow() with { Amount = 100, Speed = 60 };
            var watch = Stopwatch.StartNew();
            var distant = new WeatherFrameSource().Evaluate(Bounds, 36000, 36000, 60, "performance-basic", _ => basic);
            var basicMilliseconds = watch.ElapsedMilliseconds;
            require(distant.Length == RainfallSimulation.MaximumStrokeCount && distant.All(IsFinite));

            var local = basic with
            {
                SnowMotion = SnowMotionKind.Updraft,
                UpdraftWidth = 8192,
                UpdraftHeight = 8192,
            };
            var localSource = new WeatherFrameSource();
            watch.Restart();
            _ = localSource.Evaluate(Bounds, 60, 3600, 60, "performance-local", _ => local);
            var localOneSecond = watch.ElapsedMilliseconds;
            watch.Restart();
            _ = localSource.Evaluate(Bounds, 600, 3600, 60, "performance-local", _ => local);
            var localTenSeconds = watch.ElapsedMilliseconds;
            watch.Restart();
            var localFrame = localSource.Evaluate(Bounds, 3600, 3600, 60, "performance-local", _ => local);
            var localSixtySeconds = watch.ElapsedMilliseconds;
            watch.Restart();
            var repeated = localSource.Evaluate(Bounds, 3600, 3600, 60, "performance-local", _ => local);
            var repeatedMilliseconds = watch.ElapsedMilliseconds;
            require(localFrame.Length == RainfallSimulation.MaximumStrokeCount && localFrame.All(IsFinite));
            require(localFrame.SequenceEqual(repeated));
            Console.WriteLine($"天候移動の単体測定: 基本4000粒・10分先 {basicMilliseconds} ms、" +
                $"局所4000粒・1秒先 {localOneSecond} ms、10秒先まで追加 {localTenSeconds} ms、" +
                $"60秒先まで追加 {localSixtySeconds} ms、同時刻再評価 {repeatedMilliseconds} ms");
        });
    }

    private static RainfallParameters Snow() => RainfallParameters.Default with
    {
        Kind = WeatherKind.Snow,
        Shape = RainfallShape.SnowRound,
        Amount = 1,
        ParticleSize = 6,
        Opacity = 100,
        Speed = 60,
        SpeedVariation = 0,
        ThicknessVariation = 0,
        WindVariation = 0,
        WindResponseVariation = 0,
        DriftEnabled = false,
        SnowMotion = SnowMotionKind.Basic,
        RotationSpeed = 0,
    };

    private static bool IsFinite(RainfallStroke stroke)
        => float.IsFinite(stroke.Head.X) && float.IsFinite(stroke.Head.Y)
            && float.IsFinite(stroke.Tail.X) && float.IsFinite(stroke.Tail.Y)
            && float.IsFinite(stroke.Size) && stroke.Size > 0
            && float.IsFinite(stroke.Rotation);

    private static (int Inside, int NearStill, int MaximumBin) SimulateUpdraftGrid(double speed)
    {
        var bounds = new RainfallBounds(-400, -200, 400, 200);
        var parameters = Snow() with
        {
            SnowMotion = SnowMotionKind.Updraft,
            UpdraftWidth = 800,
            UpdraftHeight = 400,
            UpdraftSpeed = speed,
        };
        var points = new List<WeatherVector>(231);
        for (var column = 0; column < 21; column++)
        for (var row = 0; row < 11; row++)
            points.Add(new(-399 + 798.0 * column / 20, -199 + 398.0 * row / 10));

        const int fps = 60;
        const int substeps = 4;
        const double baseSpeed = 60;
        var dt = 1.0 / fps / substeps;
        for (var frame = 0; frame < 30 * fps; frame++)
        for (var substep = 0; substep < substeps; substep++)
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var first = WeatherFields.UpdraftVelocity(bounds, parameters, point.X, point.Y)
                + new WeatherVector(0, baseSpeed);
            var predicted = point + first * dt;
            var last = WeatherFields.UpdraftVelocity(bounds, parameters, predicted.X, predicted.Y)
                + new WeatherVector(0, baseSpeed);
            points[index] = point + (first + last) * (0.5 * dt);
        }

        var inside = points.Where(point => Math.Abs(point.X) < 400 && Math.Abs(point.Y) < 200).ToArray();
        var nearStill = inside.Count(point =>
        {
            var velocity = WeatherFields.UpdraftVelocity(bounds, parameters, point.X, point.Y)
                + new WeatherVector(0, baseSpeed);
            return velocity.LengthSquared < 1;
        });
        var maximumBin = inside
            .GroupBy(point => ((int)Math.Floor((point.X + 400) / 40), (int)Math.Floor((point.Y + 200) / 20)))
            .Select(group => group.Count())
            .DefaultIfEmpty()
            .Max();
        return (inside.Length, nearStill, maximumBin);
    }

    private static double WrappedDelta(double delta, double span)
    {
        delta %= span;
        if (delta > span / 2) delta -= span;
        if (delta < -span / 2) delta += span;
        return delta;
    }

    private const double ParticleMargin = RainfallParameters.MaximumParticleSize * 2 + 2;
}
