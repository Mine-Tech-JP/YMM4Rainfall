// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;

namespace YMM4Rainfall;

internal sealed class WeatherPresetEditorAttribute : PropertyEditorAttribute2
{
    public WeatherPresetEditorAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;
    public override FrameworkElement Create() => new RainfallPresetEditor();

    public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(itemProperties);
        var editor = (RainfallPresetEditor)control;
        BindingOperations.ClearBinding(editor, RainfallPresetEditor.PresetTargetProperty);
        editor.PresetTarget = null;
        var multiple = itemProperties.Length != 1;
        editor.SetMultipleSelection(multiple);
        if (multiple)
        {
            return;
        }
        editor.PresetTarget = itemProperties[0].GetValue<RainfallEffect>();
    }

    public override void ClearBindings(FrameworkElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var editor = (RainfallPresetEditor)control;
        BindingOperations.ClearBinding(editor, RainfallPresetEditor.PresetTargetProperty);
        editor.PresetTarget = null;
        editor.SetMultipleSelection(false);
    }
}

/// <summary>種類に合う設定例を確認し、明示操作時だけ現在の設定へ適用します。</summary>
internal sealed class RainfallPresetEditor : UserControl, IPropertyEditorControl2
{
    public static readonly DependencyProperty PresetTargetProperty = DependencyProperty.Register(
        nameof(PresetTarget), typeof(RainfallEffect), typeof(RainfallPresetEditor),
        new PropertyMetadata(null, (sender, args) =>
            ((RainfallPresetEditor)sender).OnTargetChanged(args.OldValue as RainfallEffect, args.NewValue as RainfallEffect)));

    private readonly ComboBox selector = new() { MinWidth = 190, DisplayMemberPath = nameof(WeatherPresetDefinition.Name) };
    private readonly TextBlock preview = new() { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button applyButton;
    private bool multipleSelection;

    public RainfallEffect? PresetTarget
    {
        get => (RainfallEffect?)GetValue(PresetTargetProperty);
        set => SetValue(PresetTargetProperty, value);
    }

    public event EventHandler? BeginEdit;
    public event EventHandler? EndEdit;

    public RainfallPresetEditor()
    {
        var row = new WrapPanel();
        row.Children.Add(selector);
        applyButton = new Button
        {
            Content = "適用",
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 1, 8, 1),
        };
        applyButton.Click += (_, _) => ApplySelectedPreset();
        row.Children.Add(applyButton);

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "設定例を選び、変更内容を確認して「適用」を押します。選ぶだけでは設定は変わりません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });
        root.Children.Add(row);
        root.Children.Add(preview);
        root.Children.Add(status);
        Content = root;
        Focusable = true;
        selector.SelectionChanged += (_, _) => RefreshPreview();
        RefreshPresets();
    }

    internal IReadOnlyList<WeatherPresetDefinition> AvailablePresets =>
        selector.ItemsSource as IReadOnlyList<WeatherPresetDefinition> ?? [];
    internal string PreviewText => preview.Text;
    internal string StatusText => status.Text;
    internal bool CanApply => applyButton.IsEnabled;

    public void SetEditorInfo(IEditorInfo? info) => _ = info;

    public void SetFocus()
    {
        if (!selector.Focus())
        {
            Keyboard.Focus(selector);
        }
    }

    internal bool SelectPreset(string key)
    {
        var preset = AvailablePresets.FirstOrDefault(item => item.Key == key);
        if (preset is null)
        {
            return false;
        }
        selector.SelectedItem = preset;
        return true;
    }

    internal void SetMultipleSelection(bool value)
    {
        multipleSelection = value;
        if (value)
        {
            SetCurrentValue(PresetTargetProperty, null);
        }
        RefreshPresets();
    }

