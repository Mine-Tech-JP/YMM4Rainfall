// SPDX-License-Identifier: MPL-2.0
using System.Text.Json.Serialization;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;

namespace YMM4Rainfall;

/// <summary>見た目別の設定と、種類内で最後に選んだ見た目を保持します。</summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
internal sealed class RainfallAppearanceBank : Animatable
{
    public RainfallAppearanceBank() : this(WeatherKind.Rain) { }

    public RainfallAppearanceBank(WeatherKind kind)
    {
        Variants = Shapes(kind).Select(shape => Create(kind, shape)).ToArray();
        selectedShape = kind == WeatherKind.Snow ? RainfallShape.SnowRound : Variants[0].Shape;
    }

    private RainfallShape selectedShape;
    public RainfallShape SelectedShape { get => selectedShape; set => Set(ref selectedShape, value); }
    private RainfallSettings[] variants = [];
    public RainfallSettings[] Variants
    {
        get => variants;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0 || value.Any(item => item is null))
                throw new ArgumentException("見た目別設定に空の要素は指定できません。", nameof(value));
            Set(ref variants, value);
        }
    }

    internal RainfallSettings Current => Variants.FirstOrDefault(value => value.Shape == SelectedShape)
        ?? Variants[0];

    internal void Import(RainfallSettings settings)
    {
        var index = Array.FindIndex(Variants, item => item.Shape == settings.Shape);
        if (index >= 0)
        {
            var replacement = (RainfallSettings[])Variants.Clone();
            replacement[index] = settings;
            Variants = replacement;
        }
        else Variants = [.. Variants, settings];
        SelectedShape = settings.Shape;
    }

    internal bool IsValid(WeatherKind kind)
        => Shapes(kind).Contains(SelectedShape) && Variants.Length == Shapes(kind).Length &&
            Variants.Select(item => item.Shape).Distinct().Count() == Variants.Length &&
            Variants.All(item => Shapes(kind).Contains(item.Shape));

    internal static RainfallShape[] Shapes(WeatherKind kind) => kind switch
    {
        WeatherKind.Snow => [RainfallShape.SnowRound, RainfallShape.SnowClump,
            RainfallShape.SnowCrystal, RainfallShape.Png],
        WeatherKind.Bubble => [RainfallShape.Bubble, RainfallShape.LensBubble],
        WeatherKind.CustomPng => [RainfallShape.Png],
        _ => [RainfallShape.Streak, RainfallShape.Circle, RainfallShape.CartoonDrop],
    };

    private static RainfallSettings Create(WeatherKind kind, RainfallShape shape)
    {
        var settings = new RainfallSettings(kind) { Shape = shape };
        switch (shape)
        {
            case RainfallShape.Circle:
                settings.ParticleSize.SetFirstValue(8);
                settings.Opacity.SetFirstValue(70);
                break;
            case RainfallShape.CartoonDrop:
                settings.ParticleSize.SetFirstValue(24);
                settings.SpeedAnimation.SetFirstValue(480);
                settings.AmountAnimation.SetFirstValue(12);
                settings.Opacity.SetFirstValue(80);
                break;
            case RainfallShape.SnowClump: settings.ParticleSize.SetFirstValue(18); break;
            case RainfallShape.SnowCrystal: settings.ParticleSize.SetFirstValue(48); break;
            case RainfallShape.Png when kind == WeatherKind.Snow: settings.ParticleSize.SetFirstValue(48); break;
            case RainfallShape.LensBubble: settings.ParticleSize.SetFirstValue(96); break;
        }
        return settings;
    }

    protected override IEnumerable<IAnimatable> GetAnimatables() => Variants;
}
