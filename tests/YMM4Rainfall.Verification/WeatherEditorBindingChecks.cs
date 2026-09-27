// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.UndoRedo;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class WeatherEditorBindingChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        check("ブラーUI: 見た目切替・無効時の値保持・途中点のUndo/Redo", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { MotionBlurEnabled = true };
            effect.MotionBlurMode = MotionBlurMode.Trailing;
            var binding = Bind(nameof(RainfallEffect.MotionBlurStrength), effect);
            try
            {
                SetUiValue(binding.Slider, 80);
                effect.Shape = RainfallShape.Circle;
                require(!effect.MotionBlurEnabled && effect.MotionBlurStrength.GetFirstValue() == 50);
                require(effect.MotionBlurMode == MotionBlurMode.Symmetric);
                SetUiValue(binding.Slider, 25);
                effect.Shape = RainfallShape.Streak;
                require(effect.MotionBlurEnabled && effect.MotionBlurStrength.GetFirstValue() == 80);
                require(effect.MotionBlurMode == MotionBlurMode.Trailing);
                effect.MotionBlurEnabled = false; effect.MotionBlurEnabled = true;
                require(effect.MotionBlurStrength.GetFirstValue() == 80);
                FeatureChecks.SetLinear(effect.MotionBlurStrength, 10, 90);
                effect.SetAnimationParameters(60, 30);
                var keys = new KeyFrames(); effect.SetKeyFrames(keys); keys.Insert(30);
                var manager = new UndoRedoManager(); manager.Subscribe(effect);
                try
                {
                    var before = effect.MotionBlurStrength.Values[1].Value;
                    effect.MotionBlurStrength.Values[1].Value = 70; manager.Record();
                    manager.UndoAsync().GetAwaiter().GetResult(); require(effect.MotionBlurStrength.Values[1].Value == before);
                    manager.RedoAsync().GetAwaiter().GetResult(); require(effect.MotionBlurStrength.Values[1].Value == 70);
                }
                finally { manager.UnSubscribe(effect); }
            }
            finally { binding.Attribute.ClearBindings(binding.Slider); }
        });

        check("PNG倍率: 初期値・UI切替・保存・別のPNG設定の保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.CustomPng };
            var binding = Bind(nameof(RainfallEffect.PngScale), effect);
            try
            {
                require(effect.PngScale.GetFirstValue() == 100 && !effect.IsBuiltinParticle);
                SetUiValue(binding.Slider, 250);
                effect.Kind = WeatherKind.Snow; effect.Shape = RainfallShape.Png;
                require(effect.PngScale.GetFirstValue() == 100);
                SetUiValue(binding.Slider, 50);
                var restored = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(effect))!;
                require(restored.PngScale.GetFirstValue() == 50);
                restored.Kind = WeatherKind.CustomPng;
                require(restored.PngScale.GetFirstValue() == 250);
                var parameters = restored.GetParameters(0, 60, 30, 1024).Normalize();
                require(parameters.ParticleSize == 2560);
                restored.Shape = RainfallShape.Circle;
                require(restored.IsBuiltinParticle && !restored.IsPng && restored.ParticleSize.GetFirstValue() == 8);
            }
            finally { binding.Attribute.ClearBindings(binding.Slider); }
        });

        check("PNG倍率: Animation途中点・Undo・大画像の逆シーク", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.CustomPng };
            FeatureChecks.SetLinear(effect.PngScale, 50, 200);
            effect.SetAnimationParameters(60, 30);
            var keys = new KeyFrames(); effect.SetKeyFrames(keys); keys.Insert(30);
            require(effect.PngScale.Values.Count == 3);
            var manager = new UndoRedoManager(); manager.Subscribe(effect);
            try
            {
                var previous = effect.PngScale.Values[1].Value;
                effect.PngScale.Values[1].Value = 150; manager.Record(); manager.UndoAsync().GetAwaiter().GetResult();
                require(effect.PngScale.Values[1].Value == previous);
                manager.RedoAsync().GetAwaiter().GetResult(); require(effect.PngScale.Values[1].Value == 150);
                var source = new WeatherFrameSource();
                RainfallStroke[] At(long frame) => source.Evaluate(new(0, 0, 1920, 1080), frame, 60, 30,
                    effect.MotionSignature(60, 30), index => effect.GetParameters(index, 60, 30, 1024));
                var first = At(30); _ = At(59);
                require(first.SequenceEqual(At(30)) && first.Any(stroke => stroke.Size > 512));
            }
            finally { manager.UnSubscribe(effect); }
        });

        check("PNG状態: 設定例UI接続中の別スレッド通知と種類切替", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.CustomPng };
            var editor = new RainfallPresetEditor { PresetTarget = effect };
            try
            {
                Task.Run(() => effect.SetPngStatus("256 × 256 px")).GetAwaiter().GetResult();
                require(effect.PngStatus == "256 × 256 px");
                effect.Kind = WeatherKind.Snow;
                effect.Shape = RainfallShape.Png;
                Task.Run(() => effect.SetPngStatus("128 × 128 px")).GetAwaiter().GetResult();
                require(effect.PngStatus == "128 × 128 px");
                effect.Kind = WeatherKind.CustomPng;
                require(effect.PngStatus == "256 × 256 px");
            }
            finally { editor.PresetTarget = null; }
        });

        check("種類別UI: 追加直後は全Animationを降雨bankへ反映", () =>
        {
            var properties = typeof(RainfallEffect).GetProperties()
                .Where(property => property.PropertyType == typeof(Animation))
                .Where(property => property.GetCustomAttribute<WeatherAnimationSliderAttribute>() is not null)
                .ToArray();
            require(properties.Length >= new RainfallEffect().RainSettings.AllAnimations.Length);

            foreach (var property in properties)
            {
                var effect = new RainfallEffect(useNewItemDefaults: false);
                var otherBanks = new[] { effect.SnowSettings, effect.BubbleSettings, effect.PngSettings };
                var otherBankJson = otherBanks.Select(bank => YmmJson.GetJsonText(bank)).ToArray();
                var binding = Bind(property, effect);

                var expected = (Animation)property.GetValue(effect)!;
                require(ReferenceEquals(binding.Slider.Animation, expected));
                SetUiValue(binding.Slider, 2);
                require(expected.GetFirstValue() == 2);
                require(otherBanks.Select(bank => YmmJson.GetJsonText(bank)).SequenceEqual(otherBankJson));
                binding.Attribute.ClearBindings(binding.Slider);
            }
        });

        check("種類別UI: 4種類の切替後に対応bankだけを編集", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var amount = Bind(nameof(RainfallEffect.AmountAnimation), effect);
            var rainLength = Bind(nameof(RainfallEffect.LengthAnimation), effect);
            var snowSpeed = Bind(nameof(RainfallEffect.SnowSpeed), effect);
            var bubbleReflection = Bind(nameof(RainfallEffect.LensReflection), effect);
            var pngSize = Bind(nameof(RainfallEffect.ParticleSize), effect);
            var bindings = new[] { amount, rainLength, snowSpeed, bubbleReflection, pngSize };

            SetKind(WeatherKind.Rain);
            RequireCurrentReferences(effect, bindings);
            SetUiValue(amount.Slider, 11);
            SetUiValue(rainLength.Slider, 31);

            SetKind(WeatherKind.Snow);
            RequireCurrentReferences(effect, bindings);
            SetUiValue(amount.Slider, 22);
            SetUiValue(snowSpeed.Slider, 122);

            SetKind(WeatherKind.Bubble);
            RequireCurrentReferences(effect, bindings);
            SetUiValue(amount.Slider, 33);
            SetUiValue(bubbleReflection.Slider, 43);

            SetKind(WeatherKind.CustomPng);
            RequireCurrentReferences(effect, bindings);
            SetUiValue(amount.Slider, 44);
            SetUiValue(pngSize.Slider, 144);

            SetKind(WeatherKind.Rain);
            RequireCurrentReferences(effect, bindings);
            SetUiValue(amount.Slider, 15);

            require(effect.RainSettings.AmountAnimation.GetFirstValue() == 15);
            require(effect.SnowSettings.AmountAnimation.GetFirstValue() == 22);
            require(effect.BubbleSettings.AmountAnimation.GetFirstValue() == 33);
            require(effect.PngSettings.AmountAnimation.GetFirstValue() == 44);
            require(effect.RainSettings.LengthAnimation.GetFirstValue() == 31);
            require(effect.SnowSettings.SpeedAnimation.GetFirstValue() == 122);
            require(effect.BubbleSettings.LensReflection.GetFirstValue() == 43);
            require(effect.PngSettings.ParticleSize.GetFirstValue() == 144);

            foreach (var binding in bindings)
                binding.Attribute.ClearBindings(binding.Slider);

            void SetKind(WeatherKind kind) => effect.Kind = kind;
        });

        check("種類別UI: 種類変更と値編集のUndo/Redoへ参照先も追従", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var binding = Bind(nameof(RainfallEffect.AmountAnimation), effect);
            var manager = new UndoRedoManager();
            manager.Subscribe(effect);
            try
            {
                effect.Kind = WeatherKind.Snow;
                manager.Record();
                SetUiValue(binding.Slider, 72);
                manager.Record();
                manager.UndoAsync().GetAwaiter().GetResult();
                require(effect.Kind == WeatherKind.Snow && effect.SnowSettings.AmountAnimation.GetFirstValue() == 20);
                require(ReferenceEquals(binding.Slider.Animation, effect.SnowSettings.AmountAnimation));
                manager.UndoAsync().GetAwaiter().GetResult();
                require(effect.Kind == WeatherKind.Rain);
                require(ReferenceEquals(binding.Slider.Animation, effect.RainSettings.AmountAnimation));
                manager.RedoAsync().GetAwaiter().GetResult();
                manager.RedoAsync().GetAwaiter().GetResult();
                require(effect.Kind == WeatherKind.Snow && effect.SnowSettings.AmountAnimation.GetFirstValue() == 72);
                require(effect.RainSettings.AmountAnimation.GetFirstValue() == 30);
                require(ReferenceEquals(binding.Slider.Animation, effect.SnowSettings.AmountAnimation));
            }
            finally
            {
                binding.Attribute.ClearBindings(binding.Slider);
            }
        });

        check("種類別UI: 同じ参照の通知では編集中ViewModelを維持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var binding = Bind(nameof(RainfallEffect.AmountAnimation), effect);
            var viewModel = binding.Slider.ViewModel;
            var animation = binding.Slider.Animation;

            SetUiValue(binding.Slider, 37);
            require(ReferenceEquals(binding.Slider.Animation, animation));
            require(ReferenceEquals(binding.Slider.ViewModel, viewModel));
            require(effect.RainSettings.AmountAnimation.GetFirstValue() == 37);
            require(ReferenceEquals(binding.Slider.Animation, animation));
            require(ReferenceEquals(binding.Slider.ViewModel, viewModel));
            binding.Attribute.ClearBindings(binding.Slider);
        });

        check("種類別UI: ClearBindings後は通知を購読しない", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var binding = Bind(nameof(RainfallEffect.AmountAnimation), effect);
            binding.Attribute.ClearBindings(binding.Slider);
            effect.Kind = WeatherKind.Snow;

            require(binding.Slider.Animation is null);
            require(binding.Slider.Animations is null or { Length: 0 });
        });

        check("種類別UI: 複数選択でも各アイテムの対応bankを編集", () =>
        {
            var first = new RainfallEffect(useNewItemDefaults: false);
            var second = new RainfallEffect(useNewItemDefaults: false);
            var property = typeof(RainfallEffect).GetProperty(nameof(RainfallEffect.AmountAnimation))!;
            var attribute = property.GetCustomAttribute<WeatherAnimationSliderAttribute>()!;
            var slider = (AnimationSlider)attribute.Create();
            var cache = new PropertiesCache();
            attribute.SetBindings(slider,
            [
                new ItemProperty(first, first, property, cache),
                new ItemProperty(second, second, property, cache),
            ]);

            SetUiValue(slider, 17);
            require(first.RainSettings.AmountAnimation.GetFirstValue() == 17);
            require(second.RainSettings.AmountAnimation.GetFirstValue() == 17);

            first.Kind = WeatherKind.Snow;
            second.Kind = WeatherKind.Snow;
            SetUiValue(slider, 27);
            require(first.SnowSettings.AmountAnimation.GetFirstValue() == 27);
            require(second.SnowSettings.AmountAnimation.GetFirstValue() == 27);
            require(first.RainSettings.AmountAnimation.GetFirstValue() == 17);
            require(second.RainSettings.AmountAnimation.GetFirstValue() == 17);

            first.Kind = WeatherKind.Bubble;
            second.Kind = WeatherKind.CustomPng;
            SetUiValue(slider, 39);
            require(first.BubbleSettings.AmountAnimation.GetFirstValue() == 39);
            require(second.PngSettings.AmountAnimation.GetFirstValue() == 39);
            require(first.SnowSettings.AmountAnimation.GetFirstValue() == 27);
            require(second.SnowSettings.AmountAnimation.GetFirstValue() == 27);
            attribute.ClearBindings(slider);
        });

        check("種類別UI: 同じ属性の複数controlを独立管理", () =>
        {
            var first = new RainfallEffect(useNewItemDefaults: false);
            var second = new RainfallEffect(useNewItemDefaults: false);
            var property = typeof(RainfallEffect).GetProperty(nameof(RainfallEffect.Opacity))!;
            var attribute = new WeatherAnimationSliderAttribute("F1", "%", 0, 100);
            var firstSlider = (AnimationSlider)attribute.Create();
            var secondSlider = (AnimationSlider)attribute.Create();
            attribute.SetBindings(firstSlider,
                [new ItemProperty(first, first, property, new PropertiesCache())]);
            attribute.SetBindings(secondSlider,
                [new ItemProperty(second, second, property, new PropertiesCache())]);

            first.Kind = WeatherKind.Snow;
            second.Kind = WeatherKind.Bubble;
            require(ReferenceEquals(firstSlider.Animation, first.SnowSettings.Opacity));
            require(ReferenceEquals(secondSlider.Animation, second.BubbleSettings.Opacity));
            SetUiValue(firstSlider, 71);
            SetUiValue(secondSlider, 62);
            require(first.SnowSettings.Opacity.GetFirstValue() == 71);
            require(second.BubbleSettings.Opacity.GetFirstValue() == 62);

            attribute.ClearBindings(firstSlider);
            first.Kind = WeatherKind.Rain;
            second.Kind = WeatherKind.CustomPng;
            require(firstSlider.Animation is null);
            require(ReferenceEquals(secondSlider.Animation, second.PngSettings.Opacity));
            attribute.ClearBindings(secondSlider);
        });
    }

    private static BoundSlider Bind(
        string propertyName,
        RainfallEffect effect) => Bind(typeof(RainfallEffect).GetProperty(propertyName)!, effect);

    private static BoundSlider Bind(
        PropertyInfo property,
        RainfallEffect effect)
    {
        var attribute = property.GetCustomAttribute<WeatherAnimationSliderAttribute>()!;
        var slider = (AnimationSlider)attribute.Create();
        attribute.SetBindings(slider, [new ItemProperty(effect, effect, property, new PropertiesCache())]);
        return new(property, attribute, slider);
    }

    private static void SetUiValue(AnimationSlider slider, double value)
    {
        var viewModel = slider.ViewModel ?? throw new InvalidOperationException("スライダーの編集モデルがありません。");
        viewModel.Values[0].Value = value;
    }

    private static void RequireCurrentReferences(
        RainfallEffect effect,
        IEnumerable<BoundSlider> bindings)
    {
        foreach (var binding in bindings)
        {
            if (!ReferenceEquals(binding.Slider.Animation, binding.Property.GetValue(effect)))
                throw new InvalidOperationException($"{effect.Kind}の{binding.Property.Name}が現在のbankを参照していません。");
        }
    }

    private sealed record BoundSlider(
        PropertyInfo Property,
        WeatherAnimationSliderAttribute Attribute,
        AnimationSlider Slider);
}