    internal bool ApplySelectedPreset()
    {
        if (PresetTarget is not { } target || selector.SelectedItem is not WeatherPresetDefinition preset)
        {
            return false;
        }
        if (!target.IsSupported || target.Kind != preset.Kind)
        {
            status.Text = "対応していない保存形式・種類・形状には設定例を適用できません。";
            return false;
        }

        BeginEdit?.Invoke(this, EventArgs.Empty);
        var applied = false;
        try
        {
            applied = preset.ApplyTo(target);
            return applied;
        }
        finally
        {
            EndEdit?.Invoke(this, EventArgs.Empty);
            status.Text = applied
                ? $"「{preset.Name}」を現在の{WeatherPresetCatalog.KindName(target.Kind)}設定へ適用しました。"
                : "選択中の種類にはこの設定例を適用できません。";
        }
    }

    private void OnTargetChanged(RainfallEffect? oldTarget, RainfallEffect? newTarget)
    {
        if (oldTarget is not null) oldTarget.PropertyChanged -= TargetPropertyChanged;
        if (newTarget is not null) newTarget.PropertyChanged += TargetPropertyChanged;
        RefreshPresets();
    }

    private void TargetPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RainfallEffect.Kind))
        {
            RefreshPresets();
        }
        else if (args.PropertyName is nameof(RainfallEffect.Shape) or nameof(RainfallEffect.SchemaVersion)
            or nameof(RainfallEffect.SnowMotion) or nameof(RainfallEffect.IsSupported))
        {
            applyButton.IsEnabled = !multipleSelection && PresetTarget?.IsSupported == true && selector.SelectedItem is not null;
            RefreshPreview();
        }
    }

    private void RefreshPresets()
    {
        var presets = !multipleSelection && PresetTarget is { } target ? WeatherPresetCatalog.For(target.Kind) : [];
        selector.ItemsSource = presets;
        selector.SelectedIndex = presets.Count > 0 ? 0 : -1;
        applyButton.IsEnabled = !multipleSelection && PresetTarget?.IsSupported == true && presets.Count > 0;
        status.Text = multipleSelection
            ? "設定例を使うには、アイテムを1つ選択してください。"
            : PresetTarget is { IsSupported: false }
                ? "対応していない保存形式・種類・形状には設定例を適用できません。"
                : PresetTarget?.Kind == WeatherKind.CustomPng
                    ? "カスタムPNG用の設定例はありません。現在の設定をそのまま調整してください。"
                    : string.Empty;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        preview.Text = PresetTarget is { } target && selector.SelectedItem is WeatherPresetDefinition preset
            ? preset.CreatePreview(target)
            : string.Empty;
    }
}

internal sealed record WeatherPresetDefinition(
    string Key,
    WeatherKind Kind,
    string Name,
    string Purpose,
    RainfallShape Shape,
    IReadOnlyList<WeatherPresetOperation> Operations)
{
    public bool ApplyTo(RainfallEffect target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsSupported || target.Kind != Kind)
        {
            return false;
        }

        // 降雪PNGは選択中の画像と外形を保ち、動きと基本値だけを設定例から適用します。
        if (!(Kind == WeatherKind.Snow && target.Shape == RainfallShape.Png))
        {
            target.Shape = Shape;
        }
        foreach (var operation in Operations)
        {
            operation.Apply(target);
        }
        return true;
    }

    public string CreatePreview(RainfallEffect target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var shape = Kind == WeatherKind.Snow && target.Shape == RainfallShape.Png
            ? $"見た目: 現在のPNGを保持（設定例の候補は{WeatherPresetCatalog.ShapeName(Shape)}）"
            : $"見た目: {WeatherPresetCatalog.ShapeName(Shape)}";
        return $"{Purpose}\n\n適用する数値項目は、時間とともに値が変わる設定（Animation）を解除し、下記の値に固定します。粒は適用後の速度や回転の設定に従って動きます。\n\n適用する項目\n{shape}\n{string.Join("\n", Operations.Select(operation => operation.Description))}";
    }
}

internal sealed record WeatherPresetOperation(string Description, Action<RainfallEffect> Apply);

