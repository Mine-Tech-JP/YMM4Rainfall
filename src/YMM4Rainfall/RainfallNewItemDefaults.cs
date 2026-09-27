// SPDX-License-Identifier: MPL-2.0
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall;

/// <summary>利用者の2026年9月13日修正版テンプレートに基づく、新規追加専用の初期値です。</summary>
internal static class RainfallNewItemDefaults
{
    internal static void Apply(RainfallSettings settings, WeatherKind kind)
    {
        // 保存データの省略値は変更せず、新規エフェクトの作成時だけ上書きします。
        switch (kind, settings.Shape)
        {
            case (WeatherKind.Rain, RainfallShape.Streak):
                settings.SpeedAnimation.SetFirstValue(4000);
                settings.AngleAnimation.SetFirstValue(0);
                settings.LengthAnimation.SetFirstValue(100);
                settings.ThicknessAnimation.SetFirstValue(3);
                settings.Opacity.SetFirstValue(50);
                settings.SpeedVariation.SetFirstValue(100);
                settings.ThicknessVariation.SetFirstValue(50);
                settings.OpacityVariation.SetFirstValue(50);
                settings.MotionBlurStrength.SetFirstValue(100);
                settings.WindSpeed.SetFirstValue(200);
                settings.WindVariation.SetFirstValue(20);
                settings.WindEnabled = true;
                settings.WindResponseVariation = 10;
                settings.RainSizeMotionLinked = true;
                settings.MotionBlurEnabled = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.OnsetEnabled = true;
                settings.AppearanceSeconds = 20;
                settings.RampEnabled = true;
                settings.RampSeconds = 20;
                settings.Seed = 0;
                break;
            case (WeatherKind.Rain, RainfallShape.Circle):
                settings.AmountAnimation.SetFirstValue(100);
                settings.SpeedAnimation.SetFirstValue(1080);
                settings.AngleAnimation.SetFirstValue(-10);
                settings.ParticleSize.SetFirstValue(3);
                settings.SpeedVariation.SetFirstValue(50);
                settings.ThicknessVariation.SetFirstValue(0);
                settings.OpacityVariation.SetFirstValue(50);
                settings.MotionBlurStrength.SetFirstValue(5);
                settings.WindEnabled = true;
                settings.WindResponseVariation = 10;
                settings.RainSizeMotionLinked = true;
                settings.MotionBlurEnabled = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.OnsetEnabled = true;
                settings.AppearanceSeconds = 10;
                settings.RampEnabled = true;
                settings.RampSeconds = 10;
                settings.Seed = 0;
                break;
            case (WeatherKind.Rain, RainfallShape.CartoonDrop):
                settings.MotionBlurStrength.SetFirstValue(10);
                settings.Red.SetFirstValue(60);
                settings.Green.SetFirstValue(76.07843137254902);
                settings.WindVariation.SetFirstValue(100);
                settings.WindEnabled = true;
                settings.WindResponseVariation = 100;
                settings.RainSizeMotionLinked = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.FollowDirection = true;
                settings.OnsetEnabled = true;
                settings.AppearanceSeconds = 10;
                settings.RampEnabled = true;
                settings.RampSeconds = 10;
                settings.Seed = 0;
                break;
            case (WeatherKind.Snow, RainfallShape.SnowRound):
                settings.AmountAnimation.SetFirstValue(30);
                settings.SpeedAnimation.SetFirstValue(100);
                settings.ParticleSize.SetFirstValue(10);
                settings.Opacity.SetFirstValue(100);
                settings.SpeedVariation.SetFirstValue(50);
                settings.ThicknessVariation.SetFirstValue(100);
                settings.OpacityVariation.SetFirstValue(0);
                settings.MotionBlurStrength.SetFirstValue(25);
                settings.WindSpeed.SetFirstValue(50);
                settings.DriftWidth.SetFirstValue(50);
                settings.RainSizeMotionLinked = true;
                settings.MotionBlurEnabled = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.DriftIrregularity = 75;
                settings.Seed = 0;
                break;
            case (WeatherKind.Snow, RainfallShape.SnowClump):
                settings.RainSizeMotionLinked = true;
                settings.AmountAnimation.SetFirstValue(25);
                settings.SpeedAnimation.SetFirstValue(150);
                settings.ParticleSize.SetFirstValue(50);
                settings.Opacity.SetFirstValue(100);
                settings.SpeedVariation.SetFirstValue(25);
                settings.DriftWidth.SetFirstValue(50);
                settings.MotionBlurEnabled = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.SnowSoftness = 75;
                settings.RotationSpeed = 100;
                settings.FollowDirection = true;
                settings.OnsetEnabled = true;
                settings.AppearanceSeconds = 10;
                settings.RampEnabled = true;
                settings.RampSeconds = 10;
                settings.Seed = 0;
                break;
            case (WeatherKind.Snow, RainfallShape.SnowCrystal):
                settings.ParticleSize.SetFirstValue(50);
                settings.SpeedVariation.SetFirstValue(50);
                settings.ThicknessVariation.SetFirstValue(50);
                settings.OpacityVariation.SetFirstValue(25);
                settings.DriftWidth.SetFirstValue(25);
                settings.CrystalStyle = SnowCrystalStyle.Dendrite;
                settings.RainSizeMotionLinked = true;
                settings.RotationSpeed = 100;
                settings.Seed = 0;
                break;
            case (WeatherKind.Snow, RainfallShape.Png):
                settings.Seed = 0;
                break;
            case (WeatherKind.Bubble, RainfallShape.Bubble):
                settings.ParticleSize.SetFirstValue(100);
                settings.Opacity.SetFirstValue(74);
                settings.SpeedVariation.SetFirstValue(25);
                settings.ThicknessVariation.SetFirstValue(50);
                settings.OpacityVariation.SetFirstValue(25);
                settings.OutlineOpacity.SetFirstValue(50);
                settings.MotionBlurStrength.SetFirstValue(100);
                settings.RainSizeMotionLinked = true;
                settings.MotionBlurEnabled = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.RotationSpeed = 50;
                settings.InitialRotationVariation = 50;
                settings.RotationSpeedVariation = 25;
                settings.FollowDirection = true;
                settings.SwayEnabled = true;
                settings.SwayAmplitude = 50;
                settings.SwayPeriod = 3;
                settings.OnsetEnabled = true;
                settings.AppearanceSeconds = 10;
                settings.RampEnabled = true;
                settings.RampSeconds = 10;
                settings.Seed = 0;
                break;
            case (WeatherKind.Bubble, RainfallShape.LensBubble):
                settings.ParticleSize.SetFirstValue(100);
                settings.Opacity.SetFirstValue(75);
                settings.SpeedVariation.SetFirstValue(25);
                settings.ThicknessVariation.SetFirstValue(50);
                settings.OpacityVariation.SetFirstValue(25);
                settings.LensReflection.SetFirstValue(50);
                settings.OutlineOpacity.SetFirstValue(50);
                settings.MotionBlurStrength.SetFirstValue(100);
                settings.RainSizeMotionLinked = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.RotationSpeed = 50;
                settings.InitialRotationVariation = 50;
                settings.RotationSpeedVariation = 25;
                settings.FollowDirection = true;
                settings.SwayEnabled = true;
                settings.SwayAmplitude = 50;
                settings.SwayPeriod = 3;
                settings.OnsetEnabled = true;
                settings.AppearanceSeconds = 10;
                settings.RampEnabled = true;
                settings.RampSeconds = 10;
                settings.Seed = 0;
                break;
            case (WeatherKind.CustomPng, RainfallShape.Png):
                settings.AmountAnimation.SetFirstValue(5);
                settings.SpeedAnimation.SetFirstValue(250);
                settings.AngleAnimation.SetFirstValue(0);
                settings.SpeedVariation.SetFirstValue(50);
                settings.ThicknessVariation.SetFirstValue(50);
                settings.RotationAngle.SetFirstValue(-360);
                settings.RainSizeMotionLinked = true;
                settings.MotionBlurMode = MotionBlurMode.Trailing;
                settings.RotationSpeed = -100;
                settings.InitialRotationVariation = 50;
                settings.RotationSpeedVariation = 50;
                settings.MirrorWhenUpright = true;
                settings.OnsetEnabled = true;
                settings.AppearanceSeconds = 10;
                settings.RampEnabled = true;
                settings.RampSeconds = 10;
                settings.Seed = 0;
                break;
        }
    }
}
