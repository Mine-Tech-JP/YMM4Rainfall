// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class NewItemDefaultsChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("新規初期値: 修正版テンプレートの全10見た目・全項目に一致", () =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "YMM4Rainfall.Verification.Fixtures.NewItemDefaults.json")!;
            using var document = JsonDocument.Parse(stream);
            var effect = new RainfallEffect();
            foreach (var bankNode in document.RootElement.EnumerateObject())
            {
                var bank = (RainfallAppearanceBank)typeof(RainfallEffect).GetProperty(bankNode.Name)!.GetValue(effect)!;
                foreach (var variant in bankNode.Value.EnumerateArray())
                {
                    var shape = Enum.Parse<RainfallShape>(variant.GetProperty("Shape").GetString()!);
                    var actual = bank.Variants.Single(value => value.Shape == shape);
                    foreach (var expected in variant.EnumerateObject())
                    {
                        var value = typeof(RainfallSettings).GetProperty(expected.Name)!.GetValue(actual)!;
                        var matches = value switch
                        {
                            Animation animation => animation.GetFirstValue() == expected.Value.GetDouble(),
                            Enum enumeration => enumeration.ToString() == expected.Value.GetString(),
                            bool boolean => boolean == expected.Value.GetBoolean(),
                            _ => Convert.ToDouble(value) == expected.Value.GetDouble(),
                        };
                        if (!matches) throw new InvalidOperationException($"初期値不一致: {bankNode.Name}/{shape}/{expected.Name}");
                    }
                    var isDefaultPig = bankNode.Name == "PngAppearances";
                    require(actual.PngReference.Id == (isDefaultPig ? RainfallBuiltInImage.PigId : Guid.Empty));
                    require(actual.PngReference.Revision == (isDefaultPig ? 1 : 0));
                }
            }
            require(effect.Kind == WeatherKind.Rain && effect.Shape == RainfallShape.Streak);
            require(effect.SnowAppearances.SelectedShape == RainfallShape.SnowClump);
            require(effect.BubbleAppearances.SelectedShape == RainfallShape.Bubble);
        });
        check("新規初期値: 両JSON読込経路で旧データの省略値を維持", () =>
        {
            const string saved = "{\"SchemaVersion\":2}";
            var loaded = new[] { YmmJson.LoadFromText<RainfallEffect>(saved)!,
                Newtonsoft.Json.JsonConvert.DeserializeObject<RainfallEffect>(saved)! };
            foreach (var effect in loaded)
            {
                require(effect.SpeedAnimation.GetFirstValue() == 900 && !effect.OnsetEnabled && !effect.MotionBlurEnabled);
                require(effect.SnowAppearances.Current.ParticleSize.GetFirstValue() == 6);
                require(effect.BubbleAppearances.Current.ParticleSize.GetFirstValue() == 48);
                require(effect.PngAppearances.Current.SpeedAnimation.GetFirstValue() == 150);
                require(effect.PngAppearances.Current.PngReference.Id == Guid.Empty);
            }
        });
        check("新規初期値: 保存再読込と独立した新規エフェクト", () =>
        {
            var first = new RainfallEffect();
            var second = new RainfallEffect();
            first.SpeedAnimation.SetFirstValue(321);
            require(second.SpeedAnimation.GetFirstValue() == 4000);
            var json = YmmJson.GetJsonText(first);
            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            require(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(YmmJson.GetJsonText(restored))));
        });
    }
}