internal static class WeatherPresetCatalog
{
    private static readonly WeatherPresetDefinition[] presets =
    [
        Snow("quiet-snow", "静かな細雪", "小さな玉雪を少なめに落とし、控えめな漂いを加えます。", RainfallShape.SnowRound,
            F("輪郭柔らかさ", "0%", effect => effect.SnowSoftness = 0),
            A("量", 12, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 6, "px", effect => effect.ParticleSize, 1, 512),
            A("落下速度", 80, "px/秒", effect => effect.SnowSpeed, 0, 4000),
            A("不透明度", 75, "%", effect => effect.Opacity, 0, 100),
            A("速度ばらつき", 25, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 35, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 20, "%", effect => effect.OpacityVariation, 0, 100),
            F("動き", "基本降雪", effect => effect.SnowMotion = SnowMotionKind.Basic),
            F("風", "無効", effect => effect.WindEnabled = false),
            F("漂い", "有効", effect => effect.DriftEnabled = true),
            A("漂い幅", 15, "px", effect => effect.DriftWidth, 0, 200),
            F("漂い速度", "0.7倍", effect => effect.DriftRate = 0.7),
            F("不規則さ", "35%", effect => effect.DriftIrregularity = 35),
            F("回転速度", "8°/秒", effect => effect.RotationSpeed = 8),
            F("初期角度ばらつき", "100%", effect => effect.InitialRotationVariation = 100),
            F("回転速度ばらつき", "40%", effect => effect.RotationSpeedVariation = 40)),

        Snow("fluttering-clump", "ひらひらしたぼたん雪", "大小の不揃いな塊を、広めの漂いと遅い回転で落とします。", RainfallShape.SnowClump,
            A("量", 18, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 40, "px", effect => effect.ParticleSize, 1, 512),
            A("落下速度", 100, "px/秒", effect => effect.SnowSpeed, 0, 4000),
            A("不透明度", 85, "%", effect => effect.Opacity, 0, 100),
            A("速度ばらつき", 30, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 75, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 15, "%", effect => effect.OpacityVariation, 0, 100),
            F("輪郭柔らかさ", "45%", effect => effect.SnowSoftness = 45),
            F("動き", "基本降雪", effect => effect.SnowMotion = SnowMotionKind.Basic),
            F("風", "無効", effect => effect.WindEnabled = false),
            F("漂い", "有効", effect => effect.DriftEnabled = true),
            A("漂い幅", 50, "px", effect => effect.DriftWidth, 0, 200),
            F("漂い速度", "0.65倍", effect => effect.DriftRate = 0.65),
            F("不規則さ", "70%", effect => effect.DriftIrregularity = 70),
            F("回転速度", "18°/秒", effect => effect.RotationSpeed = 18),
            F("初期角度ばらつき", "100%", effect => effect.InitialRotationVariation = 100),
            F("回転速度ばらつき", "65%", effect => effect.RotationSpeedVariation = 65)),

        Snow("wind-snow", "風に流れる雪", "玉雪へ横風を加え、粒ごとに風への反応を変えます。", RainfallShape.SnowRound,
            A("量", 28, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 10, "px", effect => effect.ParticleSize, 1, 512),
            A("落下速度", 120, "px/秒", effect => effect.SnowSpeed, 0, 4000),
            A("不透明度", 78, "%", effect => effect.Opacity, 0, 100),
            A("速度ばらつき", 40, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 45, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 20, "%", effect => effect.OpacityVariation, 0, 100),
            F("輪郭柔らかさ", "70%", effect => effect.SnowSoftness = 70),
            F("動き", "基本降雪", effect => effect.SnowMotion = SnowMotionKind.Basic),
            F("風", "有効", effect => effect.WindEnabled = true),
            A("風向", 90, "°", effect => effect.WindAngle, double.MinValue, double.MaxValue),
            A("風強度", 260, "px/秒", effect => effect.WindSpeed, 0, 2000),
            A("風変動", 45, "%", effect => effect.WindVariation, 0, 100),
            F("変動速度", "1.2倍", effect => effect.WindRate = 1.2),
            F("反応ばらつき", "50%", effect => effect.WindResponseVariation = 50),
            F("漂い", "有効", effect => effect.DriftEnabled = true),
            A("漂い幅", 20, "px", effect => effect.DriftWidth, 0, 200),
            F("漂い速度", "1.1倍", effect => effect.DriftRate = 1.1),
            F("不規則さ", "60%", effect => effect.DriftIrregularity = 60),
            F("回転速度", "25°/秒", effect => effect.RotationSpeed = 25),
            F("初期角度ばらつき", "100%", effect => effect.InitialRotationVariation = 100),
            F("回転速度ばらつき", "60%", effect => effect.RotationSpeedVariation = 60)),

        Snow("blizzard", "吹雪", "小さな玉雪へ強い風と速い変動を加えます。", RainfallShape.SnowRound,
            F("輪郭柔らかさ", "0%", effect => effect.SnowSoftness = 0),
            A("量", 65, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 6, "px", effect => effect.ParticleSize, 1, 512),
            A("落下速度", 250, "px/秒", effect => effect.SnowSpeed, 0, 4000),
            A("不透明度", 80, "%", effect => effect.Opacity, 0, 100),
            A("速度ばらつき", 50, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 45, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 30, "%", effect => effect.OpacityVariation, 0, 100),
            F("動き", "基本降雪", effect => effect.SnowMotion = SnowMotionKind.Basic),
            F("風", "有効", effect => effect.WindEnabled = true),
            A("風向", 100, "°", effect => effect.WindAngle, double.MinValue, double.MaxValue),
            A("風強度", 800, "px/秒", effect => effect.WindSpeed, 0, 2000),
            A("風変動", 65, "%", effect => effect.WindVariation, 0, 100),
            F("変動速度", "2倍", effect => effect.WindRate = 2),
            F("反応ばらつき", "70%", effect => effect.WindResponseVariation = 70),
            F("漂い", "有効", effect => effect.DriftEnabled = true),
            A("漂い幅", 35, "px", effect => effect.DriftWidth, 0, 200),
            F("漂い速度", "2倍", effect => effect.DriftRate = 2),
            F("不規則さ", "85%", effect => effect.DriftIrregularity = 85),
            F("回転速度", "45°/秒", effect => effect.RotationSpeed = 45),
            F("初期角度ばらつき", "100%", effect => effect.InitialRotationVariation = 100),
            F("回転速度ばらつき", "75%", effect => effect.RotationSpeedVariation = 75)),

        Snow("dancing-crystal", "結晶が舞う雪", "少ない大きな結晶をゆっくり回しながら漂わせます。", RainfallShape.SnowCrystal,
            A("量", 8, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 60, "px", effect => effect.ParticleSize, 1, 512),
            A("落下速度", 60, "px/秒", effect => effect.SnowSpeed, 0, 4000),
            A("不透明度", 90, "%", effect => effect.Opacity, 0, 100),
            A("速度ばらつき", 25, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 45, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 15, "%", effect => effect.OpacityVariation, 0, 100),
            F("動き", "基本降雪", effect => effect.SnowMotion = SnowMotionKind.Basic),
            F("風", "無効", effect => effect.WindEnabled = false),
            F("漂い", "有効", effect => effect.DriftEnabled = true),
            A("漂い幅", 55, "px", effect => effect.DriftWidth, 0, 200),
            F("漂い速度", "0.5倍", effect => effect.DriftRate = 0.5),
            F("不規則さ", "55%", effect => effect.DriftIrregularity = 55),
            F("回転速度", "12°/秒", effect => effect.RotationSpeed = 12),
            F("初期角度ばらつき", "100%", effect => effect.InitialRotationVariation = 100),
            F("回転速度ばらつき", "70%", effect => effect.RotationSpeedVariation = 70)),

        Snow("vortex", "渦を巻く雪", "玉雪を指定中心の周囲で回し、弱い漂いを重ねます。", RainfallShape.SnowRound,
            A("量", 25, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 12, "px", effect => effect.ParticleSize, 1, 512),
            A("落下速度", 40, "px/秒", effect => effect.SnowSpeed, 0, 4000),
            A("不透明度", 80, "%", effect => effect.Opacity, 0, 100),
            A("速度ばらつき", 35, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 50, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 20, "%", effect => effect.OpacityVariation, 0, 100),
            F("輪郭柔らかさ", "65%", effect => effect.SnowSoftness = 65),
            F("動き", "渦", effect => effect.SnowMotion = SnowMotionKind.Vortex),
            F("風", "有効", effect => effect.WindEnabled = true),
            A("風向", 90, "°", effect => effect.WindAngle, double.MinValue, double.MaxValue),
            A("風強度", 80, "px/秒", effect => effect.WindSpeed, 0, 2000),
            A("風変動", 30, "%", effect => effect.WindVariation, 0, 100),
            F("変動速度", "0.8倍", effect => effect.WindRate = 0.8),
            F("反応ばらつき", "35%", effect => effect.WindResponseVariation = 35),
            F("漂い", "有効", effect => effect.DriftEnabled = true),
            A("漂い幅", 20, "px", effect => effect.DriftWidth, 0, 200),
            F("漂い速度", "0.8倍", effect => effect.DriftRate = 0.8),
            F("不規則さ", "55%", effect => effect.DriftIrregularity = 55),
            A("中心X", 0, "px", effect => effect.LocalCenterX, -32768, 32768),
            A("中心Y", 0, "px", effect => effect.LocalCenterY, -32768, 32768),
            A("影響半径", 400, "px", effect => effect.VortexRadius, 1, 8192),
            A("渦強度", 180, "px/秒", effect => effect.VortexSpeed, 0, 2000),
            F("時計回り", "有効", effect => effect.VortexClockwise = true),
            F("回転速度", "30°/秒", effect => effect.RotationSpeed = 30),
            F("初期角度ばらつき", "100%", effect => effect.InitialRotationVariation = 100),
            F("回転速度ばらつき", "70%", effect => effect.RotationSpeedVariation = 70)),

        Snow("updraft", "下から吹き上がる雪", "ぼたん雪を指定範囲で下から吹き上げ、横風を加えます。", RainfallShape.SnowClump,
            A("量", 20, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 30, "px", effect => effect.ParticleSize, 1, 512),
            A("落下速度", 60, "px/秒", effect => effect.SnowSpeed, 0, 4000),
            A("不透明度", 80, "%", effect => effect.Opacity, 0, 100),
            A("速度ばらつき", 35, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 70, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 20, "%", effect => effect.OpacityVariation, 0, 100),
            F("輪郭柔らかさ", "45%", effect => effect.SnowSoftness = 45),
            F("動き", "巻き上がり", effect => effect.SnowMotion = SnowMotionKind.Updraft),
            F("風", "有効", effect => effect.WindEnabled = true),
            A("風向", 270, "°", effect => effect.WindAngle, double.MinValue, double.MaxValue),
            A("風強度", 100, "px/秒", effect => effect.WindSpeed, 0, 2000),
            A("風変動", 40, "%", effect => effect.WindVariation, 0, 100),
            F("変動速度", "1.1倍", effect => effect.WindRate = 1.1),
            F("反応ばらつき", "45%", effect => effect.WindResponseVariation = 45),
            F("漂い", "有効", effect => effect.DriftEnabled = true),
            A("漂い幅", 35, "px", effect => effect.DriftWidth, 0, 200),
            F("漂い速度", "1.2倍", effect => effect.DriftRate = 1.2),
            F("不規則さ", "70%", effect => effect.DriftIrregularity = 70),
            A("中心X", 0, "px", effect => effect.LocalCenterX, -32768, 32768),
            A("中心Y", 0, "px", effect => effect.LocalCenterY, -32768, 32768),
            A("範囲幅", 800, "px", effect => effect.UpdraftWidth, 1, 8192),
            A("範囲高さ", 400, "px", effect => effect.UpdraftHeight, 1, 8192),
            A("吹き上げ強度", 450, "px/秒", effect => effect.UpdraftSpeed, 0, 2000),
            F("回転速度", "24°/秒", effect => effect.RotationSpeed = 24),
            F("初期角度ばらつき", "100%", effect => effect.InitialRotationVariation = 100),
            F("回転速度ばらつき", "65%", effect => effect.RotationSpeedVariation = 65)),

        new WeatherPresetDefinition("wind-rain", WeatherKind.Rain, "風に流れる雨", "粒の太さに合わせて速度と風への反応を変え、横風で流します。", RainfallShape.Streak,
        [
            A("量", 30, "%", effect => effect.AmountAnimation, 0, 100),
            A("長さ", 70, "px", effect => effect.LengthAnimation, 1, 200),
            A("太さ", 2, "px", effect => effect.ThicknessAnimation, 0.1, 8),
            A("移動速度", 2500, "px/秒", effect => effect.SpeedAnimation, 0, 4000),
            A("不透明度", 40, "%", effect => effect.Opacity, 0, 100),
            A("方向", 8, "°", effect => effect.AngleAnimation, double.MinValue, double.MaxValue),
            A("速度ばらつき", 35, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 45, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 10, "%", effect => effect.OpacityVariation, 0, 100),
            F("大きさ・動き連動", "有効", effect => effect.RainSizeMotionLinked = true),
            F("風", "有効", effect => effect.WindEnabled = true),
            A("風向", 90, "°", effect => effect.WindAngle, double.MinValue, double.MaxValue),
            A("風強度", 500, "px/秒", effect => effect.WindSpeed, 0, 2000),
            A("風変動", 35, "%", effect => effect.WindVariation, 0, 100),
            F("変動速度", "1倍", effect => effect.WindRate = 1),
            F("反応ばらつき", "35%", effect => effect.WindResponseVariation = 35),
        ]),

        new WeatherPresetDefinition("thin-bubble", WeatherKind.Bubble, "輪郭の薄い水泡", "二重輪郭を抑え、反射光を残した水泡にします。", RainfallShape.Bubble,
        [
            A("量", 4, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 80, "px", effect => effect.ParticleSize, 1, 512),
            A("浮上速度", 100, "px/秒", effect => effect.BubbleSpeed, 0, 4000),
            A("不透明度", 60, "%", effect => effect.Opacity, 0, 100),
            A("方向", 180, "°", effect => effect.AngleAnimation, double.MinValue, double.MaxValue),
            A("速度ばらつき", 20, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 40, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 0, "%", effect => effect.OpacityVariation, 0, 100),
            A("輪郭濃度", 25, "%", effect => effect.OutlineOpacity, 0, 100),
            F("揺らめき", "有効", effect => effect.SwayEnabled = true),
            F("揺れ幅", "18px", effect => effect.SwayAmplitude = 18),
            F("揺れ周期", "2.4秒", effect => effect.SwayPeriod = 2.4),
            A("回転角度", 0, "°", effect => effect.RotationAngle, double.MinValue, double.MaxValue),
            F("回転速度", "0°/秒", effect => effect.RotationSpeed = 0),
        ]),

        new WeatherPresetDefinition("reflective-bubble", WeatherKind.Bubble, "背景を映す水泡", "水泡Bの内部へ入力映像を薄く映し込みます。", RainfallShape.LensBubble,
        [
            A("量", 3, "%", effect => effect.AmountAnimation, 0, 100),
            A("大きさ", 100, "px", effect => effect.ParticleSize, 1, 512),
            A("浮上速度", 90, "px/秒", effect => effect.BubbleSpeed, 0, 4000),
            A("不透明度", 65, "%", effect => effect.Opacity, 0, 100),
            A("方向", 180, "°", effect => effect.AngleAnimation, double.MinValue, double.MaxValue),
            A("速度ばらつき", 20, "%", effect => effect.SpeedVariation, 0, 100),
            A("大きさばらつき", 35, "%", effect => effect.ThicknessVariation, 0, 100),
            A("不透明度ばらつき", 0, "%", effect => effect.OpacityVariation, 0, 100),
            A("輪郭濃度", 65, "%", effect => effect.OutlineOpacity, 0, 100),
            A("映り込み", 45, "%", effect => effect.LensReflection, 0, 100),
            F("揺らめき", "有効", effect => effect.SwayEnabled = true),
            F("揺れ幅", "16px", effect => effect.SwayAmplitude = 16),
            F("揺れ周期", "2.8秒", effect => effect.SwayPeriod = 2.8),
            A("回転角度", 0, "°", effect => effect.RotationAngle, double.MinValue, double.MaxValue),
            F("回転速度", "0°/秒", effect => effect.RotationSpeed = 0),
        ]),
    ];

