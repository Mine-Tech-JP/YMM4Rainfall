// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Media;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM4Rainfall;

/// <summary>種類ごとの設定を保持し、選択中の粒を入力映像へ重ねます。</summary>
[VideoEffect("雨雪と浮上", ["描画"], ["雨", "降雨", "降雪", "雪", "Rainfall", "水泡", "PNG"])]
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
internal sealed class RainfallEffect : VideoEffectBase, IJsonOnDeserializing, IJsonOnDeserialized
{
    private WeatherKind kind;
    private int schemaVersion = 2;
    private bool updatingColor;
    private readonly List<AnimationValue> observedColorValues = [];
    private readonly List<Animation> observedColorAnimations = [];

    public RainfallEffect() : this(useNewItemDefaults: true) { }

    /// <summary>従来条件の回帰検証では新規追加専用の初期値を適用しません。</summary>
    internal RainfallEffect(bool useNewItemDefaults)
    {
        if (useNewItemDefaults)
        foreach (var weatherKind in Enum.GetValues<WeatherKind>())
        {
            var bank = weatherKind switch
            {
                WeatherKind.Snow => SnowAppearances,
                WeatherKind.Bubble => BubbleAppearances,
                WeatherKind.CustomPng => PngAppearances,
                _ => RainAppearances,
            };
            foreach (var settings in bank.Variants)
                RainfallNewItemDefaults.Apply(settings, weatherKind);
        }
        if (useNewItemDefaults)
        {
            SnowAppearances.SelectedShape = RainfallShape.SnowClump;
            PngAppearances.Current.PngReference = new(RainfallBuiltInImage.PigId, 1);
        }
        ObserveBanks();
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Kind)) NotifySelection();
        };
        ObserveColorValues();
    }

    public override string Label => "雨雪と浮上";
    [Browsable(false)]
    public int SchemaVersion { get => schemaVersion; set => Set(ref schemaVersion, value); }
    [Display(GroupName = "種類", Name = "種類"), EnumComboBox]
    public WeatherKind Kind { get => kind; set => Set(ref kind, value); }

    [Browsable(false)] public RainfallAppearanceBank RainAppearances { get; } = new(WeatherKind.Rain);
    [Browsable(false)] public RainfallAppearanceBank SnowAppearances { get; } = new(WeatherKind.Snow);
    [Browsable(false)] public RainfallAppearanceBank BubbleAppearances { get; } = new(WeatherKind.Bubble);
    [Browsable(false)] public RainfallAppearanceBank PngAppearances { get; } = new(WeatherKind.CustomPng);
    // 形式1の読込だけに使い、現在の保存では見た目別の設定を出力します。
    private bool loading;
    private RainfallSettings? legacyRain, legacySnow, legacyBubble, legacyPng;
    [Browsable(false)] public RainfallSettings RainSettings { get => loading ? legacyRain ??= new(WeatherKind.Rain) : RainAppearances.Current; set => legacyRain = value; }
    public bool ShouldSerializeRainSettings() => false;
    [Browsable(false)] public RainfallSettings SnowSettings { get => loading ? legacySnow ??= new(WeatherKind.Snow) : SnowAppearances.Current; set => legacySnow = value; }
    public bool ShouldSerializeSnowSettings() => false;
    [Browsable(false)] public RainfallSettings BubbleSettings { get => loading ? legacyBubble ??= new(WeatherKind.Bubble) : BubbleAppearances.Current; set => legacyBubble = value; }
    public bool ShouldSerializeBubbleSettings() => false;
    [Browsable(false)] public RainfallSettings PngSettings { get => loading ? legacyPng ??= new(WeatherKind.CustomPng) : PngAppearances.Current; set => legacyPng = value; }
    public bool ShouldSerializePngSettings() => false;
    private RainfallAppearanceBank[] AppearanceBanks => [RainAppearances, SnowAppearances, BubbleAppearances, PngAppearances];
    private RainfallSettings[] Banks => AppearanceBanks.SelectMany(bank => bank.Variants).ToArray();
    private RainfallAppearanceBank CurrentBank => Kind switch
    {
        WeatherKind.Snow => SnowAppearances, WeatherKind.Bubble => BubbleAppearances,
        WeatherKind.CustomPng => PngAppearances, _ => RainAppearances,
    };
    private RainfallSettings Current => CurrentBank.Current;
    private readonly List<INotifyPropertyChanged> observedBanks = [];
    private void ObserveBanks()
    {
        foreach (var bank in observedBanks) bank.PropertyChanged -= BankChanged;
        observedBanks.Clear();
        foreach (var bank in AppearanceBanks.Cast<INotifyPropertyChanged>().Concat(Banks))
        {
            bank.PropertyChanged += BankChanged;
            observedBanks.Add(bank);
        }
    }
    private void BankChanged(object? sender, PropertyChangedEventArgs args)
    {
        // 描画スレッドから届く画像状態は、設定UIの再構築を伴わず通知します。
        if (args.PropertyName == nameof(RainfallSettings.PngStatus))
        {
            if (ReferenceEquals(sender, Current)) OnPropertyChanged(nameof(PngStatus));
            return;
        }
        NotifySelection();
    }

    [Display(GroupName = "見た目", Name = "見た目"), EnumComboBox, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsRain), true)]
    public RainAppearance RainAppearance { get => (RainAppearance)Shape; set => Shape = (RainfallShape)value; }
    public bool ShouldSerializeRainAppearance() => false;
    [Display(GroupName = "見た目", Name = "見た目"), EnumComboBox, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsSnow), true)]
    public SnowAppearance SnowAppearance { get => (SnowAppearance)Shape; set => Shape = (RainfallShape)value; }
    public bool ShouldSerializeSnowAppearance() => false;
    [Display(GroupName = "見た目", Name = "結晶形状", Description = "結晶を1形状にそろえるか、6形状を粒ごとに混ぜて描きます。"), EnumComboBox, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsCrystalSnow), true)]
    public SnowCrystalStyle CrystalStyle { get => Current.CrystalStyle; set => Current.CrystalStyle = value; }
    public bool ShouldSerializeCrystalStyle() => false;
    [Display(GroupName = "見た目", Name = "見た目"), EnumComboBox, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsBubble), true)]
    public BubbleAppearance BubbleAppearance { get => (BubbleAppearance)Shape; set => Shape = (RainfallShape)value; }
    public bool ShouldSerializeBubbleAppearance() => false;
    [Browsable(false), JsonIgnore]
    public RainfallShape Shape
    {
        get => CurrentBank.SelectedShape;
        set
        {
            if (value is RainfallShape.Streak or RainfallShape.Circle or RainfallShape.CartoonDrop) Kind = WeatherKind.Rain;
            else if (value is RainfallShape.Bubble or RainfallShape.LensBubble) Kind = WeatherKind.Bubble;
            else if (value is RainfallShape.SnowRound or RainfallShape.SnowClump or RainfallShape.SnowCrystal) Kind = WeatherKind.Snow;
            else if (value == RainfallShape.Png && Kind != WeatherKind.Snow) Kind = WeatherKind.CustomPng;
            CurrentBank.SelectedShape = value;
        }
    }
    public bool ShouldSerializeShape() => false;

    [Display(GroupName = "基本設定", Name = "落下速度"), WeatherAnimationSlider("F0", "px/秒", 0, 4000), JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsSnow), true)]
    public Animation SnowSpeed => Current.SpeedAnimation;
    public bool ShouldSerializeSnowSpeed() => false;
    [Display(GroupName = "基本設定", Name = "浮上速度"), WeatherAnimationSlider("F0", "px/秒", 0, 4000), JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsBubble), true)]
    public Animation BubbleSpeed => Current.SpeedAnimation;
    public bool ShouldSerializeBubbleSpeed() => false;
    [Display(GroupName = "動き", Name = "動き"), EnumComboBox, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsSnow), true)]
    public SnowMotionKind SnowMotion { get => Current.SnowMotion; set => Current.SnowMotion = value; }
    public bool ShouldSerializeSnowMotion() => false;

    [Display(GroupName = "基本設定", Name = "量")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation AmountAnimation => Current.AmountAnimation;
    public bool ShouldSerializeAmountAnimation() => false;

    [Display(GroupName = "基本設定", Name = "移動速度")]
    [ShowPropertyEditorWhen(nameof(IsMoveSpeedVisible), true)]
    [WeatherAnimationSlider("F0", "px/秒", 0, 4000), JsonIgnore]
    public Animation SpeedAnimation => Current.SpeedAnimation;
    public bool ShouldSerializeSpeedAnimation() => false;

    [Display(GroupName = "動き", Name = "方向")]
    [ShowPropertyEditorWhen(nameof(IsDirectionVisible), true)]
    [WeatherAnimationSlider("F1", "°", -360, 360), JsonIgnore]
    public Animation AngleAnimation => Current.AngleAnimation;
    public bool ShouldSerializeAngleAnimation() => false;


    [Display(GroupName = "基本設定", Name = "長さ")]
    [ShowPropertyEditorWhen(nameof(IsStreak), true)]
    [WeatherAnimationSlider("F1", "px", 1, 200), JsonIgnore]
    public Animation LengthAnimation => Current.LengthAnimation;
    public bool ShouldSerializeLengthAnimation() => false;

    [Display(GroupName = "基本設定", Name = "太さ")]
    [ShowPropertyEditorWhen(nameof(IsStreak), true)]
    [WeatherAnimationSlider("F2", "px", 0.1, 8), JsonIgnore]
    public Animation ThicknessAnimation => Current.ThicknessAnimation;
    public bool ShouldSerializeThicknessAnimation() => false;

    [Display(GroupName = "基本設定", Name = "大きさ")]
    [ShowPropertyEditorWhen(nameof(IsBuiltinParticle), true)]
    [WeatherAnimationSlider("F1", "px", 1, 512), JsonIgnore]
    public Animation ParticleSize => Current.ParticleSize;
    public bool ShouldSerializeParticleSize() => false;

    [Display(GroupName = "基本設定", Name = "倍率", Description = "100%でPNGの元の縦横サイズになります。透明な余白も含みます。")]
    [ShowPropertyEditorWhen(nameof(IsPng), true)]
    [WeatherAnimationSlider("F1", "%", 1, 1000), JsonIgnore]
    public Animation PngScale => Current.PngScale;
    public bool ShouldSerializePngScale() => false;
    [Browsable(false), JsonIgnore]
    public bool IsBuiltinParticle => IsParticle && !IsPng;
    public bool ShouldSerializeIsBuiltinParticle() => false;

    [Display(GroupName = "基本設定", Name = "不透明度")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation Opacity => Current.Opacity;
    public bool ShouldSerializeOpacity() => false;

    [Display(GroupName = "ばらつき", Name = "速度ばらつき")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation SpeedVariation => Current.SpeedVariation;
    public bool ShouldSerializeSpeedVariation() => false;

    [Display(GroupName = "ばらつき", Name = "大きさばらつき")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation ThicknessVariation => Current.ThicknessVariation;
    public bool ShouldSerializeThicknessVariation() => false;

    [Display(GroupName = "ばらつき", Name = "不透明度ばらつき")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation OpacityVariation => Current.OpacityVariation;
    public bool ShouldSerializeOpacityVariation() => false;

    [Display(GroupName = "ばらつき", Name = "大きさ・動き連動", Description = "大きい粒ほど基本移動を速くします。差は大きさばらつきと速度ばらつきで調整します。全体の大きさやPNG倍率だけでは速度は変わりません。雨のみ、小粒ほど風に流されやすくなります。"), ToggleSlider, JsonIgnore]
    public bool RainSizeMotionLinked { get => Current.RainSizeMotionLinked; set => Current.RainSizeMotionLinked = value; }
    public bool ShouldSerializeRainSizeMotionLinked() => false;

    [Display(GroupName = "モーションブラー", Name = "モーションブラー", Description = "粒の移動方向に沿ってぼかします。風・漂い・揺らめき等も含み、画像の向きの方向追従とは独立します。"), ToggleSlider, JsonIgnore]
    public bool MotionBlurEnabled { get => Current.MotionBlurEnabled; set => Current.MotionBlurEnabled = value; }
    public bool ShouldSerializeMotionBlurEnabled() => false;

    [Display(GroupName = "モーションブラー", Name = "方式", Description = "前後は移動方向の両側をぼかします。後方は現在位置の像を残し、進行方向の後ろへ薄い尾を伸ばします。"), EnumComboBox, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(MotionBlurEnabled), true)]
    public MotionBlurMode MotionBlurMode { get => Current.MotionBlurMode; set => Current.MotionBlurMode = value; }
    public bool ShouldSerializeMotionBlurMode() => false;

    [Display(GroupName = "モーションブラー", Name = "強さ", Description = "速い粒ほど長くぼかします。0%ではぼかしません。")]
    [ShowPropertyEditorWhen(nameof(MotionBlurEnabled), true)]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation MotionBlurStrength => Current.MotionBlurStrength;
    public bool ShouldSerializeMotionBlurStrength() => false;

    [Display(GroupName = "回転", Name = "回転角度")]
    [ShowPropertyEditorWhen(nameof(IsRotationVisible), true)]
    [WeatherAnimationSlider("F1", "°", -360, 360), JsonIgnore]
    public Animation RotationAngle => Current.RotationAngle;
    public bool ShouldSerializeRotationAngle() => false;

    [Display(GroupName = "見た目", Name = "映り込み")]
    [ShowPropertyEditorWhen(nameof(IsLensBubble), true)]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation LensReflection => Current.LensReflection;
    public bool ShouldSerializeLensReflection() => false;

    [Display(GroupName = "見た目", Name = "輪郭濃度")]
    [ShowPropertyEditorWhen(nameof(IsBubble), true)]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation OutlineOpacity => Current.OutlineOpacity;
    public bool ShouldSerializeOutlineOpacity() => false;

    [Display(GroupName = "色", Name = "赤強度")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation Red => Current.Red;
    public bool ShouldSerializeRed() => false;

    [Display(GroupName = "色", Name = "緑強度")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation Green => Current.Green;
    public bool ShouldSerializeGreen() => false;

    [Display(GroupName = "色", Name = "青強度")]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation Blue => Current.Blue;
    public bool ShouldSerializeBlue() => false;

    [Display(GroupName = "風", Name = "風")]
    [ShowPropertyEditorWhen(nameof(IsWindSupported), true)]
    [ToggleSlider]
    [JsonIgnore]
    public bool WindEnabled { get => Current.WindEnabled; set => Current.WindEnabled = value; }
    public bool ShouldSerializeWindEnabled() => false;

    [Display(GroupName = "風", Name = "風向")]
    [ShowPropertyEditorWhen(nameof(IsWindSettingsVisible), true)]
    [WeatherAnimationSlider("F1", "°", -360, 360), JsonIgnore]
    public Animation WindAngle => Current.WindAngle;
    public bool ShouldSerializeWindAngle() => false;

    [Display(GroupName = "風", Name = "風強度")]
    [ShowPropertyEditorWhen(nameof(IsWindSettingsVisible), true)]
    [WeatherAnimationSlider("F0", "px/秒", 0, 2000), JsonIgnore]
    public Animation WindSpeed => Current.WindSpeed;
    public bool ShouldSerializeWindSpeed() => false;

    [Display(GroupName = "風", Name = "風変動")]
    [ShowPropertyEditorWhen(nameof(IsWindSettingsVisible), true)]
    [WeatherAnimationSlider("F1", "%", 0, 100), JsonIgnore]
    public Animation WindVariation => Current.WindVariation;
    public bool ShouldSerializeWindVariation() => false;

    [Display(GroupName = "漂い", Name = "漂い")]
    [ShowPropertyEditorWhen(nameof(IsSnow), true)]
    [ToggleSlider]
    [JsonIgnore]
    public bool DriftEnabled { get => Current.DriftEnabled; set => Current.DriftEnabled = value; }
    public bool ShouldSerializeDriftEnabled() => false;

    [Display(GroupName = "漂い", Name = "漂い幅")]
    [ShowPropertyEditorWhen(nameof(IsDriftSettingsVisible), true)]
    [WeatherAnimationSlider("F1", "px", 0, 200), JsonIgnore]
    public Animation DriftWidth => Current.DriftWidth;
    public bool ShouldSerializeDriftWidth() => false;

    [Display(GroupName = "局所的な動き", Name = "中心X")]
    [ShowPropertyEditorWhen(nameof(IsLocalVisible), true)]
    [WeatherAnimationSlider("F1", "px", -32768, 32768), JsonIgnore]
    public Animation LocalCenterX => Current.LocalCenterX;
    public bool ShouldSerializeLocalCenterX() => false;

    [Display(GroupName = "局所的な動き", Name = "中心Y")]
    [ShowPropertyEditorWhen(nameof(IsLocalVisible), true)]
    [WeatherAnimationSlider("F1", "px", -32768, 32768), JsonIgnore]
    public Animation LocalCenterY => Current.LocalCenterY;
    public bool ShouldSerializeLocalCenterY() => false;

    [Display(GroupName = "局所的な動き", Name = "影響半径")]
    [ShowPropertyEditorWhen(nameof(IsVortex), true)]
    [WeatherAnimationSlider("F1", "px", 1, 8192), JsonIgnore]
    public Animation VortexRadius => Current.VortexRadius;
    public bool ShouldSerializeVortexRadius() => false;

    [Display(GroupName = "局所的な動き", Name = "渦強度")]
    [ShowPropertyEditorWhen(nameof(IsVortex), true)]
    [WeatherAnimationSlider("F0", "px/秒", 0, 2000), JsonIgnore]
    public Animation VortexSpeed => Current.VortexSpeed;
    public bool ShouldSerializeVortexSpeed() => false;

    [Display(GroupName = "局所的な動き", Name = "範囲幅")]
    [ShowPropertyEditorWhen(nameof(IsUpdraft), true)]
    [WeatherAnimationSlider("F1", "px", 1, 8192), JsonIgnore]
    public Animation UpdraftWidth => Current.UpdraftWidth;
    public bool ShouldSerializeUpdraftWidth() => false;

    [Display(GroupName = "局所的な動き", Name = "範囲高さ")]
    [ShowPropertyEditorWhen(nameof(IsUpdraft), true)]
    [WeatherAnimationSlider("F1", "px", 1, 8192), JsonIgnore]
    public Animation UpdraftHeight => Current.UpdraftHeight;
    public bool ShouldSerializeUpdraftHeight() => false;

    [Display(GroupName = "局所的な動き", Name = "吹き上げ強度", Description = "中央で上昇し、上側で左右へ広がる流れを加えます。中央の上向き速度を指定します。")]
    [ShowPropertyEditorWhen(nameof(IsUpdraft), true)]
    [WeatherAnimationSlider("F0", "px/秒", 0, 2000), JsonIgnore]
    public Animation UpdraftSpeed => Current.UpdraftSpeed;
    public bool ShouldSerializeUpdraftSpeed() => false;


    [Display(GroupName = "風", Name = "変動速度")]
    [ShowPropertyEditorWhen(nameof(IsWindDetailsVisible), true)]
    [TextBoxSlider("F1", "倍", 0, 10), Range(0, 10)]
    [JsonIgnore]
    public double WindRate { get => Current.WindRate; set => Current.WindRate = value; }
    public bool ShouldSerializeWindRate() => false;

    [Display(GroupName = "風", Name = "反応ばらつき")]
    [ShowPropertyEditorWhen(nameof(IsWindDetailsVisible), true)]
    [TextBoxSlider("F1", "%", 0, 100), Range(0, 100)]
    [JsonIgnore]
    public double WindResponseVariation { get => Current.WindResponseVariation; set => Current.WindResponseVariation = value; }
    public bool ShouldSerializeWindResponseVariation() => false;


    [Display(GroupName = "漂い", Name = "漂い速度")]
    [ShowPropertyEditorWhen(nameof(IsDriftSettingsVisible), true)]
    [TextBoxSlider("F1", "倍", 0, 10), Range(0, 10)]
    [JsonIgnore]
    public double DriftRate { get => Current.DriftRate; set => Current.DriftRate = value; }
    public bool ShouldSerializeDriftRate() => false;

    [Display(GroupName = "漂い", Name = "不規則さ")]
    [ShowPropertyEditorWhen(nameof(IsDriftDetailsVisible), true)]
    [TextBoxSlider("F1", "%", 0, 100), Range(0, 100)]
    [JsonIgnore]
    public double DriftIrregularity { get => Current.DriftIrregularity; set => Current.DriftIrregularity = value; }
    public bool ShouldSerializeDriftIrregularity() => false;

    [Display(GroupName = "局所的な動き", Name = "時計回り")]
    [ShowPropertyEditorWhen(nameof(IsVortex), true)]
    [ToggleSlider]
    [JsonIgnore]
    public bool VortexClockwise { get => Current.VortexClockwise; set => Current.VortexClockwise = value; }
    public bool ShouldSerializeVortexClockwise() => false;

    [Display(GroupName = "見た目", Name = "輪郭柔らかさ")]
    [ShowPropertyEditorWhen(nameof(IsSoftSnow), true)]
    [TextBoxSlider("F1", "%", 0, 100), Range(0, 100)]
    [JsonIgnore]
    public double SnowSoftness { get => Current.SnowSoftness; set => Current.SnowSoftness = value; }
    public bool ShouldSerializeSnowSoftness() => false;

    [Display(GroupName = "回転", Name = "回転速度")]
    [ShowPropertyEditorWhen(nameof(IsRotationVisible), true)]
    [TextBoxSlider("F1", "°/秒", -36000, 36000), Range(-36000, 36000)]
    [JsonIgnore]
    public double RotationSpeed { get => Current.RotationSpeed; set => Current.RotationSpeed = value; }
    public bool ShouldSerializeRotationSpeed() => false;

    [Display(GroupName = "回転", Name = "初期角度ばらつき")]
    [ShowPropertyEditorWhen(nameof(IsRotationVisible), true)]
    [TextBoxSlider("F1", "%", 0, 100), Range(0, 100)]
    [JsonIgnore]
    public double InitialRotationVariation { get => Current.InitialRotationVariation; set => Current.InitialRotationVariation = value; }
    public bool ShouldSerializeInitialRotationVariation() => false;

    [Display(GroupName = "回転", Name = "回転速度ばらつき")]
    [ShowPropertyEditorWhen(nameof(IsRotationVisible), true)]
    [TextBoxSlider("F1", "%", 0, 100), Range(0, 100)]
    [JsonIgnore]
    public double RotationSpeedVariation { get => Current.RotationSpeedVariation; set => Current.RotationSpeedVariation = value; }
    public bool ShouldSerializeRotationSpeedVariation() => false;

    [Display(GroupName = "回転", Name = "進行方向追従")]
    [ShowPropertyEditorWhen(nameof(IsRotationVisible), true)]
    [ToggleSlider]
    [JsonIgnore]
    public bool FollowDirection { get => Current.FollowDirection; set => Current.FollowDirection = value; }
    public bool ShouldSerializeFollowDirection() => false;

    [Display(GroupName = "回転", Name = "上下維持")]
    [ShowPropertyEditorWhen(nameof(IsRotationVisible), true)]
    [ToggleSlider]
    [JsonIgnore]
    public bool KeepUpright { get => Current.KeepUpright; set => Current.KeepUpright = value; }
    public bool ShouldSerializeKeepUpright() => false;

    [Display(GroupName = "回転", Name = "補正時左右反転")]
    [ShowPropertyEditorWhen(nameof(IsUprightMirrorVisible), true)]
    [ToggleSlider]
    [JsonIgnore]
    public bool MirrorWhenUpright { get => Current.MirrorWhenUpright; set => Current.MirrorWhenUpright = value; }
    public bool ShouldSerializeMirrorWhenUpright() => false;

    [Display(GroupName = "揺らめき", Name = "揺らめき")]
    [ShowPropertyEditorWhen(nameof(IsSwaySupported), true)]
    [ToggleSlider]
    [JsonIgnore]
    public bool SwayEnabled { get => Current.SwayEnabled; set => Current.SwayEnabled = value; }
    public bool ShouldSerializeSwayEnabled() => false;

    [Display(GroupName = "揺らめき", Name = "揺れ幅")]
    [ShowPropertyEditorWhen(nameof(IsSwaySettingsVisible), true)]
    [TextBoxSlider("F1", "px", 0, 200), Range(0, 200)]
    [JsonIgnore]
    public double SwayAmplitude { get => Current.SwayAmplitude; set => Current.SwayAmplitude = value; }
    public bool ShouldSerializeSwayAmplitude() => false;

    [Display(GroupName = "揺らめき", Name = "揺れ周期")]
    [ShowPropertyEditorWhen(nameof(IsSwaySettingsVisible), true)]
    [TextBoxSlider("F1", "秒", 0.1, 60), Range(0.1, 60)]
    [JsonIgnore]
    public double SwayPeriod { get => Current.SwayPeriod; set => Current.SwayPeriod = value; }
    public bool ShouldSerializeSwayPeriod() => false;

    [Browsable(false)]
    [JsonIgnore]
    public Guid PngImageId { get => Current.PngImageId; set => Current.PngImageId = value; }
    public bool ShouldSerializePngImageId() => false;

    [Browsable(false)]
    [JsonIgnore]
    public long PngImageRevision { get => Current.PngImageRevision; set => Current.PngImageRevision = value; }
    public bool ShouldSerializePngImageRevision() => false;

    [Browsable(false)]
    [JsonIgnore]
    public byte ColorAlpha { get => Current.ColorAlpha; set => Current.ColorAlpha = value; }
    public bool ShouldSerializeColorAlpha() => false;

    [Display(GroupName = "出現開始", Name = "出現開始指定")]
    [ToggleSlider]
    [JsonIgnore]
    public bool OnsetEnabled { get => Current.OnsetEnabled; set => Current.OnsetEnabled = value; }
    public bool ShouldSerializeOnsetEnabled() => false;

    [Display(GroupName = "出現開始", Name = "開始時間")]
    [ShowPropertyEditorWhen(nameof(IsOnsetVisible), true)]
    [TextBoxSlider("F1", "秒", 0, 36000), Range(0, 36000)]
    [JsonIgnore]
    public double StartSeconds { get => Current.StartSeconds; set => Current.StartSeconds = value; }
    public bool ShouldSerializeStartSeconds() => false;

    [Browsable(false)]
    [JsonIgnore]
    public double AppearanceSeconds { get => Current.AppearanceSeconds; set => Current.AppearanceSeconds = value; }
    public bool ShouldSerializeAppearanceSeconds() => false;

    [Browsable(false)]
    [JsonIgnore]
    public bool RampEnabled { get => Current.RampEnabled; set => Current.RampEnabled = value; }
    public bool ShouldSerializeRampEnabled() => false;

    [Browsable(false)]
    [JsonIgnore]
    public double RampSeconds { get => Current.RampSeconds; set => Current.RampSeconds = value; }
    public bool ShouldSerializeRampSeconds() => false;

    [Display(GroupName = "配置", Name = "乱数シード")]
    [TextBoxSlider("F0", "", 0, 99999), Range(0, 99999)]
    [JsonIgnore]
    public int Seed { get => Current.Seed; set => Current.Seed = value; }
    public bool ShouldSerializeSeed() => false;

    [Display(GroupName = "見た目", Name = "PNG画像", Description = "登録名を記入して「PNGを追加」を押してください"), RainfallImageEditor, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsPng), true)]
    public string PngSelection
    {
        get => $"{PngImageId:N}|{PngImageRevision}";
        set
        {
            var parts = (value ?? "").Split('|');
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out var id) || !long.TryParse(parts[1], out var revision) || revision < 0) return;
            Current.PngReference = new(id, revision);
        }
    }
    public bool ShouldSerializePngSelection() => false;
    [Display(GroupName = "見た目", Name = "画像解像度"), RainfallStatus, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsPng), true)]
    public string PngStatus => Current.PngStatus;
    public bool ShouldSerializePngStatus() => false;
    internal void SetPngStatus(string value)
    {
        Current.PngStatus = value;
    }

    [Display(GroupName = "色", Name = "開始色", Description = "RGBの先頭値を編集します。途中点と終点は保持します。"), ColorPicker, JsonIgnore]
    public Color Color
    {
        get => System.Windows.Media.Color.FromArgb(ColorAlpha, ToByte(Red.GetFirstValue()), ToByte(Green.GetFirstValue()), ToByte(Blue.GetFirstValue()));
        set
        {
            updatingColor = true;
            try
            {
                ColorAlpha = value.A;
                Red.SetFirstValue(value.R / 255.0 * 100);
                Green.SetFirstValue(value.G / 255.0 * 100);
                Blue.SetFirstValue(value.B / 255.0 * 100);
            }
            finally { updatingColor = false; ObserveColorValues(); OnPropertyChanged(nameof(Color)); }
        }
    }
    public bool ShouldSerializeColor() => false;

    [Display(GroupName = "出現開始", Name = "増え方"), EnumComboBox, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsOnsetVisible), true)]
    public RainfallOnsetPattern OnsetPattern
    {
        get => RampEnabled ? RainfallOnsetPattern.Sparse : RainfallOnsetPattern.Uniform;
        set { var seconds = RiseSeconds; RampEnabled = value == RainfallOnsetPattern.Sparse; RiseSeconds = seconds; }
    }
    public bool ShouldSerializeOnsetPattern() => false;
    [Display(GroupName = "出現開始", Name = "立ち上がり時間"), TextBoxSlider("F2", "秒", 0, 36000), Range(0, 36000), JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsOnsetVisible), true)]
    public double RiseSeconds
    {
        get => RampEnabled ? RampSeconds : AppearanceSeconds;
        set { AppearanceSeconds = value; RampSeconds = value; }
    }
    public bool ShouldSerializeRiseSeconds() => false;

    [Display(GroupName = "設定例", Name = "設定例（プリセット）"), WeatherPresetEditor, JsonIgnore]
    public RainfallEffect PresetTarget => this;
    public bool ShouldSerializePresetTarget() => false;

    [Display(GroupName = "種類", Name = "設定状態"), RainfallStatus, JsonIgnore]
    [ShowPropertyEditorWhen(nameof(IsUnsupported), true)]
    public string SettingsStatus => "この保存形式または種類・形状には対応していません。入力映像を表示します。";
    public bool ShouldSerializeSettingsStatus() => false;

    [Browsable(false), JsonIgnore]
    public bool IsRain => Kind == WeatherKind.Rain;
    public bool ShouldSerializeIsRain() => false;
    [Browsable(false), JsonIgnore]
    public bool IsSnow => Kind == WeatherKind.Snow;
    public bool ShouldSerializeIsSnow() => false;
    [Browsable(false), JsonIgnore]
    public bool IsBubble => Kind == WeatherKind.Bubble;
    public bool ShouldSerializeIsBubble() => false;
    [Browsable(false), JsonIgnore]
    public bool IsStreak => Shape == RainfallShape.Streak;
    public bool ShouldSerializeIsStreak() => false;
    [Browsable(false), JsonIgnore]
    public bool IsParticle => !IsStreak;
    public bool ShouldSerializeIsParticle() => false;
    [Browsable(false), JsonIgnore]
    public bool IsPng => Shape == RainfallShape.Png;
    public bool ShouldSerializeIsPng() => false;
    [Browsable(false), JsonIgnore]
    public bool IsLensBubble => IsBubble && Shape == RainfallShape.LensBubble;
    public bool ShouldSerializeIsLensBubble() => false;
    [Browsable(false), JsonIgnore]
    public bool IsSoftSnow => IsSnow && Shape is RainfallShape.SnowRound or RainfallShape.SnowClump;
    public bool ShouldSerializeIsSoftSnow() => false;
    [Browsable(false), JsonIgnore]
    public bool IsCrystalSnow => IsSnow && Shape == RainfallShape.SnowCrystal;
    public bool ShouldSerializeIsCrystalSnow() => false;
    [Browsable(false), JsonIgnore]
    public bool IsMoveSpeedVisible => Kind is WeatherKind.Rain or WeatherKind.CustomPng;
    public bool ShouldSerializeIsMoveSpeedVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsDirectionVisible => !IsSnow;
    public bool ShouldSerializeIsDirectionVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsWindSupported => IsRain || IsSnow;
    public bool ShouldSerializeIsWindSupported() => false;
    [Browsable(false), JsonIgnore]
    public bool IsWindSettingsVisible => IsWindSupported && WindEnabled;
    public bool ShouldSerializeIsWindSettingsVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsWindDetailsVisible => IsWindSettingsVisible;
    public bool ShouldSerializeIsWindDetailsVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsRainDetailsVisible => IsRain;
    public bool ShouldSerializeIsRainDetailsVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsDriftSettingsVisible => IsSnow && DriftEnabled;
    public bool ShouldSerializeIsDriftSettingsVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsDriftDetailsVisible => IsDriftSettingsVisible;
    public bool ShouldSerializeIsDriftDetailsVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsLocalVisible => IsSnow && SnowMotion != SnowMotionKind.Basic;
    public bool ShouldSerializeIsLocalVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsVortex => IsSnow && SnowMotion == SnowMotionKind.Vortex;
    public bool ShouldSerializeIsVortex() => false;
    [Browsable(false), JsonIgnore]
    public bool IsUpdraft => IsSnow && SnowMotion == SnowMotionKind.Updraft;
    public bool ShouldSerializeIsUpdraft() => false;
    [Browsable(false), JsonIgnore]
    public bool IsRotationVisible => IsParticle;
    public bool ShouldSerializeIsRotationVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsSwaySupported => IsBubble || Kind == WeatherKind.CustomPng;
    public bool ShouldSerializeIsSwaySupported() => false;
    [Browsable(false), JsonIgnore]
    public bool IsSwaySettingsVisible => IsSwaySupported && SwayEnabled;
    public bool ShouldSerializeIsSwaySettingsVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsUprightMirrorVisible => IsRotationVisible && KeepUpright;
    public bool ShouldSerializeIsUprightMirrorVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsOnsetVisible => OnsetEnabled;
    public bool ShouldSerializeIsOnsetVisible() => false;
    [Browsable(false), JsonIgnore]
    public bool IsUnsupported => !IsSupported;
    public bool ShouldSerializeIsUnsupported() => false;
    [Browsable(false), JsonIgnore]
    public bool IsSupported => RainAppearances.IsValid(WeatherKind.Rain) && SnowAppearances.IsValid(WeatherKind.Snow) &&
        BubbleAppearances.IsValid(WeatherKind.Bubble) && PngAppearances.IsValid(WeatherKind.CustomPng) && SchemaVersion == 2 && Enum.IsDefined(Kind) && Enum.IsDefined(SnowMotion) &&
        Enum.IsDefined(CrystalStyle) && Kind switch
    {
        WeatherKind.Rain => Shape is RainfallShape.Streak or RainfallShape.Circle or RainfallShape.CartoonDrop,
        WeatherKind.Snow => Shape is RainfallShape.SnowRound or RainfallShape.SnowClump or RainfallShape.SnowCrystal or RainfallShape.Png,
        WeatherKind.Bubble => Shape is RainfallShape.Bubble or RainfallShape.LensBubble,
        WeatherKind.CustomPng => Shape == RainfallShape.Png,
        _ => false,
    };
    public bool ShouldSerializeIsSupported() => false;

    private static byte ToByte(double percent) => (byte)Math.Round(Math.Clamp(double.IsFinite(percent) ? percent : 100, 0, 100) * 255 / 100);
    private void ObserveColorValues()
    {
        foreach (var value in observedColorValues) value.PropertyChanged -= ColorValueChanged;
        foreach (var animation in observedColorAnimations) animation.PropertyChanged -= ColorAnimationChanged;
        observedColorValues.Clear(); observedColorAnimations.Clear();
        foreach (var animation in new[] { Red, Green, Blue })
        {
            observedColorAnimations.Add(animation); animation.PropertyChanged += ColorAnimationChanged;
            foreach (var value in animation.Values)
            {
                observedColorValues.Add(value); value.PropertyChanged += ColorValueChanged;
            }
        }
    }
    private void ColorAnimationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (updatingColor) return;
        ObserveColorValues(); OnPropertyChanged(nameof(Color));
    }
    private void ColorValueChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!updatingColor) OnPropertyChanged(nameof(Color));
    }
    private static readonly string[] SelectionProperties = typeof(RainfallEffect).GetProperties()
        .Where(property => property.IsDefined(typeof(JsonIgnoreAttribute), false))
        .Select(property => property.Name).ToArray();
    private void NotifySelection()
    {
        ObserveColorValues();
        foreach (var name in SelectionProperties) OnPropertyChanged(name);
    }

    public void OnDeserializing()
    {
        schemaVersion = 0;
        loading = true;
        // 旧保存データの省略項目へ、新規追加専用の初期値を混ぜません。
        ResetForLoad(RainAppearances, WeatherKind.Rain);
        ResetForLoad(SnowAppearances, WeatherKind.Snow);
        ResetForLoad(BubbleAppearances, WeatherKind.Bubble);
        ResetForLoad(PngAppearances, WeatherKind.CustomPng);
    }

    private static void ResetForLoad(RainfallAppearanceBank target, WeatherKind kind)
    {
        var previousDefaults = new RainfallAppearanceBank(kind);
        target.Variants = previousDefaults.Variants;
        target.SelectedShape = previousDefaults.SelectedShape;
    }
    public void OnDeserialized()
    {
        if (schemaVersion == 1)
        {
            if (legacyRain is not null) RainAppearances.Import(legacyRain);
            if (legacySnow is not null) SnowAppearances.Import(legacySnow);
            if (legacyBubble is not null) BubbleAppearances.Import(legacyBubble);
            if (legacyPng is not null) PngAppearances.Import(legacyPng);
            schemaVersion = 2;
        }
        loading = false;
        legacyRain = legacySnow = legacyBubble = legacyPng = null;
        ObserveBanks(); NotifySelection();
    }
    [OnDeserializing] private void BeforeJsonLoad(StreamingContext context) => OnDeserializing();
    [OnDeserialized] private void AfterJsonLoad(StreamingContext context) => OnDeserialized();

    internal RainfallParameters GetParameters(long frame, long duration, int fps, int pngSourceSize = 0) => new(
        AmountAnimation.GetValue(frame, duration, fps), SpeedAnimation.GetValue(frame, duration, fps),
        IsSnow ? 0 : AngleAnimation.GetValue(frame, duration, fps), LengthAnimation.GetValue(frame, duration, fps),
        ThicknessAnimation.GetValue(frame, duration, fps), Opacity.GetValue(frame, duration, fps) * ColorAlpha / 255, Seed,
        ThicknessVariation.GetValue(frame, duration, fps), OnsetEnabled, StartSeconds, AppearanceSeconds,
        Red.GetValue(frame, duration, fps) / 100, Green.GetValue(frame, duration, fps) / 100, Blue.GetValue(frame, duration, fps) / 100,
        SpeedVariation.GetValue(frame, duration, fps), RampEnabled, RampSeconds, Shape, ParticleSize.GetValue(frame, duration, fps),
        OpacityVariation.GetValue(frame, duration, fps), RotationAngle.GetValue(frame, duration, fps), RotationSpeed,
        FollowDirection, KeepUpright, LensReflection.GetValue(frame, duration, fps), MirrorWhenUpright, SwayEnabled, SwayAmplitude, SwayPeriod)
    {
        Kind = Kind, SnowMotion = SnowMotion, WindEnabled = IsWindSupported && WindEnabled,
        PngScale = PngScale.GetValue(frame, duration, fps),
        PngSourceSize = pngSourceSize,
        PngMaximumScale = PngScale.AnimationType == AnimationType.なし
            ? RainfallParameters.NormalizePngScale(PngScale.GetFirstValue()) : RainfallParameters.MaximumPngScale,
        CrystalStyle = CrystalStyle,
        WindAngle = WindAngle.GetValue(frame, duration, fps),
        WindSpeed = WindSpeed.GetValue(frame, duration, fps),
        WindVariation = WindVariation.GetValue(frame, duration, fps),
        DriftWidth = DriftWidth.GetValue(frame, duration, fps),
        LocalCenterX = LocalCenterX.GetValue(frame, duration, fps),
        LocalCenterY = LocalCenterY.GetValue(frame, duration, fps),
        VortexRadius = VortexRadius.GetValue(frame, duration, fps),
        VortexSpeed = VortexSpeed.GetValue(frame, duration, fps),
        UpdraftWidth = UpdraftWidth.GetValue(frame, duration, fps),
        UpdraftHeight = UpdraftHeight.GetValue(frame, duration, fps),
        UpdraftSpeed = UpdraftSpeed.GetValue(frame, duration, fps),
        OutlineOpacity = OutlineOpacity.GetValue(frame, duration, fps),
        MotionBlurEnabled = MotionBlurEnabled,
        MotionBlurMode = MotionBlurMode,
        MotionBlurStrength = MotionBlurStrength.GetValue(frame, duration, fps),
        WindRate = WindRate,
        WindResponseVariation = WindResponseVariation,
        RainSizeMotionLinked = RainSizeMotionLinked,
        DriftRate = DriftRate,
        DriftIrregularity = DriftIrregularity,
        VortexClockwise = VortexClockwise,
        SnowSoftness = SnowSoftness,
        InitialRotationVariation = InitialRotationVariation,
        RotationSpeedVariation = RotationSpeedVariation,
        DriftEnabled = IsSnow && DriftEnabled,
    };

    internal RainfallTravel GetVelocity(long frame, long duration, int fps) =>
        RainfallTravel.Velocity(SpeedAnimation.GetValue(frame, duration, fps), IsSnow ? 0 : AngleAnimation.GetValue(frame, duration, fps), SpeedVariation.GetValue(frame, duration, fps));
    internal RainfallEmission CreateEmission(RainfallMotion motion, long duration, int fps)
    {
        var signature = MotionSignature(duration, fps);
        return new(seconds => motion.EvaluateSeconds(seconds, fps, signature, frame => GetVelocity(frame, duration, fps)),
            seconds => IsSnow ? 0 : AngleAnimation.GetValue((long)Math.Round(seconds * fps), duration, fps));
    }

    // 中間点・曲線・固定値の編集を含め、移動キャッシュの入力全体を比較します。
    internal string MotionSignature(long duration, int fps)
    {
        var text = new StringBuilder();
        Append(duration); Append(fps); Append(Kind); Append(SchemaVersion);
        Append(Shape);
        Append(CrystalStyle);
        Append(SnowMotion);
        Append(WindEnabled);
        Append(WindRate);
        Append(WindResponseVariation);
        Append(RainSizeMotionLinked);
        Append(MotionBlurEnabled);
        Append(MotionBlurMode);
        Append(DriftEnabled);
        Append(DriftRate);
        Append(DriftIrregularity);
        Append(VortexClockwise);
        Append(SnowSoftness);
        Append(RotationSpeed);
        Append(InitialRotationVariation);
        Append(RotationSpeedVariation);
        Append(FollowDirection);
        Append(KeepUpright);
        Append(MirrorWhenUpright);
        Append(SwayEnabled);
        Append(SwayAmplitude);
        Append(SwayPeriod);
        Append(PngImageId);
        Append(PngImageRevision);
        Append(ColorAlpha);
        Append(OnsetEnabled);
        Append(StartSeconds);
        Append(AppearanceSeconds);
        Append(RampEnabled);
        Append(RampSeconds);
        Append(Seed);
        foreach (var animation in Current.AllAnimations)
        {
            Append(animation.AnimationType); Append(animation.Span); Append(animation.Length); Append(animation.Loop);
            if (animation.KeyFrames is { } keys) foreach (var frame in keys.Frames) Append(frame);
            text.Append('|');
            foreach (var value in animation.Values) Append(value.Value);
            text.Append('|');
            if (animation.Bezier is { } bezier)
            {
                Append(bezier.IsQuadratic);
                foreach (var point in bezier.Points)
                {
                    Append(point.Point.X); Append(point.Point.Y);
                    Append(point.ControlPoint1.X); Append(point.ControlPoint1.Y);
                    Append(point.ControlPoint2.X); Append(point.ControlPoint2.Y);
                }
            }
            text.Append('|');
        }
        return text.ToString();
        void Append<T>(T value)
        {
            var item = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            text.Append(item.Length).Append(':').Append(item);
        }
    }
    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new RainfallEffectProcessor(devices, this);
    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    protected override IEnumerable<IAnimatable> GetAnimatables() => AppearanceBanks;
}

internal enum RainAppearance
{
    [Display(Name = "線")] Streak = RainfallShape.Streak,
    [Display(Name = "丸")] Circle = RainfallShape.Circle,
    [Display(Name = "水滴")] CartoonDrop = RainfallShape.CartoonDrop,
}
internal enum SnowAppearance
{
    [Display(Name = "玉雪")] Round = RainfallShape.SnowRound,
    [Display(Name = "ぼたん雪")] Clump = RainfallShape.SnowClump,
    [Display(Name = "雪の結晶")] Crystal = RainfallShape.SnowCrystal,
    [Display(Name = "PNG")] Png = RainfallShape.Png,
}
internal enum BubbleAppearance
{
    [Display(Name = "水泡A")] Bubble = RainfallShape.Bubble,
    [Display(Name = "水泡B")] LensBubble = RainfallShape.LensBubble,
}
internal enum RainfallOnsetPattern
{
    [Display(Name = "均等")] Uniform,
    [Display(Name = "ポツポツから")] Sparse,
}
