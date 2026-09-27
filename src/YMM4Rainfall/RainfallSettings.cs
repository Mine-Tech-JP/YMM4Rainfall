// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Text.Json.Serialization;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;

namespace YMM4Rainfall;

/// <summary>種類ごとに保持する設定。非選択時もAnimationとPNG参照を保存します。</summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
internal sealed class RainfallSettings : Animatable
{
    public RainfallSettings() : this(WeatherKind.Rain) { }
    public RainfallSettings(WeatherKind kind)
    {
        if (kind == WeatherKind.Snow)
        {
            Shape = RainfallShape.SnowRound;
            AmountAnimation.SetFirstValue(20); SpeedAnimation.SetFirstValue(60);
            AngleAnimation.SetFirstValue(0); Opacity.SetFirstValue(80); ParticleSize.SetFirstValue(6);
            SpeedVariation.SetFirstValue(35); OpacityVariation.SetFirstValue(20);
            DriftEnabled = true; WindResponseVariation = 30; RotationSpeed = 20;
            InitialRotationVariation = 100; RotationSpeedVariation = 50;
        }
        else if (kind == WeatherKind.Bubble)
        {
            Shape = RainfallShape.Bubble;
            AmountAnimation.SetFirstValue(5); SpeedAnimation.SetFirstValue(100);
            AngleAnimation.SetFirstValue(180); Opacity.SetFirstValue(60); ParticleSize.SetFirstValue(48);
            SpeedVariation.SetFirstValue(20);
        }
        else if (kind == WeatherKind.CustomPng)
        {
            Shape = RainfallShape.Png;
            AmountAnimation.SetFirstValue(3); SpeedAnimation.SetFirstValue(150);
            AngleAnimation.SetFirstValue(90); Opacity.SetFirstValue(100); ParticleSize.SetFirstValue(96);
            SpeedVariation.SetFirstValue(0);
        }
    }

    public Animation AmountAnimation { get; } = new(30, 0, 100);
    public Animation SpeedAnimation { get; } = new(900, 0, 4000);
    public Animation AngleAnimation { get; } = new(10, double.MinValue, double.MaxValue);
    public Animation LengthAnimation { get; } = new(24, 1, 200);
    public Animation ThicknessAnimation { get; } = new(1, 0.1, 8);
    public Animation ParticleSize { get; } = new(8, 1, 512);
    public Animation PngScale { get; } = new(100, 1, 1000);
    public Animation Opacity { get; } = new(35, 0, 100);
    public Animation SpeedVariation { get; } = new(40, 0, 100);
    public Animation ThicknessVariation { get; } = new(40, 0, 100);
    public Animation OpacityVariation { get; } = new(0, 0, 100);
    public Animation RotationAngle { get; } = new(0, double.MinValue, double.MaxValue);
    public Animation LensReflection { get; } = new(25, 0, 100);
    public Animation OutlineOpacity { get; } = new(100, 0, 100);
    public Animation MotionBlurStrength { get; } = new(50, 0, 100);
    public Animation Red { get; } = new(100, 0, 100);
    public Animation Green { get; } = new(100, 0, 100);
    public Animation Blue { get; } = new(100, 0, 100);
    public Animation WindAngle { get; } = new(90, double.MinValue, double.MaxValue);
    public Animation WindSpeed { get; } = new(100, 0, 2000);
    public Animation WindVariation { get; } = new(25, 0, 100);
    public Animation DriftWidth { get; } = new(15, 0, 200);
    public Animation LocalCenterX { get; } = new(0, -32768, 32768);
    public Animation LocalCenterY { get; } = new(0, -32768, 32768);
    public Animation VortexRadius { get; } = new(400, 1, 8192);
    public Animation VortexSpeed { get; } = new(150, 0, 2000);
    public Animation UpdraftWidth { get; } = new(800, 1, 8192);
    public Animation UpdraftHeight { get; } = new(400, 1, 8192);
    public Animation UpdraftSpeed { get; } = new(200, 0, 2000);

    private RainfallShape shape = RainfallShape.Streak;
    public RainfallShape Shape { get => shape; set => Set(ref shape, value); }

    private SnowCrystalStyle crystalStyle = SnowCrystalStyle.Mixed;
    public SnowCrystalStyle CrystalStyle { get => crystalStyle; set => Set(ref crystalStyle, value); }

    private SnowMotionKind snowMotion = SnowMotionKind.Basic;
    public SnowMotionKind SnowMotion { get => snowMotion; set => Set(ref snowMotion, value); }

    private bool windEnabled = false;
    public bool WindEnabled { get => windEnabled; set => Set(ref windEnabled, value); }

    private double windRate = 1;
    public double WindRate { get => windRate; set => Set(ref windRate, value); }

    private double windResponseVariation = 0;
    public double WindResponseVariation { get => windResponseVariation; set => Set(ref windResponseVariation, value); }

    private bool rainSizeMotionLinked;
    public bool RainSizeMotionLinked { get => rainSizeMotionLinked; set => Set(ref rainSizeMotionLinked, value); }

    private bool motionBlurEnabled;
    public bool MotionBlurEnabled { get => motionBlurEnabled; set => Set(ref motionBlurEnabled, value); }