    static WeatherPresetCatalog()
    {
        for (var index = 0; index < presets.Length; index++)
        {
            var preset = presets[index];
            var operations = preset.Operations.ToList();
            if (preset.Key != "wind-rain")
                operations.Add(F("大きさ・動き連動", "有効", effect => effect.RainSizeMotionLinked = true));
            operations.Add(F("進行方向追従", "無効", effect => effect.FollowDirection = false));
            operations.Add(F("上下維持", "無効", effect => effect.KeepUpright = false));
            operations.Add(F("補正時左右反転", "無効", effect => effect.MirrorWhenUpright = false));
            var blur = preset.Key is "wind-rain" or "wind-snow" or "blizzard";
            operations.Add(F("モーションブラー", blur ? "有効" : "無効", effect => effect.MotionBlurEnabled = blur));
            if (blur)
            {
                operations.Add(F("ブラー方式", "後方", effect => effect.MotionBlurMode = MotionBlurMode.Trailing));
                operations.Add(A("ブラー強さ", 25, "%", effect => effect.MotionBlurStrength, 0, 100));
            }
            if (preset.Kind == WeatherKind.Bubble)
            {
                operations.Add(F("初期角度ばらつき", "0%", effect => effect.InitialRotationVariation = 0));
                operations.Add(F("回転速度ばらつき", "0%", effect => effect.RotationSpeedVariation = 0));
            }
            presets[index] = preset with { Operations = operations.ToArray() };
        }
    }

