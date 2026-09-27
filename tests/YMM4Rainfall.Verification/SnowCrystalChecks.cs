// SPDX-License-Identifier: MPL-2.0
using System.Text.Json.Nodes;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.UndoRedo;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class SnowCrystalChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("結晶の形: 非選択時の保存とUndo/Redoで選択を保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.SnowCrystal };
            require(effect.IsCrystalSnow && effect.CrystalStyle == SnowCrystalStyle.Mixed);
            effect.CrystalStyle = SnowCrystalStyle.Fernlike;
            effect.Shape = RainfallShape.SnowClump;
            require(!effect.IsCrystalSnow && effect.CrystalStyle == SnowCrystalStyle.Mixed);
            effect.Kind = WeatherKind.Rain;
            require(effect.CrystalStyle == SnowCrystalStyle.Mixed);
            var json = YmmJson.GetJsonText(effect);
            var document = JsonNode.Parse(json)!.AsObject();
            require(!document.ContainsKey(nameof(RainfallEffect.CrystalStyle)));
            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            restored.Kind = WeatherKind.Snow;
            require(restored.CrystalStyle == SnowCrystalStyle.Mixed);
            restored.Shape = RainfallShape.SnowCrystal;
            require(restored.IsCrystalSnow && restored.IsSupported && restored.CrystalStyle == SnowCrystalStyle.Fernlike);
            var manager = new UndoRedoManager();
            manager.Subscribe(restored);
            try
            {
                restored.CrystalStyle = SnowCrystalStyle.HexagonalPlate;
                manager.Record();
                manager.UndoAsync().GetAwaiter().GetResult();
                require(restored.CrystalStyle == SnowCrystalStyle.Fernlike);
                manager.RedoAsync().GetAwaiter().GetResult();
                require(restored.CrystalStyle == SnowCrystalStyle.HexagonalPlate);
                require(restored.RainSettings.CrystalStyle == SnowCrystalStyle.Mixed);
            }
            finally
            {
                manager.UnSubscribe(restored);
            }
        });

        check("結晶の形: 指定した6形状を全粒へ反映し軌道を保つ", () =>
        {
            var bounds = new RainfallBounds(0, 0, 1920, 1080);
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.SnowCrystal };
            effect.AmountAnimation.SetFirstValue(10);
            var source = new WeatherFrameSource();
            var baseline = source.Evaluate(bounds, 120, 240, 60, effect.MotionSignature(240, 60),
                frame => effect.GetParameters(frame, 240, 60));
            foreach (var style in Enum.GetValues<SnowCrystalStyle>().Where(style => style != SnowCrystalStyle.Mixed))
            {
                effect.CrystalStyle = style;
                var strokes = source.Evaluate(bounds, 120, 240, 60, effect.MotionSignature(240, 60),
                    frame => effect.GetParameters(frame, 240, 60));
                require(strokes.Length > 0 && strokes.All(stroke => stroke.Variant == (int)style - 1));
                require(strokes.Select(stroke => stroke.Head).SequenceEqual(baseline.Select(stroke => stroke.Head)));
                var direct = RainfallSimulation.CreateFrame(bounds, effect.GetParameters(120, 240, 60), 2);
                require(direct.Length > 0 && direct.All(stroke => stroke.Variant == (int)style - 1));
            }
        });

        check("結晶の混在: 6形状を使用し時間・保存・逆シークで粒ごとの形を保持", () =>
        {
            var bounds = new RainfallBounds(0, 0, 1920, 1080);
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.SnowCrystal };
            effect.AmountAnimation.SetFirstValue(10);
            var source = new WeatherFrameSource();
            RainfallStroke[] Evaluate(long frame) => source.Evaluate(bounds, frame, 240, 60,
                effect.MotionSignature(240, 60), sample => effect.GetParameters(sample, 240, 60));
            var first = Evaluate(0);
            require(first.Select(stroke => stroke.Variant).Distinct().Order()
                .SequenceEqual(Enumerable.Range(0, 6)));
            var later = Evaluate(180);
            require(later.Select(stroke => stroke.Variant).SequenceEqual(first.Select(stroke => stroke.Variant)));
            require(Evaluate(0).SequenceEqual(first));
            var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            var restoredStrokes = new WeatherFrameSource().Evaluate(bounds, 180, 240, 60,
                restored.MotionSignature(240, 60), sample => restored.GetParameters(sample, 240, 60));
            require(restoredStrokes.SequenceEqual(later));
            effect.Seed = 2026;
            require(!Evaluate(180).Select(stroke => stroke.Variant).SequenceEqual(later.Select(stroke => stroke.Variant)));
        });
    }
}
