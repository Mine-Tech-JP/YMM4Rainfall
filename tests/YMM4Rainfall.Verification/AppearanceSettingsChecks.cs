// SPDX-License-Identifier: MPL-2.0
using System.Text.Json.Nodes;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.UndoRedo;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class AppearanceSettingsChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("丸い雪: 4形状・全10設定と初期値、旧5形状の保存データを拒否", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            require(RainfallAppearanceBank.Shapes(WeatherKind.Snow).SequenceEqual(
                new[] { RainfallShape.SnowRound, RainfallShape.SnowClump, RainfallShape.SnowCrystal, RainfallShape.Png }));
            require(Enum.GetValues<WeatherKind>().Sum(k => RainfallAppearanceBank.Shapes(k).Length) == 10);
            require(effect.Shape == RainfallShape.SnowRound && effect.ParticleSize.GetFirstValue() == 6);
            require(effect.SnowSoftness == 50 && !effect.RainSizeMotionLinked && effect.IsSoftSnow);
            require(!Enum.IsDefined((RainfallShape)6));
            var json = JsonNode.Parse(YmmJson.GetJsonText(effect))!.AsObject();
            var variants = json["SnowAppearances"]!["Variants"]!.AsArray();
            var fine = variants[0]!.DeepClone();
            fine["Shape"] = 6;
            variants.Add(fine);
            var old = YmmJson.LoadFromText<RainfallEffect>(json.ToJsonString())!;
            foreach (var kind in Enum.GetValues<WeatherKind>())
            {
                old.Kind = kind;
                require(!old.IsSupported);
            }
        });

        check("丸い雪: 設定例の適用順で柔らかさを持ち越さず連動設定を保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow, RainSizeMotionLinked = true };
            foreach (var (key, softness) in new[] { ("wind-snow", 70), ("quiet-snow", 0), ("vortex", 65), ("blizzard", 0) })
            {
                require(WeatherPresetCatalog.All.Single(p => p.Key == key).ApplyTo(effect));
                require(effect.Shape == RainfallShape.SnowRound && effect.SnowSoftness == softness);
                require(effect.RainSizeMotionLinked);
                if (softness == 0) require(effect.ParticleSize.GetFirstValue() == 6);
            }
        });

        check("見た目別設定: 全10見た目の値とAnimationを独立保存・再読込", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var snapshots = new Dictionary<(WeatherKind, RainfallShape), string>();
            var index = 0;
            foreach (var kind in Enum.GetValues<WeatherKind>())
            foreach (var shape in RainfallAppearanceBank.Shapes(kind))
            {
                effect.Kind = kind; effect.Shape = shape;
                effect.ParticleSize.SetFirstValue(13 + index);
                FeatureChecks.SetLinear(effect.AmountAnimation, 10 + index, 40 + index);
                FeatureChecks.SetLinear(effect.Red, 20 + index, 60 + index);
                effect.Seed = 100 + index;
                effect.StartSeconds = index;
                effect.OnsetEnabled = index % 2 == 0;
                snapshots[(kind, shape)] = YmmJson.GetJsonText(Bank(effect));
                index++;
            }
            effect.SetAnimationParameters(100, 30);
            var keys = new KeyFrames();
            effect.SetKeyFrames(keys);
            keys.Insert(50);
            foreach (var pair in snapshots.Keys.ToArray())
            {
                effect.Kind = pair.Item1; effect.Shape = pair.Item2;
                require(effect.AmountAnimation.Values.Count == 3);
                effect.AmountAnimation.Values[1].Value = effect.Seed - 80;
                snapshots[pair] = YmmJson.GetJsonText(Bank(effect));
            }
            var json = YmmJson.GetJsonText(effect);
            require(!JsonNode.Parse(json)!.AsObject().ContainsKey("RainSettings"));
            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            require(restored.IsSupported && restored.SchemaVersion == 2);
            foreach (var pair in snapshots.Reverse())
            {
                restored.Kind = pair.Key.Item1; restored.Shape = pair.Key.Item2;
                require(YmmJson.GetJsonText(Bank(restored)) == pair.Value);
            }
        });

        check("見た目別設定: 切替と値変更のUndo/Redoが別の見た目を壊さない", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.Circle };
            effect.ParticleSize.SetFirstValue(12);
            effect = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
            var manager = new UndoRedoManager();
            manager.Subscribe(effect);
            try
            {
                effect.Shape = RainfallShape.CartoonDrop; manager.Record();
                var initialDropSize = effect.ParticleSize.GetFirstValue();
                require(initialDropSize > 12);
                effect.ParticleSize.SetFirstValue(35); manager.Record();
                manager.UndoAsync().GetAwaiter().GetResult();
                require(effect.Shape == RainfallShape.CartoonDrop && effect.ParticleSize.GetFirstValue() == initialDropSize);
                manager.UndoAsync().GetAwaiter().GetResult();
                require(effect.Shape == RainfallShape.Circle && effect.ParticleSize.GetFirstValue() == 12);
                manager.RedoAsync().GetAwaiter().GetResult();
                manager.RedoAsync().GetAwaiter().GetResult();
                require(effect.Shape == RainfallShape.CartoonDrop && effect.ParticleSize.GetFirstValue() == 35);
                effect.Shape = RainfallShape.Circle;
                require(effect.ParticleSize.GetFirstValue() == 12);
            }
            finally { manager.UnSubscribe(effect); }
        });

        check("見た目別設定: 形式1の選択中設定を全種類で引き継ぐ", () =>
        {
            var old = new JsonObject { ["SchemaVersion"] = 1, ["Kind"] = (int)WeatherKind.Snow };
            var expected = new Dictionary<WeatherKind, RainfallSettings>();
            foreach (var kind in Enum.GetValues<WeatherKind>())
            {
                var settings = new RainfallSettings(kind) { Shape = RainfallAppearanceBank.Shapes(kind).Last(), Seed = 123 };
                settings.ParticleSize.SetFirstValue(77);
                FeatureChecks.SetLinear(settings.AmountAnimation, 12, 34);
                expected[kind] = settings;
                old[kind switch { WeatherKind.Rain => "RainSettings", WeatherKind.Snow => "SnowSettings",
                    WeatherKind.Bubble => "BubbleSettings", _ => "PngSettings" }] = JsonNode.Parse(YmmJson.GetJsonText(settings));
            }
            var restored = YmmJson.LoadFromText<RainfallEffect>(old.ToJsonString())!;
            require(restored.SchemaVersion == 2 && restored.IsSupported);
            foreach (var pair in expected)
            {
                restored.Kind = pair.Key;
                require(restored.Shape == pair.Value.Shape);
                require(YmmJson.GetJsonText(Bank(restored)) == YmmJson.GetJsonText(pair.Value));
            }
            var reloaded = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(restored))!;
            require(reloaded.IsSupported && reloaded.ParticleSize.GetFirstValue() == 77);
        });

        check("見た目別設定: 設定例は適用先以外の見た目を保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            effect.ParticleSize.SetFirstValue(91);
            effect.Red.SetFirstValue(25);
            var before = YmmJson.GetJsonText(effect.SnowSettings);
            require(WeatherPresetCatalog.All.Single(item => item.Key == "dancing-crystal").ApplyTo(effect));
            require(effect.Shape == RainfallShape.SnowCrystal);
            effect.Shape = RainfallShape.SnowRound;
            require(YmmJson.GetJsonText(effect.SnowSettings) == before);
        });
    }

    private static RainfallSettings Bank(RainfallEffect effect) => effect.Kind switch
    {
        WeatherKind.Rain => effect.RainSettings, WeatherKind.Snow => effect.SnowSettings,
        WeatherKind.Bubble => effect.BubbleSettings, _ => effect.PngSettings,
    };
}
