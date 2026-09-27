// SPDX-License-Identifier: MPL-2.0
namespace YMM4Rainfall.Simulation;

/// <summary>描画に使う値です。UIや保存データからの値は使用前に正規化します。</summary>
internal readonly record struct RainfallParameters(
    double Amount, double Speed, double Angle, double Length,
    double Thickness, double Opacity, int Seed,
    double ThicknessVariation = 40, bool OnsetEnabled = false,
    double StartSeconds = 0, double AppearanceSeconds = 5,
    double Red = 1, double Green = 1, double Blue = 1, double SpeedVariation = 40,
    bool RampEnabled = false, double RampSeconds = 5,
    RainfallShape Shape = RainfallShape.Streak, double ParticleSize = 8,
    double OpacityVariation = 0, double RotationAngle = 0, double RotationSpeed = 0, bool FollowDirection = false, bool KeepUpright = false, double LensReflection = 25, bool MirrorWhenUpright = false, bool SwayEnabled = false, double SwayAmplitude = 20, double SwayPeriod = 2)
{
    public WeatherKind Kind { get; init; }
    public double PngScale { get; init; } = 100;
    public int PngSourceSize { get; init; }
    public double PngMaximumScale { get; init; } = MaximumPngScale;
    public const double MaximumPngScale = 1000;
    public const int MaximumPngSourceSize = 2048;
    public static double NormalizePngScale(double value) => ClampFinite(value, 1, MaximumPngScale, 100);
    // Animation中も配置領域を固定し、大きなPNGが見えている途中で周回しないようにします。
    public double ParticleSizeLimit => Shape == RainfallShape.Png
        ? Math.Clamp(PngSourceSize, 1, MaximumPngSourceSize) * NormalizePngScale(PngMaximumScale) / 100
        : MaximumParticleSize;
    public SnowMotionKind SnowMotion { get; init; }
    public SnowCrystalStyle CrystalStyle { get; init; }
    public bool WindEnabled { get; init; }
    public double WindAngle { get; init; } = 90;
    public double WindSpeed { get; init; } = 100;
    public double WindVariation { get; init; } = 25;
    public double WindRate { get; init; } = 1;
    public double WindResponseVariation { get; init; }
    public bool RainSizeMotionLinked { get; init; }
    public bool MotionBlurEnabled { get; init; }
    public MotionBlurMode MotionBlurMode { get; init; }
    public double MotionBlurStrength { get; init; } = 50;
    public bool HasMotionBlur => MotionBlurEnabled && MotionBlurStrength > 0;
    public bool DriftEnabled { get; init; }
    public double DriftWidth { get; init; } = 15;
    public double DriftRate { get; init; } = 1;
    public double DriftIrregularity { get; init; } = 60;
    public double LocalCenterX { get; init; }
    public double LocalCenterY { get; init; }
    public double VortexRadius { get; init; } = 400;
    public double VortexSpeed { get; init; } = 150;
    public bool VortexClockwise { get; init; } = true;
    public double UpdraftWidth { get; init; } = 800;
    public double UpdraftHeight { get; init; } = 400;
    public double UpdraftSpeed { get; init; } = 200;
    public double SnowSoftness { get; init; } = 50;
    public double OutlineOpacity { get; init; } = 100;
    public double InitialRotationVariation { get; init; }
    public double RotationSpeedVariation { get; init; }
    public const double DefaultAmount = 30;
    public const double DefaultSpeed = 900;
    public const double DefaultAngle = 10;
    public const double DefaultLength = 24;
    public const double DefaultThickness = 1;
    public const double DefaultOpacity = 35;
    public const int DefaultSeed = 1;
    public const double MaximumSpeed = 4000;
    public const double MaximumLength = 200;
    public const double MinimumThickness = 0.1;
    public const double MaximumThickness = 8;
    public const double MaximumParticleSize = 512;
    public const int MaximumSeed = 99999;

    public static RainfallParameters Default => new(
        DefaultAmount, DefaultSpeed, DefaultAngle, DefaultLength,
        DefaultThickness, DefaultOpacity, DefaultSeed);

    public RainfallParameters Normalize() => this with
    {
        Kind = Enum.IsDefined(Kind) ? Kind : WeatherKind.Rain,
        SnowMotion = Enum.IsDefined(SnowMotion) ? SnowMotion : SnowMotionKind.Basic,
        CrystalStyle = Enum.IsDefined(CrystalStyle) ? CrystalStyle : SnowCrystalStyle.Mixed,
        WindAngle = double.IsFinite(WindAngle) ? WindAngle : 90,
        WindSpeed = ClampFinite(WindSpeed, 0, 2000, 100),
        WindVariation = ClampFinite(WindVariation, 0, 100, 25),
        WindRate = ClampFinite(WindRate, 0, 10, 1),
        WindResponseVariation = ClampFinite(WindResponseVariation, 0, 100, 0),
        DriftWidth = ClampFinite(DriftWidth, 0, 200, 15),
        DriftRate = ClampFinite(DriftRate, 0, 10, 1),
        DriftIrregularity = ClampFinite(DriftIrregularity, 0, 100, 60),
        LocalCenterX = ClampFinite(LocalCenterX, -32768, 32768, 0),
        LocalCenterY = ClampFinite(LocalCenterY, -32768, 32768, 0),
        VortexRadius = ClampFinite(VortexRadius, 1, 8192, 400),
        VortexSpeed = ClampFinite(VortexSpeed, 0, 2000, 150),
        UpdraftWidth = ClampFinite(UpdraftWidth, 1, 8192, 800),
        UpdraftHeight = ClampFinite(UpdraftHeight, 1, 8192, 400),
        UpdraftSpeed = ClampFinite(UpdraftSpeed, 0, 2000, 200),
        SnowSoftness = ClampFinite(SnowSoftness, 0, 100, 50),
        OutlineOpacity = ClampFinite(OutlineOpacity, 0, 100, 100),
        MotionBlurStrength = ClampFinite(MotionBlurStrength, 0, 100, 50),
        MotionBlurMode = Enum.IsDefined(MotionBlurMode) ? MotionBlurMode : MotionBlurMode.Symmetric,
        InitialRotationVariation = ClampFinite(InitialRotationVariation, 0, 100, 0),
        RotationSpeedVariation = ClampFinite(RotationSpeedVariation, 0, 100, 0),
        Shape = Enum.IsDefined(Shape) ? Shape : RainfallShape.Streak,
        PngScale = NormalizePngScale(PngScale),
        PngMaximumScale = NormalizePngScale(PngMaximumScale),
        PngSourceSize = Math.Clamp(PngSourceSize, 0, MaximumPngSourceSize),
        ParticleSize = Shape == RainfallShape.Png && PngSourceSize > 0
            ? Math.Min(PngSourceSize, MaximumPngSourceSize) * NormalizePngScale(PngScale) / 100
            : ClampFinite(ParticleSize, 1, MaximumParticleSize, 8),
        OpacityVariation = ClampFinite(OpacityVariation, 0, 100, 0),
        RotationAngle = double.IsFinite(RotationAngle) ? RotationAngle : 0,
        LensReflection = ClampFinite(LensReflection, 0, 100, 25),
        SwayAmplitude = ClampFinite(SwayAmplitude, 0, 200, 20),
        SwayPeriod = ClampFinite(SwayPeriod, 0.1, 60, 2),
        RotationSpeed = ClampFinite(RotationSpeed, -36000, 36000, 0),
        Amount = ClampFinite(Amount, 0, 100, DefaultAmount),
        Speed = ClampFinite(Speed, 0, MaximumSpeed, DefaultSpeed),
        // 保存値は回転数を保ち、三角関数へ渡す直前にだけ剰余を取ります。
        Angle = double.IsFinite(Angle) ? Angle : DefaultAngle,
        Length = ClampFinite(Length, 1, MaximumLength, DefaultLength),
        Thickness = ClampFinite(Thickness, MinimumThickness, MaximumThickness, DefaultThickness),
        Opacity = ClampFinite(Opacity, 0, 100, DefaultOpacity),
        Seed = Math.Clamp(Seed, 0, MaximumSeed),
        SpeedVariation = ClampFinite(SpeedVariation, 0, 100, 40),
        ThicknessVariation = ClampFinite(ThicknessVariation, 0, 100, 40),
        StartSeconds = ClampFinite(StartSeconds, 0, 36000, 0),
        RampSeconds = ClampFinite(RampSeconds, 0, 36000, 5),
        AppearanceSeconds = ClampFinite(AppearanceSeconds, 0, 36000, 5),
        Red = ClampFinite(Red, 0, 1, 1),
        Green = ClampFinite(Green, 0, 1, 1),
        Blue = ClampFinite(Blue, 0, 1, 1),
    };

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