    private MotionBlurMode motionBlurMode;
    public MotionBlurMode MotionBlurMode { get => motionBlurMode; set => Set(ref motionBlurMode, value); }

    private bool driftEnabled = false;
    public bool DriftEnabled { get => driftEnabled; set => Set(ref driftEnabled, value); }

    private double driftRate = 1;
    public double DriftRate { get => driftRate; set => Set(ref driftRate, value); }

    private double driftIrregularity = 60;
    public double DriftIrregularity { get => driftIrregularity; set => Set(ref driftIrregularity, value); }

    private bool vortexClockwise = true;
    public bool VortexClockwise { get => vortexClockwise; set => Set(ref vortexClockwise, value); }

    private double snowSoftness = 50;
    public double SnowSoftness { get => snowSoftness; set => Set(ref snowSoftness, value); }

    private double rotationSpeed = 0;
    public double RotationSpeed { get => rotationSpeed; set => Set(ref rotationSpeed, value); }

    private double initialRotationVariation = 0;
    public double InitialRotationVariation { get => initialRotationVariation; set => Set(ref initialRotationVariation, value); }

    private double rotationSpeedVariation = 0;
    public double RotationSpeedVariation { get => rotationSpeedVariation; set => Set(ref rotationSpeedVariation, value); }

    private bool followDirection = false;
    public bool FollowDirection { get => followDirection; set => Set(ref followDirection, value); }

    private bool keepUpright = false;
    public bool KeepUpright { get => keepUpright; set => Set(ref keepUpright, value); }

    private bool mirrorWhenUpright = false;
    public bool MirrorWhenUpright { get => mirrorWhenUpright; set => Set(ref mirrorWhenUpright, value); }

    private bool swayEnabled = false;
    public bool SwayEnabled { get => swayEnabled; set => Set(ref swayEnabled, value); }

    private double swayAmplitude = 20;
    public double SwayAmplitude { get => swayAmplitude; set => Set(ref swayAmplitude, value); }

    private double swayPeriod = 2;
    public double SwayPeriod { get => swayPeriod; set => Set(ref swayPeriod, value); }

    private RainfallPngReference pngReference;
    public RainfallPngReference PngReference
    {
        get => pngReference;
        set
        {
            if (!Set(ref pngReference, value)) return;
            PngStatus = value.Id == Guid.Empty ? "PNG画像を選択してください。" : "次の描画でPNG画像を確認します。";
            OnPropertyChanged(nameof(PngImageId));
            OnPropertyChanged(nameof(PngImageRevision));
        }
    }
    [Browsable(false), JsonIgnore]
    public Guid PngImageId { get => PngReference.Id; set => PngReference = PngReference with { Id = value }; }
    public bool ShouldSerializePngImageId() => false;
    [Browsable(false), JsonIgnore]
    public long PngImageRevision { get => PngReference.Revision; set => PngReference = PngReference with { Revision = Math.Max(0, value) }; }
    public bool ShouldSerializePngImageRevision() => false;

    private string pngStatus = "PNG画像を選択してください。";
    private RainfallPngReference statusReference;
    [Browsable(false), JsonIgnore]
    public string PngStatus
    {
        get => statusReference == PngReference ? pngStatus
            : PngReference.Id == Guid.Empty ? "PNG画像を選択してください。" : "次の描画でPNG画像を確認します。";
        internal set
        {
            if (pngStatus == value && statusReference == PngReference) return;
            pngStatus = value;
            statusReference = PngReference;
            OnPropertyChanged(nameof(PngStatus));
        }
    }
    public bool ShouldSerializePngStatus() => false;

    private byte colorAlpha = 255;
    public byte ColorAlpha { get => colorAlpha; set => Set(ref colorAlpha, value); }

    private bool onsetEnabled = false;
    public bool OnsetEnabled { get => onsetEnabled; set => Set(ref onsetEnabled, value); }

    private double startSeconds = 0;
    public double StartSeconds { get => startSeconds; set => Set(ref startSeconds, value); }

    private double appearanceSeconds = 5;
    public double AppearanceSeconds { get => appearanceSeconds; set => Set(ref appearanceSeconds, value); }

    private bool rampEnabled = false;
    public bool RampEnabled { get => rampEnabled; set => Set(ref rampEnabled, value); }

    private double rampSeconds = 5;
    public double RampSeconds { get => rampSeconds; set => Set(ref rampSeconds, value); }

    private int seed = 1;
    public int Seed { get => seed; set => Set(ref seed, value); }

    [Browsable(false), JsonIgnore]
    public Animation[] AllAnimations => [AmountAnimation, SpeedAnimation, AngleAnimation, LengthAnimation, ThicknessAnimation, ParticleSize, PngScale, Opacity, SpeedVariation, ThicknessVariation, OpacityVariation, RotationAngle, LensReflection, OutlineOpacity, MotionBlurStrength, Red, Green, Blue, WindAngle, WindSpeed, WindVariation, DriftWidth, LocalCenterX, LocalCenterY, VortexRadius, VortexSpeed, UpdraftWidth, UpdraftHeight, UpdraftSpeed];
    public bool ShouldSerializeAllAnimations() => false;
    protected override IEnumerable<IAnimatable> GetAnimatables() => AllAnimations;
}

internal readonly record struct RainfallPngReference(Guid Id, long Revision);