    public static IReadOnlyList<WeatherPresetDefinition> All => presets;
    public static IReadOnlyList<WeatherPresetDefinition> For(WeatherKind kind) =>
        presets.Where(preset => preset.Kind == kind).ToArray();

    public static string KindName(WeatherKind kind) => kind switch
    {
        WeatherKind.Rain => "雨",
        WeatherKind.Snow => "雪",
        WeatherKind.Bubble => "泡",
        WeatherKind.CustomPng => "カスタムPNG",
        _ => "不明な種類",
    };

    public static string ShapeName(RainfallShape shape) => shape switch
    {
        RainfallShape.Streak => "線",
        RainfallShape.Circle => "丸",
        RainfallShape.CartoonDrop => "水滴",
        RainfallShape.Png => "PNG",
        RainfallShape.Bubble => "水泡A",
        RainfallShape.LensBubble => "水泡B",
        RainfallShape.SnowRound => "玉雪",
        RainfallShape.SnowClump => "ぼたん雪",
        RainfallShape.SnowCrystal => "雪の結晶",
        _ => "不明な形状",
    };

    private static WeatherPresetDefinition Snow(string key, string name, string purpose, RainfallShape shape,
        params WeatherPresetOperation[] operations) => new(key, WeatherKind.Snow, name, purpose, shape, operations);

    private static WeatherPresetOperation A(string name, double value, string unit,
        Func<RainfallEffect, Animation> select, double minimum, double maximum)
    {
        var shown = value.ToString("0.##", CultureInfo.InvariantCulture);
        return new($"{name}: {shown} {unit}",
            effect => select(effect).CopyFrom(new Animation(value, minimum, maximum)));
    }

    private static WeatherPresetOperation F(string name, string value, Action<RainfallEffect> apply) =>
        new($"{name}: {value}", apply);
}
