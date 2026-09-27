// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows.Media;
using YMM4Rainfall.Simulation;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class EntryColorChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("出現開始UI: 保存した増え方と時間を保持し、方式変更でも時間を維持", () =>
        {
            foreach (var ramp in new[] { false, true })
            {
                var old = new RainfallEffect(useNewItemDefaults: false) { OnsetEnabled = true, RampEnabled = ramp, AppearanceSeconds = 2, RampSeconds = 7 };
                var loaded = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(old))!;
                require(loaded.RiseSeconds == (ramp ? 7 : 2));
                require(loaded.OnsetPattern == (ramp ? RainfallOnsetPattern.Sparse : RainfallOnsetPattern.Uniform));
                require(old.GetParameters(100, 600, 60) == loaded.GetParameters(100, 600, 60));
                loaded.RiseSeconds = 9;
                loaded.OnsetPattern = ramp ? RainfallOnsetPattern.Uniform : RainfallOnsetPattern.Sparse;
                require(loaded.RiseSeconds == 9);
                var json = JsonNode.Parse(YmmJson.GetJsonText(loaded))!.AsObject();
                require(!json.ContainsKey("RiseSeconds") && !json.ContainsKey("OnsetPattern"));
                require(YmmJson.LoadFromText<RainfallEffect>(json.ToJsonString())!.RiseSeconds == 9);
            }
        });
        check("形状: 各種類の形と粒サイズAnimationを保存再読込", () =>
        {
            require(YmmJson.LoadFromText<RainfallEffect>("{}")!.Shape == RainfallShape.Streak);
            foreach (var shape in Enum.GetValues<RainfallShape>())
            {
                var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = shape };
                FeatureChecks.SetLinear(effect.ParticleSize, 4, 32);
                var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
                require(restored.Shape == shape);
                require(restored.GetParameters(300, 600, 60) == effect.GetParameters(300, 600, 60));
                require(restored.IsParticle == (shape != RainfallShape.Streak));
            }
            require((RainfallParameters.Default with { Shape = (RainfallShape)99, ParticleSize = double.NaN }).Normalize().Shape == RainfallShape.Streak);
        });
        check("形状: 大きさのばらつきとサイズ変更でも頭の位置を保つ", () =>
        {
            var area = new RainfallBounds(0, 0, 960, 540);
            var p = RainfallParameters.Default with { Shape = RainfallShape.Circle, ThicknessVariation = 0, ParticleSize = 10 };
            var first = RainfallSimulation.CreateFrame(area, p, 1);
            var second = RainfallSimulation.CreateFrame(area, p with { Shape = RainfallShape.CartoonDrop, ParticleSize = 64, ThicknessVariation = 100 }, 1);
            require(first.All(s => s.Size == 10));
            require(second.Select(s => s.Size).Distinct().Count() > 1);
            require(first.Select(s => s.Head).SequenceEqual(second.Select(s => s.Head)));
        });
        var bounds = new RainfallBounds(-100, -50, 1820, 1030);
        var defaults = RainfallParameters.Default with
        {
            OnsetEnabled = true,
            StartSeconds = 2,
            AppearanceSeconds = 0,
            Speed = 100,
            SpeedVariation = 0,
            Angle = 0,
        };
        check("雨量徐増: 少ない粒から設定量へ到達し、逆順でも一致", () =>
        {
            var p = defaults with { Amount = 100, RampEnabled = true, RampSeconds = 4 };
            var before = RainfallSimulation.CreateFrame(bounds, p, 1.9);
            var quarter = RainfallSimulation.CreateFrame(bounds, p, 3);
            var half = RainfallSimulation.CreateFrame(bounds, p, 4);
            var full = RainfallSimulation.CreateFrame(bounds, p, 6);
            require(before.Length == 0 && quarter.Length is > 30 and < 100);
            require(half.Length is > 200 and < 300 && full.Length == 1000);
            require(half.SequenceEqual(RainfallSimulation.CreateFrame(bounds, p, 4)));
        });
        check("雨量徐増: 既存の出現期間と独立し、無効時は従来のまま", () =>
        {
            var p = defaults with { RampEnabled = true, RampSeconds = 4 };
            require(RainfallSimulation.CreateFrame(bounds, p, 4).SequenceEqual(
                RainfallSimulation.CreateFrame(bounds, p with { AppearanceSeconds = 999 }, 4)));
            require(RainfallSimulation.CreateFrame(bounds, p with { OnsetEnabled = false }, 4).SequenceEqual(
                RainfallSimulation.CreateFrame(bounds, p with { OnsetEnabled = false, RampEnabled = false }, 4)));
            require(RainfallSimulation.CreateFrame(bounds, p with { RampEnabled = false }, 4).SequenceEqual(
                RainfallSimulation.CreateFrame(bounds, defaults, 4)));
            require(RainfallSimulation.CreateFrame(bounds, p with { RampSeconds = 0 }, 2).Length == 300);
        });
        check("雨量徐増: 追加粒は上端から入り、速度0では画面外に留まる", () =>
        {
            var p = defaults with { RampEnabled = true, RampSeconds = 4 };
            require(RainfallSimulation.CreateFrame(bounds, p, 2.5).All(s => s.Head.Y <= bounds.Top + 41.001));
            require(RainfallSimulation.CreateFrame(bounds, p with { Speed = 0 }, 10).All(s => s.Head.Y < bounds.Top));
            require(RainfallSimulation.CreateFrame(bounds, p with { Amount = 0 }, 10).Length == 0);
        });
        check("雨量徐増: Animationと併用したシークと保存互換性", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { OnsetEnabled = true, RampEnabled = true, RampSeconds = 4 };
            FeatureChecks.SetLinear(effect.SpeedAnimation, 100, 500);
            FeatureChecks.SetLinear(effect.SpeedVariation, 0, 100);
            var motion = new RainfallMotion();
            RainfallStroke[] Frame(long frame) => RainfallSimulation.CreateFrame(bounds, effect.GetParameters(frame, 600, 60),
                frame / 60.0, motion.Evaluate(frame, 60, effect.MotionSignature(600, 60), f => effect.GetVelocity(f, 600, 60)),
                effect.CreateEmission(motion, 600, 60));
            var early = Frame(100);
            _ = Frame(600);
            require(early.SequenceEqual(Frame(100)));
            var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            require(restored.GetParameters(100, 600, 60) == effect.GetParameters(100, 600, 60));
            var old = YmmJson.LoadFromText<RainfallEffect>("{}")!;
            require(!old.RampEnabled && old.RampSeconds == 5);
            var p = defaults with { RampSeconds = double.NaN };
            require(p.Normalize().RampSeconds == 5);
        }); check("降り始め: 下向きは上端から入り、途中には出現しない", () =>
        {
            var birth = RainfallSimulation.CreateFrame(bounds, defaults, 2);
            var later = RainfallSimulation.CreateFrame(bounds, defaults, 2.2);
            require(birth.Length > 0 && birth.All(s => s.Head.Y < bounds.Top && s.Tail.Y < bounds.Top));
            require(later.All(s => Math.Abs(s.Head.Y - (bounds.Top + 11)) < 0.001));
        });
        check("降り始め: 横・上・斜めの各方向で手前の画面外に配置", () =>
        {
            foreach (var angle in new[] { 90, 180, 270, 45, 135, 225, 315 })
            {
                var frame = RainfallSimulation.CreateFrame(bounds, defaults with { Angle = angle }, 2);
                require(frame.All(s => s.Head.X < bounds.Left || s.Head.X > bounds.Right ||
                    s.Head.Y < bounds.Top || s.Head.Y > bounds.Bottom));
                if (angle == 90) require(frame.All(s => s.Head.X < bounds.Left));
                if (angle == 180) require(frame.All(s => s.Head.Y > bounds.Bottom));
                if (angle == 270) require(frame.All(s => s.Head.X > bounds.Right));
            }
        });
        check("降り始め: 速度0では画面内に現れない", () =>
        {
            var p = defaults with { Speed = 0 };
            require(RainfallSimulation.CreateFrame(bounds, p, 100).All(s => s.Head.Y < bounds.Top));
        });
        check("降り始め: 出現を分散しても最初の到達範囲より下には出ない", () =>
        {
            var p = defaults with { AppearanceSeconds = 3 };
            var frame = RainfallSimulation.CreateFrame(bounds, p, 2.5);
            require(frame.Length > 0 && frame.All(s => s.Head.Y <= bounds.Top + 41.001));
        });
        check("降り始め: Animationの出生時刻を差し引き、逆順シークを再現", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { OnsetEnabled = true, StartSeconds = 2, AppearanceSeconds = 3 };
            effect.AngleAnimation.SetFirstValue(0);
            FeatureChecks.SetLinear(effect.SpeedAnimation, 100, 500);
            FeatureChecks.SetLinear(effect.SpeedVariation, 0, 100);
            var motion = new RainfallMotion();
            RainfallStroke[] Frame(long frame)
            {
                var travel = motion.Evaluate(frame, 60, effect.MotionSignature(600, 60), f => effect.GetVelocity(f, 600, 60));
                return RainfallSimulation.CreateFrame(bounds, effect.GetParameters(frame, 600, 60), frame / 60.0,
                    travel, effect.CreateEmission(motion, 600, 60));
            }
            var first = Frame(150);
            require(first.All(s => s.Head.Y < bounds.Top + 150));
            _ = Frame(600); _ = Frame(120);
            require(first.SequenceEqual(Frame(150)));
            effect.StartSeconds = 2.5;
            require(Frame(149).Length == 0);
        });
        check("移動量: 小数フレームの出生時刻も台形則で連続", () =>
        {
            var motion = new RainfallMotion();
            var value = motion.EvaluateSeconds(2.505, 60, "linear", f => new(0, f / 60.0 * 100));
            require(Math.Abs(value.Y - 50 * 2.505 * 2.505) < 1e-8);
            require(motion.EvaluateSeconds(2.5, 60, "linear", f => new(0, f / 60.0 * 100)) ==
                motion.Evaluate(150, 60, "linear", f => new(0, f / 60.0 * 100)));
        });
        check("色: パレットとRGBの先頭値が双方向に連動", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            FeatureChecks.SetLinear(effect.Red, 20, 80);
            FeatureChecks.SetLinear(effect.Green, 30, 70);
            FeatureChecks.SetLinear(effect.Blue, 40, 60);
            var types = new[] { effect.Red.AnimationType, effect.Green.AnimationType, effect.Blue.AnimationType };
            var notified = 0;
            ((INotifyPropertyChanged)effect).PropertyChanged += (_, e) => { if (e.PropertyName == nameof(effect.Color)) notified++; };
            effect.Color = Color.FromArgb(128, 0, 255, 128);
            require(effect.Red.GetFirstValue() == 0 && effect.Green.GetFirstValue() == 100 &&
                Math.Abs(effect.Blue.GetFirstValue() - 12800.0 / 255) < 1e-8);
            require(effect.Red.Values[^1].Value == 80 && effect.Green.Values[^1].Value == 70 && effect.Blue.Values[^1].Value == 60);
            require(types.SequenceEqual(new[] { effect.Red.AnimationType, effect.Green.AnimationType, effect.Blue.AnimationType }));
            var before = notified;
            effect.Red.Values[0].Value = 50;
            require(effect.Color.R == 128 && effect.Color.A == 128 && notified > before);
            require(Math.Abs(effect.GetParameters(0, 600, 60).Red - 0.5) < 1e-8);
        });
    }
}
