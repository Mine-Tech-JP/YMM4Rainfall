// SPDX-License-Identifier: MPL-2.0
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.UndoRedo;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class WeatherPresetChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("設定例: 追加の表示条件を明示し、適用前の状態によらず保存再読込できる", () =>
        {
            foreach (var preset in WeatherPresetCatalog.All)
            foreach (var previous in new[] { false, true })
            {
                var effect = new RainfallEffect { Kind = preset.Kind, Shape = preset.Shape };
                effect.RainSizeMotionLinked = previous;
                effect.FollowDirection = previous;
                effect.KeepUpright = previous;
                effect.MirrorWhenUpright = previous;
                effect.MotionBlurEnabled = previous;
                effect.MotionBlurMode = MotionBlurMode.Symmetric;
                FeatureChecks.SetLinear(effect.MotionBlurStrength, 10, 90);
                effect.Seed = 132;
                effect.OnsetEnabled = true;
                effect.StartSeconds = 3;
                effect.InitialRotationVariation = 85;
                effect.RotationSpeedVariation = 85;
                require(preset.ApplyTo(effect));
                require(effect.RainSizeMotionLinked && !effect.FollowDirection && !effect.KeepUpright && !effect.MirrorWhenUpright);
                var blur = preset.Key is "wind-rain" or "wind-snow" or "blizzard";
                require(effect.MotionBlurEnabled == blur);
                if (blur) require(effect.MotionBlurMode == MotionBlurMode.Trailing && effect.MotionBlurStrength.GetFirstValue() == 25
                    && effect.MotionBlurStrength.Values.Count == 1);
                if (preset.Kind == WeatherKind.Bubble)
                    require(effect.InitialRotationVariation == 0 && effect.RotationSpeedVariation == 0);
                require(effect.Seed == 132 && effect.OnsetEnabled && effect.StartSeconds == 3);
                require(preset.CreatePreview(effect).Contains("モーションブラー", StringComparison.Ordinal));
                var json = YmmJson.GetJsonText(effect);
                require(YmmJson.GetJsonText(YmmJson.LoadFromText<RainfallEffect>(json)!) == json);
            }
        });

        check("設定例: 10候補を現在の種類だけに絞る", () =>
        {
            require(WeatherPresetCatalog.All.Count == 10);
            require(WeatherPresetCatalog.For(WeatherKind.Snow).Count == 7);
            require(WeatherPresetCatalog.For(WeatherKind.Rain).Single().Name == "風に流れる雨");
            require(WeatherPresetCatalog.For(WeatherKind.Bubble).Select(item => item.Name)
                .SequenceEqual(["輪郭の薄い水泡", "背景を映す水泡"]));
            require(WeatherPresetCatalog.For(WeatherKind.CustomPng).Count == 0);
            require(WeatherPresetCatalog.All.Select(item => item.Key).Distinct().Count() == 10);
        });

        check("設定例: 標準属性経路で表示・切替・適用・再利用", () =>
        {
            var property = typeof(RainfallEffect).GetProperty(nameof(RainfallEffect.PresetTarget))!;
            var attribute = new WeatherPresetEditorAttribute { IsMultiEditing = true };
            var editor = (RainfallPresetEditor)attribute.Create();
            var cache = new PropertiesCache();
            var rain = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Rain };

            attribute.SetBindings(editor, [new ItemProperty(rain, rain, property, cache)]);
            require(ReferenceEquals(editor.PresetTarget, rain));
            require(editor.AvailablePresets.Count == 1);
            require(editor.SelectPreset("wind-rain"));
            require(editor.PreviewText.Contains("風強度: 500 px/秒", StringComparison.Ordinal));
            require(editor.ApplySelectedPreset());
            require(rain.AmountAnimation.GetFirstValue() == 30 && rain.WindEnabled);

            rain.Kind = WeatherKind.Snow;
            require(editor.AvailablePresets.Count == 7 && editor.CanApply);
            rain.Kind = WeatherKind.Bubble;
            require(editor.AvailablePresets.Count == 2 && editor.CanApply);
            rain.Kind = WeatherKind.CustomPng;
            require(editor.AvailablePresets.Count == 0 && !editor.CanApply);
            require(editor.StatusText.Contains("設定例はありません", StringComparison.Ordinal));

            attribute.ClearBindings(editor);
            require(editor.PresetTarget is null && editor.AvailablePresets.Count == 0);
            rain.Kind = WeatherKind.Snow;
            require(editor.PresetTarget is null && editor.AvailablePresets.Count == 0);

            var snow = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            attribute.SetBindings(editor, [new ItemProperty(snow, snow, property, cache)]);
            require(ReferenceEquals(editor.PresetTarget, snow));
            require(editor.AvailablePresets.Count == 7 && editor.CanApply);
            attribute.ClearBindings(editor);
        });

        check("設定例: 選択だけでは設定を変更せず適用内容を日本語表示", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            var before = YmmJson.GetJsonText(effect);
            var editor = new RainfallPresetEditor { PresetTarget = effect };
            require(editor.AvailablePresets.Count == 7);
            require(editor.SelectPreset("wind-snow"));
            require(YmmJson.GetJsonText(effect) == before);
            require(editor.PreviewText.Contains("玉雪", StringComparison.Ordinal));
            require(editor.PreviewText.Contains("量: 28 %", StringComparison.Ordinal));
            require(editor.PreviewText.Contains("風強度: 260 px/秒", StringComparison.Ordinal));
            require(editor.PreviewText.Contains("時間とともに値が変わる設定（Animation）を解除", StringComparison.Ordinal));
            effect.Kind = WeatherKind.Bubble;
            require(editor.AvailablePresets.Count == 2);
        });

        check("設定例: 複数選択では候補と適用を無効化", () =>
        {
            var first = new RainfallEffect(useNewItemDefaults: false);
            var second = new RainfallEffect(useNewItemDefaults: false);
            var editor = new RainfallPresetEditor();
            var attribute = new WeatherPresetEditorAttribute();
            var property = typeof(RainfallEffect).GetProperty(nameof(RainfallEffect.PresetTarget))!;
            var cache = new PropertiesCache();
            attribute.SetBindings(editor, []);
            require(editor.PresetTarget is null && editor.AvailablePresets.Count == 0 && !editor.CanApply);
            require(editor.StatusText.Contains("アイテムを1つ選択", StringComparison.Ordinal));
            attribute.SetBindings(editor,
            [
                new ItemProperty(first, first, property, cache),
                new ItemProperty(second, second, property, cache),
            ]);
            require(editor.AvailablePresets.Count == 0 && !editor.CanApply);
            require(editor.StatusText.Contains("アイテムを1つ選択", StringComparison.Ordinal));
            require(!editor.ApplySelectedPreset());
            attribute.ClearBindings(editor);
        });

        check("設定例: 明示適用をUndo 1回で全復帰できる", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            FeatureChecks.SetLinear(effect.AmountAnimation, 3, 97);
            FeatureChecks.SetLinear(effect.ParticleSize, 2, 200);
            var before = YmmJson.GetJsonText(effect);
            var editor = new RainfallPresetEditor { PresetTarget = effect };
            require(editor.SelectPreset("quiet-snow"));
            var begins = 0;
            var ends = 0;
            var manager = new UndoRedoManager();
            manager.Subscribe(effect);
            editor.BeginEdit += (_, _) => { begins++; effect.BeginEdit(); };
            editor.EndEdit += (_, _) => { ends++; effect.EndEditAsync().AsTask().GetAwaiter().GetResult(); };

            require(editor.ApplySelectedPreset());
            require(begins == 1 && ends == 1);
            require(effect.Shape == RainfallShape.SnowRound);
            require(effect.AmountAnimation.AnimationType == AnimationType.なし && effect.AmountAnimation.Values.Count == 1);
            require(effect.AmountAnimation.GetFirstValue() == 12);
            require(effect.ParticleSize.AnimationType == AnimationType.なし && effect.ParticleSize.Values.Count == 1);
            require(effect.ParticleSize.GetFirstValue() == 6);

            var after = YmmJson.GetJsonText(effect);
            manager.Record();
            require(manager.IsUndoable);
            manager.UndoAsync().GetAwaiter().GetResult();
            require(YmmJson.GetJsonText(effect) == before);
            require(manager.IsRedoable);
            manager.RedoAsync().GetAwaiter().GetResult();
            require(YmmJson.GetJsonText(effect) == after);
            manager.UnSubscribe(effect);
        });

        check("設定例: 色・乱数・出現開始・PNG参照・他種類を変更しない", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Shape = RainfallShape.SnowCrystal };
            var imageId = Guid.NewGuid();
            effect.PngImageId = imageId;
            effect.PngImageRevision = 17;
            effect.Seed = 123;
            effect.OnsetEnabled = true;
            effect.StartSeconds = 2.5;
            effect.RampEnabled = true;
            effect.RampSeconds = 8;
            effect.ColorAlpha = 111;
            FeatureChecks.SetLinear(effect.Red, 10, 80);
            FeatureChecks.SetLinear(effect.Green, 20, 70);
            FeatureChecks.SetLinear(effect.Blue, 30, 60);
            FeatureChecks.SetLinear(effect.LensReflection, 12, 34);
            var red = YmmJson.GetJsonText(effect.Red);
            var green = YmmJson.GetJsonText(effect.Green);
            var blue = YmmJson.GetJsonText(effect.Blue);
            var unusedAnimation = YmmJson.GetJsonText(effect.LensReflection);
            var rain = YmmJson.GetJsonText(effect.RainSettings);
            var bubble = YmmJson.GetJsonText(effect.BubbleSettings);
            var png = YmmJson.GetJsonText(effect.PngSettings);

            var preset = WeatherPresetCatalog.All.Single(item => item.Key == "dancing-crystal");
            require(preset.ApplyTo(effect));
            require(effect.PngImageId == imageId && effect.PngImageRevision == 17);
            require(effect.Seed == 123 && effect.OnsetEnabled && effect.StartSeconds == 2.5);
            require(effect.RampEnabled && effect.RampSeconds == 8 && effect.ColorAlpha == 111);
            require(YmmJson.GetJsonText(effect.Red) == red && YmmJson.GetJsonText(effect.Green) == green &&
                YmmJson.GetJsonText(effect.Blue) == blue);
            require(YmmJson.GetJsonText(effect.LensReflection) == unusedAnimation);
            require(YmmJson.GetJsonText(effect.RainSettings) == rain);
            require(YmmJson.GetJsonText(effect.BubbleSettings) == bubble);
            require(YmmJson.GetJsonText(effect.PngSettings) == png);
        });

        check("設定例: 降雪PNGの形と参照を保って動きだけを適用", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            effect.SnowAppearance = SnowAppearance.Png;
            var imageId = Guid.NewGuid();
            effect.PngImageId = imageId;
            effect.PngImageRevision = 29;
            var editor = new RainfallPresetEditor { PresetTarget = effect };
            require(editor.SelectPreset("vortex"));
            require(editor.PreviewText.Contains("現在のPNGを保持", StringComparison.Ordinal));
            require(editor.ApplySelectedPreset());
            require(effect.Kind == WeatherKind.Snow && effect.Shape == RainfallShape.Png);
            require(effect.PngImageId == imageId && effect.PngImageRevision == 29);
            require(effect.SnowMotion == SnowMotionKind.Vortex);
            require(effect.VortexRadius.GetFirstValue() == 400 && effect.VortexSpeed.GetFirstValue() == 180);
        });

        check("設定例: 種類が異なる候補を適用しない", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Rain };
            var before = YmmJson.GetJsonText(effect);
            var snow = WeatherPresetCatalog.All.Single(item => item.Key == "blizzard");
            require(!snow.ApplyTo(effect));
            require(YmmJson.GetJsonText(effect) == before);
        });

        check("設定例: 未対応の保存形式・形状を上書きしない", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow, SchemaVersion = 99 };
            var before = YmmJson.GetJsonText(effect);
            var preset = WeatherPresetCatalog.All.Single(item => item.Key == "quiet-snow");
            require(!preset.ApplyTo(effect));
            require(YmmJson.GetJsonText(effect) == before);

            var editor = new RainfallPresetEditor { PresetTarget = effect };
            require(!editor.CanApply);
            require(editor.StatusText.Contains("対応していない", StringComparison.Ordinal));
        });
    }

}
