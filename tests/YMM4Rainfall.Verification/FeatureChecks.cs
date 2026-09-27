// SPDX-License-Identifier: MPL-2.0
using System.Text.Json;
using YmmJson = YukkuriMovieMaker.Json.Json;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;

namespace YMM4Rainfall.Verification;

internal static class FeatureChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("PNG設定の保存再読込と旧形状の保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.Png, PngImageId = Guid.NewGuid() };
            effect.SetPngStatus("検証中");
            var json = YmmJson.GetJsonText(effect);
            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            require(restored.IsPng && restored.IsParticle && !restored.IsStreak);
            require(restored.PngImageId == effect.PngImageId);
            require(!json.Contains("PngStatus") && !json.Contains("IsPng"));
            require(YmmJson.LoadFromText<RainfallEffect>("{}")!.Shape == RainfallShape.Streak);
        });
        var bounds = new RainfallBounds(0, 0, 1920, 1080);
        var defaults = RainfallParameters.Default;
        check("揺らめき: 対象3形状・周期・方向・回転保持・シーク再現", () =>
        {
            foreach (var shape in new[] { RainfallShape.Bubble, RainfallShape.LensBubble, RainfallShape.Png })
                foreach (var angle in new[] { 0.0, 45, 90, 180, 270 })
                {
                    var p = defaults with
                    {
                        Shape = shape,
                        Angle = angle,
                        Speed = 0,
                        SwayEnabled = true,
                        SwayAmplitude = 20,
                        SwayPeriod = 2,
                        RotationAngle = 120,
                        KeepUpright = true,
                        MirrorWhenUpright = true
                    };
                    var baseline = RainfallSimulation.CreateFrame(bounds, p with { SwayEnabled = false }, 0.5);
                    var a = RainfallSimulation.CreateFrame(bounds, p, 0.5);
                    var opposite = RainfallSimulation.CreateFrame(bounds, p, 1.5);
                    var repeated = RainfallSimulation.CreateFrame(bounds, p, 2.5);
                    var rad = angle * Math.PI / 180;
                    var offsets = new List<double>();
                    for (var i = 0; i < a.Length; i++)
                    {
                        // 画面外の循環境界を跨いだ差を最短距離へ戻します。
                        double Delta(double d, double span) => d > span / 2 ? d - span : d < -span / 2 ? d + span : d;
                        var dx = Delta(a[i].Head.X - baseline[i].Head.X, bounds.Width + 2052);
                        var dy = Delta(a[i].Head.Y - baseline[i].Head.Y, bounds.Height + 2052);
                        require(Math.Abs(dx * Math.Sin(rad) + dy * Math.Cos(rad)) < 0.001);
                        require(Math.Sqrt(dx * dx + dy * dy) <= 20.001);
                        var ox = Delta(opposite[i].Head.X - baseline[i].Head.X, bounds.Width + 2052);
                        var oy = Delta(opposite[i].Head.Y - baseline[i].Head.Y, bounds.Height + 2052);
                        require(Math.Abs(dx + ox) < 0.001 && Math.Abs(dy + oy) < 0.001);
                        require(System.Numerics.Vector2.Distance(a[i].Head, repeated[i].Head) < 0.001);
                        require(a[i].Rotation == baseline[i].Rotation && a[i].FlipHorizontal == baseline[i].FlipHorizontal);
                        offsets.Add(Math.Round(dx * Math.Cos(rad) - dy * Math.Sin(rad), 2));
                    }
                    require(offsets.Distinct().Count() > 10);
                    require(a.SequenceEqual(RainfallSimulation.CreateFrame(bounds, p, 0.5)));
                    require(baseline.SequenceEqual(RainfallSimulation.CreateFrame(bounds, p with { SwayAmplitude = 0 }, 0.5)));
                }
        });
        check("揺らめき: 対象外と出生フレームは従来位置・極端な値でも有限", () =>
        {
            foreach (var shape in Enum.GetValues<RainfallShape>())
            {
                var p = defaults with { Shape = shape, SwayEnabled = true, SwayAmplitude = 200, SwayPeriod = 0.1 };
                if (shape is not (RainfallShape.Bubble or RainfallShape.LensBubble or RainfallShape.Png))
                    require(RainfallSimulation.CreateFrame(bounds, p, 1).SequenceEqual(
                        RainfallSimulation.CreateFrame(bounds, p with { SwayEnabled = false }, 1)));
                foreach (var angle in new[] { 0.0, 45, 90, 180, 225, 270 })
                {
                    var onset = p with { Angle = angle, OnsetEnabled = true, StartSeconds = 2, AppearanceSeconds = 0 };
                    require(RainfallSimulation.CreateFrame(bounds, onset, 2).SequenceEqual(
                        RainfallSimulation.CreateFrame(bounds, onset with { SwayEnabled = false }, 2)));
                }
                foreach (var t in new[] { double.MaxValue, -double.MaxValue, 10000.0 })
                    require(RainfallSimulation.CreateFrame(bounds, p, t).All(s => float.IsFinite(s.Head.X) && float.IsFinite(s.Head.Y)));
            }
            var invalid = (defaults with { SwayAmplitude = double.NaN, SwayPeriod = double.PositiveInfinity }).Normalize();
            require(invalid.SwayAmplitude == 20 && invalid.SwayPeriod == 2);
            require((defaults with { SwayPeriod = 0, SwayAmplitude = -1 }).Normalize() is { SwayPeriod: 0.1, SwayAmplitude: 0 });
        });
        check("揺らめき: 保存再読込・種類ごとの設定と表示条件通知", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.Png, SwayEnabled = true, SwayAmplitude = 37, SwayPeriod = 3.5 };
            var json = YmmJson.GetJsonText(effect);
            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            require(restored.GetParameters(100, 300, 30) is { SwayEnabled: true, SwayAmplitude: 37, SwayPeriod: 3.5 });
            require(restored.IsSwaySupported && restored.IsSwaySettingsVisible);
            require(!json.Contains("IsSwaySupported") && !json.Contains("IsSwaySettingsVisible"));
            require(!YmmJson.LoadFromText<RainfallEffect>("{}")!.SwayEnabled);
            var changed = new List<string?>();
            restored.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            restored.Shape = RainfallShape.Streak;
            require(!restored.IsSwaySupported && !restored.IsSwaySettingsVisible);
            require(changed.Contains(nameof(RainfallEffect.IsSwaySupported)) && changed.Contains(nameof(RainfallEffect.IsSwaySettingsVisible)));
            restored.Shape = RainfallShape.Bubble;
            require(!restored.IsSwaySettingsVisible);
            restored.Shape = RainfallShape.Png;
            require(restored.IsSwaySettingsVisible);
            restored.SwayEnabled = false;
            require(!restored.IsSwaySettingsVisible);
        });
        check("上下を保つ: 境界・追従と回転速度の合成・追加設定の保存", () =>
        {
            var p = defaults with { Shape = RainfallShape.Png, KeepUpright = true };
            foreach (var angle in new[] { -720.0, -181, -180, -91, -90, -89, 0, 89, 90, 91, 180, 181, 720 })
                require(RainfallSimulation.CreateFrame(bounds, p with { RotationAngle = angle }, 1).All(s => s.Rotation is >= -90 and <= 90));
            require(RainfallSimulation.CreateFrame(bounds, p with { RotationAngle = 91 }, 1).All(s => s.Rotation == -89));
            require(RainfallSimulation.CreateFrame(bounds, p with { RotationAngle = -91 }, 1).All(s => s.Rotation == 89));
            require(RainfallSimulation.CreateFrame(bounds, p with { RotationAngle = 91, MirrorWhenUpright = true }, 1).All(s => s.FlipHorizontal));
            require(RainfallSimulation.CreateFrame(bounds, p with { RotationAngle = 91, KeepUpright = false, MirrorWhenUpright = true }, 1).All(s => !s.FlipHorizontal));
            var combined = p with { FollowDirection = true, Angle = 90, RotationAngle = 30, RotationSpeed = 180 };
            require(RainfallSimulation.CreateFrame(bounds, combined, 1).All(s => s.Rotation == -60));
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.LensBubble, KeepUpright = true, MirrorWhenUpright = true };
            SetLinear(effect.LensReflection, 0, 100);
            var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            require(restored.IsLensBubble && restored.KeepUpright && restored.MirrorWhenUpright && restored.GetParameters(150, 300, 30).LensReflection == 50);
        });
        check("粒の不透明度: ばらつき0で指定値どおり、100で粒別の濃淡", () =>
        {
            var p = defaults with { Shape = RainfallShape.Png, Opacity = 100, OpacityVariation = 0 };
            require(RainfallSimulation.CreateFrame(bounds, p, 1).All(s => s.Opacity == 1));
            require(RainfallSimulation.CreateFrame(bounds, p with { Opacity = 35 }, 1).All(s => Math.Abs(s.Opacity - 0.35) < 1e-6));
            var varied = RainfallSimulation.CreateFrame(bounds, p with { OpacityVariation = 100 }, 1);
            require(varied.All(s => s.Opacity is > 0 and <= 1));
            require(varied.Select(s => s.Opacity).Distinct().Count() > 1);
            require(varied.SequenceEqual(RainfallSimulation.CreateFrame(bounds, p with { OpacityVariation = 100 }, 1)));
        });
        check("粒サイズ512: 上限・最大ばらつき・サイズ変更時の中心位置", () =>
        {
            var p = defaults with { Shape = RainfallShape.Png, ParticleSize = 512, ThicknessVariation = 100 };
            var large = RainfallSimulation.CreateFrame(bounds, p, 1);
            var small = RainfallSimulation.CreateFrame(bounds, p with { ParticleSize = 8 }, 1);
            require(large.Select(s => s.Head).SequenceEqual(small.Select(s => s.Head)));
            require(large.All(s => s.Size > 0 && s.Size <= 1024));
            require(large.Length <= RainfallSimulation.MaximumStrokeCount);
            require((p with { ParticleSize = 999 }).Normalize().ParticleSize == 512);
            var effect = new RainfallEffect(useNewItemDefaults: false);
            effect.ParticleSize.SetFirstValue(512);
            require(effect.GetParameters(0, 300, 30).ParticleSize == 512);
        });
        check("粒の回転: 正負・方向追従・出生時刻・巨大時刻の再現性", () =>
        {
            var p = defaults with { Shape = RainfallShape.Png, Speed = 0, RotationAngle = 10, RotationSpeed = 90 };
            var a = RainfallSimulation.CreateFrame(bounds, p, 1);
            require(a.All(s => Math.Abs(s.Rotation - 100) < 0.001));
            require(a.Select(s => s.Head).SequenceEqual(RainfallSimulation.CreateFrame(bounds, p, 2).Select(s => s.Head)));
            require(RainfallSimulation.CreateFrame(bounds, p with { RotationSpeed = -90 }, 1).All(s => Math.Abs(s.Rotation + 80) < 0.001));
            require(RainfallSimulation.CreateFrame(bounds, p with { FollowDirection = true, Angle = 90 }, 1).All(s => Math.Abs(s.Rotation - 10) < 0.001));
            require(RainfallSimulation.CreateFrame(bounds, p with { OnsetEnabled = true, StartSeconds = 2, AppearanceSeconds = 0 }, 2).All(s => s.Rotation == 10));
            require(RainfallSimulation.CreateFrame(bounds, p with { OnsetEnabled = true, StartSeconds = 2, AppearanceSeconds = 0 }, 3).All(s => s.Rotation == 100));
            foreach (var time in new[] { double.MaxValue, -double.MaxValue, 10000, -10000 })
            {
                var first = RainfallSimulation.CreateFrame(bounds, p, time);
                require(first.All(s => float.IsFinite(s.Rotation)));
                require(first.SequenceEqual(RainfallSimulation.CreateFrame(bounds, p, time)));
            }
        });
        check("追加設定: Animationと回転速度・方向追従の保存再読込", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.Png, RotationSpeed = -180, FollowDirection = true };
            SetLinear(effect.RotationAngle, 0, 720);
            SetLinear(effect.OpacityVariation, 0, 100);
            SetLinear(effect.ParticleSize, 64, 512);
            var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            require(restored.GetParameters(0, 300, 30).RotationAngle == 0);
            require(restored.GetParameters(150, 300, 30).RotationAngle == 360);
            var last = restored.GetParameters(300, 300, 30);
            require(last.RotationAngle == 720 && last.OpacityVariation == 100 && last.ParticleSize == 512);
            require(last.RotationSpeed == -180 && last.FollowDirection);
        });
        check("全方向と複数回転の角度", () =>
        {
            foreach (var angle in new[] { -1080.0, -270, 90, 180, 270, 720, 1080 })
            {
                var frame = RainfallSimulation.CreateFrame(bounds, defaults with { Angle = angle }, 1);
                var repeated = RainfallSimulation.CreateFrame(bounds, defaults with { Angle = angle + 360 }, 1);
                require(frame.SequenceEqual(repeated));
                var direction = frame[0].Head - frame[0].Tail;
                var velocity = RainfallTravel.Velocity(1, angle);
                require(direction.X * velocity.X + direction.Y * velocity.Y > 0);
            }
        });
        check("降り始め前・途中・完了と逆順再現", () =>
        {
            var p = defaults with { OnsetEnabled = true, StartSeconds = 2, AppearanceSeconds = 3 };
            require(RainfallSimulation.CreateFrame(bounds, p, 1.99).Length == 0);
            var middle = RainfallSimulation.CreateFrame(bounds, p, 3.5);
            var complete = RainfallSimulation.CreateFrame(bounds, p, 5);
            require(middle.Length > 0 && middle.Length < complete.Length);
            require(complete.Length == RainfallSimulation.CreateFrame(bounds, defaults, 5).Length);
            require(middle.SequenceEqual(RainfallSimulation.CreateFrame(bounds, p, 3.5)));
            require(RainfallSimulation.CreateFrame(bounds, p with { AppearanceSeconds = 0 }, 2).Length == complete.Length);
        });
        check("太さのばらつき0は均一・100は広がる", () =>
        {
            var uniform = RainfallSimulation.CreateFrame(bounds, defaults with { ThicknessVariation = 0 }, 1);
            var varied = RainfallSimulation.CreateFrame(bounds, defaults with { ThicknessVariation = 100 }, 1);
            require(uniform.All(s => s.Thickness == defaults.Thickness));
            require(varied.Min(s => s.Thickness) < 0.1 && varied.Max(s => s.Thickness) > 1.9);
        });
        check("長さ・太さ・角度変更は粒の頭の位置を変えない", () =>
        {
            var travel = new RainfallTravel(100, 200);
            var before = RainfallSimulation.CreateFrame(bounds, defaults, 1, travel);
            var after = RainfallSimulation.CreateFrame(bounds, defaults with { Length = 200, Thickness = 8, Angle = 180, ThicknessVariation = 100 }, 1, travel);
            require(before.Select(s => s.Head).SequenceEqual(after.Select(s => s.Head)));
        });
        check("速度の積分は線形加速の解析解と一致", () =>
        {
            var motion = new RainfallMotion();
            var result = motion.Evaluate(600, 60, "acceleration", f => new(0, f / 60.0 * 100));
            require(Math.Abs(result.Y - 5000) < 1e-8 && result.X == 0);
        });
        check("移動量はシーク順序に依存せず過去編集で更新", () =>
        {
            var motion = new RainfallMotion();
            RainfallTravel Velocity(long f) => RainfallTravel.Velocity(100 + f, f);
            var direct = motion.Evaluate(600, 60, "a", Velocity);
            motion.Evaluate(12, 60, "a", Velocity);
            require(direct == motion.Evaluate(600, 60, "a", Velocity));
            require(direct == new RainfallMotion().Evaluate(600, 60, "a", Velocity));
            require(Math.Abs(motion.Evaluate(600, 60, "b", f => new(0, 100)).Y - 1000) < 1e-8);
        });
        check("速度のばらつき0は均一・100はほぼ0～2倍", () =>
        {
            foreach (var variation in new[] { 0.0, 100 })
            {
                var p = defaults with { Speed = 100, Angle = 0, SpeedVariation = variation };
                var first = RainfallSimulation.CreateFrame(bounds, p, 0);
                var second = RainfallSimulation.CreateFrame(bounds, p, 0.01);
                var distances = first.Zip(second).Select(pair => pair.Second.Head.Y - pair.First.Head.Y).ToArray();
                if (variation == 0) require(distances.All(distance => Math.Abs(distance - 1) < 0.001));
                else require(distances.Min() < 0.1 && distances.Max() > 1.9 && distances.All(distance => distance >= 0 && distance <= 2.001));
            }
        });
        check("速度のばらつき40は従来と同じ倍率", () =>
        {
            var p = defaults with { Speed = 100, Angle = 0 };
            var first = RainfallSimulation.CreateFrame(bounds, p, 0);
            var second = RainfallSimulation.CreateFrame(bounds, p, 0.01);
            require(first.Zip(second).All(pair =>
                Math.Abs(pair.Second.Head.Y - pair.First.Head.Y - pair.First.Thickness / p.Thickness) < 0.001));
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var motion = new RainfallMotion();
            var travel = motion.Evaluate(60, 60, effect.MotionSignature(600, 60), f => effect.GetVelocity(f, 600, 60));
            var direct = RainfallSimulation.CreateFrame(bounds, defaults, 1);
            var integrated = RainfallSimulation.CreateFrame(bounds, defaults, 1, travel);
            require(direct.Zip(integrated).All(pair => System.Numerics.Vector2.Distance(pair.First.Head, pair.Second.Head) < 0.001));
        });
        check("速度ばらつきの時間変化を積分し、均一へ戻しても位置が跳ねない", () =>
        {
            var motion = new RainfallMotion();
            var ramp = motion.Evaluate(600, 60, "ramp", f => RainfallTravel.Velocity(100, 0, f / 6.0));
            require(Math.Abs(ramp.Y - 1000) < 1e-8 && Math.Abs(ramp.VariationY - 1250) < 1e-8);
            var before = motion.Evaluate(599, 60, "stopVariation", f => RainfallTravel.Velocity(100, 0, f < 300 ? 100 : 0));
            var after = motion.Evaluate(600, 60, "stopVariation", f => RainfallTravel.Velocity(100, 0, f < 300 ? 100 : 0));
            require(before.VariationY == after.VariationY);
            require(Math.Abs(after.Y - before.Y - 100.0 / 60) < 1e-8);
            require(after == new RainfallMotion().Evaluate(600, 60, "stopVariation", f => RainfallTravel.Velocity(100, 0, f < 300 ? 100 : 0)));
        });
        check("速度ばらつきのAnimation編集と0の保存復元", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            SetLinear(effect.SpeedVariation, 0, 100);
            var motion = new RainfallMotion();
            var signature = effect.MotionSignature(600, 60);
            var before = motion.Evaluate(550, 60, signature, f => effect.GetVelocity(f, 600, 60));
            effect.SpeedVariation.Values[0].Value = 50;
            var changed = effect.MotionSignature(600, 60);
            require(signature != changed);
            var after = motion.Evaluate(550, 60, changed, f => effect.GetVelocity(f, 600, 60));
            require(before.VariationY != after.VariationY && before.Y == after.Y);
            require(after == new RainfallMotion().Evaluate(550, 60, changed, f => effect.GetVelocity(f, 600, 60)));
            effect.SpeedVariation.CopyFrom(new Animation(0, 0, 100));
            require(YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!.GetParameters(100, 600, 60).SpeedVariation == 0);
        }); check("全11数値項目のAnimationと色の時間変化", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            foreach (var animation in new[] { effect.AmountAnimation, effect.SpeedAnimation, effect.AngleAnimation,
                effect.LengthAnimation, effect.ThicknessAnimation, effect.Opacity, effect.ThicknessVariation,
                effect.Red, effect.Green, effect.Blue, effect.SpeedVariation })
            {
                SetLinear(animation, 1, 5);
                if (animation.GetValue(0, 600, 60) != 1 || animation.GetValue(600, 600, 60) != 5) throw new InvalidOperationException($"Animationの端点: default={animation.DefaultValue}, start={animation.GetValue(0, 600, 60):R}, end={animation.GetValue(600, 600, 60):R}, count={animation.Values.Count}");
            }
            var first = effect.GetParameters(0, 600, 60);
            var last = effect.GetParameters(600, 600, 60);
            require(first.Amount == 1 && last.Amount == 5 && first.Speed == 1 && last.Speed == 5);
            require(first.Angle == 1 && last.Angle == 5 && first.Length == 1 && last.Length == 5);
            require(first.Thickness == 1 && last.Thickness == 5 && first.Opacity == 1 && last.Opacity == 5);
            require(first.ThicknessVariation == 1 && last.ThicknessVariation == 5);
            require(first.SpeedVariation == 1 && last.SpeedVariation == 5);
            require(first.Red == 0.01 && last.Red == 0.05 && first.Green == 0.01 && last.Green == 0.05 &&
                first.Blue == 0.01 && last.Blue == 0.05);
            var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            foreach (var frame in new[] { 0, 120, 300, 600, 250 })
                require(effect.GetParameters(frame, 600, 60) == restored.GetParameters(frame, 600, 60));
        });
        check("標準Animationで720度回転と速度変更・停止", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            SetLinear(effect.SpeedAnimation, 600, 0);
            SetLinear(effect.AngleAnimation, 0, 720);
            require(effect.GetParameters(600, 600, 60).Angle == 720);
            var motion = new RainfallMotion();
            RainfallTravel Position(long frame) => motion.Evaluate(frame, 60, effect.MotionSignature(600, 60),
                f => effect.GetVelocity(f, 600, 60));
            var before = Position(590);
            var end = Position(600);
            require(Math.Abs(end.X - before.X) < 2 && Math.Abs(end.Y - before.Y) < 2);
            require(end == Position(600));
            var serialized = YmmJson.GetJsonText(effect);
            _ = Position(100);
            require(serialized == YmmJson.GetJsonText(effect));
        });
        check("中間点編集・移動・FPSと尺変更で移動キャッシュを無効化", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            SetLinear(effect.SpeedAnimation, 100, 500);
            effect.SpeedAnimation.SetAnimationParameters(600, 60);
            var keys = new KeyFrames();
            effect.SpeedAnimation.SetKeyFrames(keys);
            keys.Insert(300);
            var motion = new RainfallMotion();
            var signature = effect.MotionSignature(600, 60);
            var first = motion.Evaluate(550, 60, signature, f => effect.GetVelocity(f, 600, 60));
            effect.SpeedAnimation.Values[1].Value = 50;
            var updated = effect.MotionSignature(600, 60);
            require(signature != updated);
            var edited = motion.Evaluate(550, 60, updated, f => effect.GetVelocity(f, 600, 60));
            require(first != edited);
            require(edited == new RainfallMotion().Evaluate(550, 60, updated, f => effect.GetVelocity(f, 600, 60)));
            keys.Insert(450);
            require(updated != effect.MotionSignature(600, 60));
            require(effect.MotionSignature(600, 60) != effect.MotionSignature(1200, 60));
            require(effect.MotionSignature(600, 60) != effect.MotionSignature(600, 30));
        });
        check("10分先への初回シークとキャッシュ再利用", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            SetLinear(effect.SpeedAnimation, 100, 900);
            var motion = new RainfallMotion();
            var signature = effect.MotionSignature(36000, 60);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var end = motion.Evaluate(36000, 60, signature, f => effect.GetVelocity(f, 36000, 60));
            var elapsed = watch.ElapsedMilliseconds;
            var calls = 0;
            var repeated = motion.Evaluate(36000, 60, signature, f => { calls++; return effect.GetVelocity(f, 36000, 60); });
            require(end == repeated && calls <= 1 && double.IsFinite(end.X) && double.IsFinite(end.Y));
            Console.WriteLine($"10分先の移動計算: {elapsed} ms（この端末の単体検証）");
        });
        check("Animationと色・降り始めの保存再読込", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false)
            {
                OnsetEnabled = true,
                StartSeconds = 2,
                AppearanceSeconds = 3,
                Color = System.Windows.Media.Colors.CornflowerBlue
            };
            effect.AmountAnimation.CopyFrom(new Animation(17, 0, 100));
            effect.AngleAnimation.CopyFrom(new Animation(1080, double.MinValue, double.MaxValue));

            var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            require(restored.GetParameters(100, 300, 30) == effect.GetParameters(100, 300, 30));
        });
    }

    internal static void SetLinear(Animation animation, double from, double to)
    {
        animation.AnimationType = AnimationType.直線移動;
#pragma warning disable CS0618
        animation.From = from;
        animation.To = to;
#pragma warning restore CS0618
    }
}
