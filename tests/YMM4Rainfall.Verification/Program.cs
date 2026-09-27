// SPDX-License-Identifier: MPL-2.0
using System.Text;
using System.Reflection;
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall.Verification;

internal static class Program
{
    private static int passed;
    private static int failed;
    private static readonly RainfallBounds Bounds = new(0, 0, 1920, 1080);
    private static readonly RainfallParameters Default = RainfallParameters.Default;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var hostDirectory = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "YMM4ReferenceDirectory").Value!;
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = System.IO.Path.Combine(hostDirectory, name.Name + ".dll");
            return System.IO.File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        if (args.Length == 2 && args[0] == "--blur-benchmark")
        {
            BlurBenchmark.Run(args[1]);
            return 0;
        }
        if (args.Contains("--motion-blur-only", StringComparer.Ordinal))
        {
            MotionBlurChecks.Run(Check, Require);
            Check("モーションブラーの描画検証", () => MotionBlurGraphicsChecks.Run(Check, Require));
            Console.WriteLine($"検証結果: 成功 {passed}、失敗 {failed}");
            return failed == 0 ? 0 : 1;
        }
        RunSimulationChecks();
        NewItemDefaultsChecks.Run(Check, Require);
        FeatureChecks.Run(Check, Require);
        EntryColorChecks.Run(Check, Require);
        WeatherSettingsChecks.Run(Check, Require);
        AppearanceSettingsChecks.Run(Check, Require);
        WeatherEditorBindingChecks.Run(Check, Require);
        WeatherMotionChecks.Run(Check, Require);
        RainSizeMotionChecks.Run(Check, Require);
        SizeSpeedLinkChecks.Run(Check, Require);
        MotionBlurChecks.Run(Check, Require);
        OnsetFlowChecks.Run(Check, Require);
        SnowCrystalChecks.Run(Check, Require);
        WeatherPresetChecks.Run(Check, Require);
        ImageLibraryChecks.Run(Check, Require);
        if (args.Contains("--graphics", StringComparer.Ordinal))
        {
            Check("Direct2D描画の初期化とピクセル検証", () => GraphicsChecks.Run(Check, Require, args.Contains("--preview", StringComparer.Ordinal)));
            Check("降雪と輪郭の描画検証", () => SnowGraphicsChecks.Run(Check, Require));
            Check("モーションブラーの描画検証", () => MotionBlurGraphicsChecks.Run(Check, Require));
        }

        Console.WriteLine($"検証結果: 成功 {passed}、失敗 {failed}");
        return failed == 0 ? 0 : 1;
    }

    private static void RunSimulationChecks()
    {
        Check("初期設定で有限な雨筋を生成", () =>
        {
            var strokes = Frame(0);
            Require(strokes.Length > 0 && strokes.Length <= RainfallSimulation.MaximumStrokeCount);
            Require(strokes.All(IsFinite));
            Require(strokes.Select(stroke => stroke.Thickness).Distinct().Count() > 1);
        });
        Check("同時刻の再計算が一致", () => Require(Frame(1.25).SequenceEqual(Frame(1.25))));
        Check("逆順とランダムシークでも一致", () =>
        {
            var times = new[] { 0.0, 0.1, 0.5, 1, 2.25, 15, 60, 3600 };
            var frames = times.Select(time => Frame(time)).ToArray();
            foreach (var index in new[] { 7, 6, 5, 4, 3, 2, 1, 0, 4, 1, 7, 3 })
            {
                Require(Frame(times[index]).SequenceEqual(frames[index]));
            }
        });
        Check("異なるフレームレートの同時刻が一致", () =>
            Require(Frame(RainfallSimulation.GetSeconds(30, 30)).SequenceEqual(Frame(RainfallSimulation.GetSeconds(60, 60)))));
        Check("時刻が変わると雨が移動", () => Require(!Frame(0).SequenceEqual(Frame(0.1))));
        Check("シードで配置が変わる", () =>
            Require(!Frame(1).SequenceEqual(RainfallSimulation.CreateFrame(Bounds, Default with { Seed = 2 }, 1))));
        Check("密度変更で既存の雨筋の配置を保つ", () =>
        {
            var sparse = RainfallSimulation.CreateFrame(Bounds, Default with { Amount = 10 }, 1);
            var dense = RainfallSimulation.CreateFrame(Bounds, Default with { Amount = 20 }, 1);
            Require(dense.Length == sparse.Length * 2);
            Require(sparse.SequenceEqual(dense.Take(sparse.Length)));
        });
        Check("画像面積に応じて密度を維持", () =>
        {
            var smaller = RainfallSimulation.CreateFrame(new RainfallBounds(0, 0, 960, 540), Default, 0);
            Require(smaller.Length * 4 == Frame(0).Length);
        });
        Check("雨の量0では生成しない", () =>
            Require(RainfallSimulation.CreateFrame(Bounds, Default with { Amount = 0 }, 1).Length == 0));
        Check("不透明度0では生成しない", () =>
            Require(RainfallSimulation.CreateFrame(Bounds, Default with { Opacity = 0 }, 1).Length == 0));
        Check("速度0では時刻によらず静止", () =>
        {
            var parameters = Default with { Speed = 0 };
            Require(RainfallSimulation.CreateFrame(Bounds, parameters, 0)
                .SequenceEqual(RainfallSimulation.CreateFrame(Bounds, parameters, 120)));
        });
        Check("角度0では真下へ進む", () =>
        {
            var parameters = Default with { Angle = 0 };
            var before = RainfallSimulation.CreateFrame(Bounds, parameters, 0);
            var after = RainfallSimulation.CreateFrame(Bounds, parameters, 0.001);
            Require(before.All(stroke => stroke.Tail.X == stroke.Head.X && stroke.Head.Y > stroke.Tail.Y));
            Require(before.Zip(after).All(pair => pair.First.Head.X == pair.Second.Head.X));
            Require(before.Zip(after).Count(pair => pair.Second.Head.Y > pair.First.Head.Y) > before.Length * 0.9);
        });
        Check("角度の符号で左右を切り替え", () =>
        {
            Require(RainfallSimulation.CreateFrame(Bounds, Default with { Angle = 30 }, 0)
                .All(stroke => stroke.Head.X > stroke.Tail.X && stroke.Head.Y > stroke.Tail.Y));
            Require(RainfallSimulation.CreateFrame(Bounds, Default with { Angle = -30 }, 0)
                .All(stroke => stroke.Head.X < stroke.Tail.X && stroke.Head.Y > stroke.Tail.Y));
        });
        Check("負の原点でも相対位置を維持", () =>
        {
            var shifted = RainfallSimulation.CreateFrame(new RainfallBounds(-960, -540, 960, 540), Default, 1);
            Require(Frame(1).Zip(shifted).All(pair =>
                Math.Abs(pair.First.Head.X - 960 - pair.Second.Head.X) < 0.001 &&
                Math.Abs(pair.First.Head.Y - 540 - pair.Second.Head.Y) < 0.001));
        });
        Check("無効な領域は生成しない", () =>
        {
            foreach (var bounds in new[]
            {
                default(RainfallBounds), new(10, 0, 0, 10), new(0, 10, 10, 0),
                new(double.NaN, 0, 10, 10), new(0, 0, double.PositiveInfinity, 10),
                new(0, 0, 32769, 100), new(1_000_001, 0, 1_000_010, 10),
            })
            {
                Require(RainfallSimulation.CreateFrame(bounds, Default, 0).Length == 0);
            }
        });
        Check("最大領域でも本数の上限を守る", () =>
        {
            var strokes = RainfallSimulation.CreateFrame(new RainfallBounds(0, 0, 32768, 32768), Default with { Amount = 100 }, 0);
            Require(strokes.Length == RainfallSimulation.MaximumStrokeCount && strokes.All(IsFinite));
        });
        Check("非有限の設定を初期値へ補完", () =>
        {
            var invalid = new RainfallParameters(double.NaN, double.PositiveInfinity, double.NaN,
                double.NegativeInfinity, double.NaN, double.PositiveInfinity, 1);
            Require(invalid.Normalize() == Default);
        });
        Check("設定範囲の上下限を補正", () =>
        {
            var normalized = new RainfallParameters(-1, -1, -1000, 500, -1, 101, int.MaxValue).Normalize();
            Require(normalized == new RainfallParameters(0, 0, -1000, 200, 0.1, 100, 99999));
            Require(normalized.Normalize() == normalized);
        });
        Check("長時間と負の時刻でも有限", () =>
        {
            foreach (var time in new[] { -1000.0, 86400, double.MaxValue, -double.MaxValue })
            {
                Require(Frame(time).All(IsFinite));
                Require(RainfallSimulation.CreateFrame(Bounds, Default with { Speed = double.Epsilon }, time).All(IsFinite));
            }
        });
        Check("非有限の時刻を描画へ渡さない", () =>
        {
            Require(Frame(double.NaN).Length == 0);
            Require(Frame(double.PositiveInfinity).Length == 0);
        });
        Check("不正なフレームレートを補正", () =>
        {
            Require(RainfallSimulation.GetSeconds(1, 0) == 0);
            Require(RainfallSimulation.GetSeconds(1, -1) == 0);
            Require(RainfallSimulation.GetSeconds(double.NaN, 30) == 0);
            Require(RainfallSimulation.GetSeconds(1, double.PositiveInfinity) == 0);
            Require(RainfallSimulation.GetSeconds(double.MaxValue, double.Epsilon) == 0);
        });
    }

    private static RainfallStroke[] Frame(double time) => RainfallSimulation.CreateFrame(Bounds, Default, time);

    private static bool IsFinite(RainfallStroke stroke) =>
        float.IsFinite(stroke.Tail.X) && float.IsFinite(stroke.Tail.Y) &&
        float.IsFinite(stroke.Head.X) && float.IsFinite(stroke.Head.Y) &&
        float.IsFinite(stroke.Thickness) && stroke.Thickness > 0 &&
        float.IsFinite(stroke.Opacity) && stroke.Opacity is > 0 and <= 1;

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("期待した条件を満たしませんでした。");
        }
    }

    private static void Check(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine($"成功: {name}");
        }
        catch (Exception exception)
        {
            failed++;
            Console.Error.WriteLine($"失敗: {name}\n{exception}");
        }
    }
}
