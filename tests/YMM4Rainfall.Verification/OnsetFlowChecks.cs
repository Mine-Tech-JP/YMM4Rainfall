// SPDX-License-Identifier: MPL-2.0
using System.Diagnostics;
using System.Numerics;
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall.Verification;

internal static class OnsetFlowChecks
{
    private static readonly RainfallBounds Bounds = new(0, 0, 1920, 1080);

    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("出現開始の先頭: 少量でも全方向で早く入り、シーク順によらず再現", () =>
        {
            foreach (var angle in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
            foreach (var seed in new[] { 0, 1, 42 })
            foreach (var ramp in new[] { false, true })
            {
                var p = Snow() with { Amount = 0.01, Kind = WeatherKind.Rain,
                    Shape = RainfallShape.Circle, ParticleSize = 6, Speed = 60,
                    Angle = angle, Seed = seed, StartSeconds = 2,
                    AppearanceSeconds = 10, RampEnabled = ramp, RampSeconds = 10 };
                var source = new WeatherFrameSource();
                RainfallStroke[] Frame(long frame) => source.Evaluate(Bounds, frame, 1800, 60, "lead", _ => p);
                require(Frame(119).Length == 0);
                require(Frame(120).All(stroke => !Inside(stroke.Head)));
                require(Frame(138).Any(stroke => Inside(stroke.Head)));
                _ = Frame(1800);
                require(Frame(138).SequenceEqual(new WeatherFrameSource().Evaluate(Bounds, 138,
                    1800, 60, "fresh-lead", _ => p)));
                require(new WeatherFrameSource().Evaluate(Bounds, 600, 1800, 60, "stopped-lead",
                    _ => p with { Speed = 0 }).All(stroke => !Inside(stroke.Head)));
            }
        });

        check("出現開始の流れ: 開始前は非表示、全方向で画面外から入る", () =>
        {
            foreach (var angle in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
            {
                var parameters = Snow() with
                {
                    Kind = WeatherKind.Rain, Shape = RainfallShape.Circle, Angle = angle,
                    Speed = 240, StartSeconds = 2, AppearanceSeconds = 0,
                };
                var source = new WeatherFrameSource();
                RainfallStroke[] Frame(long frame) => source.Evaluate(Bounds, frame, 600, 60,
                    $"entry-{angle}", _ => parameters);
                require(Frame(119).Length == 0);
                require(Frame(120).Length > 0 && Frame(120).All(stroke => !Inside(stroke.Head)));
                var early = Frame(180).Where(stroke => Inside(stroke.Head)).ToArray();
                require(early.Length > 0);
                var displacement = WeatherFields.Direction(parameters.Speed, angle);
                require(early.All(stroke => !Inside(new Vector2(
                    (float)(stroke.Head.X - displacement.X), (float)(stroke.Head.Y - displacement.Y)))));
            }
        });

        check("出現開始の流れ: 斜めの先頭が一つの面になり、通常の密度へ途切れずつながる", () =>
        {
            var bounds = new RainfallBounds(0, 0, 640, 480);
            foreach (var angle in new[] { 30, 45, 135, 225, 315 })
            foreach (var rise in new[] { 0.0, 0.1 })
            foreach (var windOnly in new[] { false, true })
            {
                var p = Snow() with { Kind = WeatherKind.Rain, Shape = RainfallShape.Circle,
                    Angle = angle, Speed = 120, Amount = 100, AppearanceSeconds = rise };
                if (windOnly) p = p with { Kind = WeatherKind.Snow, Speed = 0,
                    WindEnabled = true, WindSpeed = 120, WindAngle = angle, WindVariation = 0,
                    WindResponseVariation = 0 };
                var direction = WeatherFields.Direction(1, angle);
                double Projection(Vector2 point) => point.X * direction.X + point.Y * direction.Y;
                var minimum = new[] { new Vector2(0, 0), new Vector2(640, 0),
                    new Vector2(0, 480), new Vector2(640, 480) }.Min(Projection);
                var source = new WeatherFrameSource();
                RainfallStroke[] Frame(long frame) => source.Evaluate(bounds, frame, 3600, 60,
                    $"diagonal-{angle}-{rise}", _ => p);
                require(Frame(0).All(stroke => !Inside(stroke.Head, bounds)));
                // 立ち上がりがある場合の先頭1粒だけは、画面端から先行して入ります。
                var early = Frame(120).Skip(rise > 0 ? 1 : 0).Where(stroke => Inside(stroke.Head, bounds)).ToArray();
                require(early.Length > 0);
                require(early.All(stroke => Projection(stroke.Head) <= minimum + 240 + 0.01));
                for (var frame = 600L; frame <= 3600; frame += 60)
                {
                    var visible = Frame(frame).Count(stroke => Inside(stroke.Head, bounds));
                    require(visible is > 90 and < 210);
                }
                require(Frame(120).SequenceEqual(new WeatherFrameSource().Evaluate(bounds, 120,
                    3600, 60, "fresh-diagonal", _ => p)));
            }
        });

        check("出現開始の流れ: 0秒でも通常の粒配置を保ち、そのまま定常状態へつながる", () =>
        {
            foreach (var parameters in new[]
            {
                Snow(),
                Snow() with { Kind = WeatherKind.Rain, Shape = RainfallShape.Streak, Speed = 900 },
                Snow() with { Kind = WeatherKind.Bubble, Shape = RainfallShape.Bubble, Speed = 100, Angle = 180 },
                Snow() with { Kind = WeatherKind.CustomPng, Shape = RainfallShape.Png, Speed = 150, Angle = 90 },
            })
            {
                var source = new WeatherFrameSource();
                var normal = new WeatherFrameSource();
                foreach (var frame in new long[] { 0, 30, 60, 120, 360, 1800, 3600, 7200 })
                {
                    var actual = source.Evaluate(Bounds, frame, 7200, 60, "onset", _ => parameters);
                    var expected = normal.Evaluate(Bounds, frame, 7200, 60, "normal", _ => parameters with { OnsetEnabled = false });
                    var normalSet = expected.ToHashSet();
                    require(actual.All(normalSet.Contains));
                    if (frame >= 1800) require(actual.SequenceEqual(expected));
                }
            }
        });

        check("出現開始の流れ: 短い立ち上がりから複数周まで帯状集中と途切れがない", () =>
        {
            foreach (var seed in new[] { 1, 17, 2026 })
            foreach (var rise in new[] { 0.0, 0.1, 1, 5 })
            {
                var parameters = Snow() with { Seed = seed, AppearanceSeconds = rise };
                var source = new WeatherFrameSource();
                for (var frame = 360L; frame <= 7200; frame += 15)
                {
                    var visible = source.Evaluate(Bounds, frame, 7200, 60, $"flow-{seed}-{rise}", _ => parameters)
                        .Where(stroke => Inside(stroke.Head)).ToArray();
                    require(visible.Length >= 10);
                    var peak = visible.GroupBy(stroke => (int)(stroke.Head.Y / 30)).Max(group => group.Count());
                    require(peak < 35);
                    if (frame >= 1800)
                    {
                        require(visible.Length is > 120 and < 300);
                        require(visible.Max(stroke => stroke.Head.Y) - visible.Min(stroke => stroke.Head.Y) > 900);
                    }
                }
            }
        });

        check("出現開始の流れ: 均等とポツポツからを分け、時間0では同じ流れ", () =>
        {
            var uniform = Snow() with { Amount = 100, AppearanceSeconds = 10 };
            var sparse = uniform with { RampEnabled = true, RampSeconds = 10 };
            var normalSource = new WeatherFrameSource();
            var sparseSource = new WeatherFrameSource();
            var normalCount = normalSource.Evaluate(Bounds, 300, 3600, 60, "uniform", _ => uniform).Count(stroke => Inside(stroke.Head));
            var sparseCount = sparseSource.Evaluate(Bounds, 300, 3600, 60, "sparse", _ => sparse).Count(stroke => Inside(stroke.Head));
            require(sparseCount > 0 && sparseCount < normalCount);
            var zero = uniform with { AppearanceSeconds = 0 };
            require(new WeatherFrameSource().Evaluate(Bounds, 600, 3600, 60, "zero", _ => zero)
                .SequenceEqual(new WeatherFrameSource().Evaluate(Bounds, 600, 3600, 60, "zero-sparse",
                    _ => zero with { RampEnabled = true, RampSeconds = 0 })));
        });

        check("出現開始の流れ: 停止から加速し、反転しても一度放出した粒は消えない", () =>
        {
            var bounds = new RainfallBounds(0, 0, 320, 180);
            var source = new WeatherFrameSource();
            RainfallParameters Parameters(long frame) => Snow() with
            {
                Kind = WeatherKind.Rain, Shape = RainfallShape.Circle,
                Speed = frame <= 120 ? 0 : 240,
                Angle = frame <= 240 ? 0 : 180,
            };
            RainfallStroke[] Frame(long frame) => source.Evaluate(bounds, frame, 600, 60, "acceleration-reversal", Parameters);
            var waiting = Frame(120);
            require(waiting.All(stroke => !Inside(stroke.Head, bounds)));
            require(Frame(240).Any(stroke => Inside(stroke.Head, bounds)));
            var returned = Frame(360);
            require(returned.Length > waiting.Length);
            var expected = new WeatherFrameSource().Evaluate(bounds, 360, 600, 60, "normal-reversal",
                frame => Parameters(frame) with { OnsetEnabled = false });
            require(returned.SequenceEqual(expected));
            foreach (var frame in new long[] { 600, 60, 360, 180, 120, 480 })
                require(Frame(frame).SequenceEqual(new WeatherFrameSource().Evaluate(bounds, frame, 600, 60,
                    "fresh", Parameters)));
        });

        check("出現開始の流れ: 局所移動と粒数Animationの順・逆シークを再現", () =>
        {
            var bounds = new RainfallBounds(0, 0, 640, 480);
            foreach (var motion in new[] { SnowMotionKind.Basic, SnowMotionKind.Vortex, SnowMotionKind.Updraft })
            {
                var source = new WeatherFrameSource();
                RainfallParameters Parameters(long frame) => Snow() with
                {
                    Amount = frame < 120 ? 1 : 4, Speed = 120, AppearanceSeconds = 0.1,
                    SnowMotion = motion, LocalCenterX = 0, LocalCenterY = 0,
                    VortexRadius = 300, VortexSpeed = 100, UpdraftWidth = 600, UpdraftHeight = 400,
                };
                var first = source.Evaluate(bounds, 60, 600, 60, "local", Parameters);
                _ = source.Evaluate(bounds, 600, 600, 60, "local", Parameters);
                require(first.SequenceEqual(source.Evaluate(bounds, 60, 600, 60, "local", Parameters)));
                foreach (var frame in new long[] { 480, 120, 360, 30 })
                    require(source.Evaluate(bounds, frame, 600, 60, "local", Parameters)
                        .SequenceEqual(new WeatherFrameSource().Evaluate(bounds, frame, 600, 60, "fresh", Parameters)));
            }
        });

        check("出現開始の流れ: 速度0で画面内へ出さず、低FPSの外周通過を再現", () =>
        {
            var stopped = Snow() with { Amount = 100, Speed = 0 };
            var beforeStart = new WeatherFrameSource().Evaluate(Bounds, 18000 * 60L, 36000 * 60L, 60,
                "not-started", _ => stopped with { StartSeconds = 36000 });
            require(beforeStart.Length == 0);
            var watch = Stopwatch.StartNew();
            var source = new WeatherFrameSource();
            var frame = source.Evaluate(Bounds, 3600, 3600, 60, "stopped", _ => stopped);
            require(frame.Length > 0 && frame.All(stroke => !Inside(stroke.Head)));
            Console.WriteLine($"出現開始・停止4000粒の60秒先: {watch.ElapsedMilliseconds} ms（単体計算）");
            var fast = Snow() with { Speed = 4000, Shape = RainfallShape.Streak, Kind = WeatherKind.Rain };
            var lowFps = new WeatherFrameSource().Evaluate(Bounds, 2, 10, 1, "fast", _ => fast);
            var normal = new WeatherFrameSource().Evaluate(Bounds, 2, 10, 1, "normal", _ => fast with { OnsetEnabled = false });
            require(lowFps.SequenceEqual(normal));
        });
    }

    private static RainfallParameters Snow() => RainfallParameters.Default with
    {
        Kind = WeatherKind.Snow, Shape = RainfallShape.SnowRound, Amount = 20, Speed = 60,
        Angle = 0, ParticleSize = 6, SpeedVariation = 0, ThicknessVariation = 0,
        OnsetEnabled = true, StartSeconds = 0, AppearanceSeconds = 0,
        DriftEnabled = false, WindEnabled = false,
    };

    private static bool Inside(Vector2 point) => Inside(point, Bounds);
    private static bool Inside(Vector2 point, RainfallBounds bounds) =>
        point.X >= bounds.Left && point.X <= bounds.Right && point.Y >= bounds.Top && point.Y <= bounds.Bottom;
}
