// SPDX-License-Identifier: MPL-2.0
namespace YMM4Rainfall.Simulation;

/// <summary>天候の移動計算で使う倍精度の二次元ベクトルです。</summary>
internal readonly record struct WeatherVector(double X, double Y)
{
    public double LengthSquared => X * X + Y * Y;

    public static WeatherVector operator +(WeatherVector left, WeatherVector right)
        => new(left.X + right.X, left.Y + right.Y);

    public static WeatherVector operator -(WeatherVector left, WeatherVector right)
        => new(left.X - right.X, left.Y - right.Y);

    public static WeatherVector operator *(WeatherVector value, double scale)
        => new(value.X * scale, value.Y * scale);

    public static WeatherVector operator /(WeatherVector value, double scale)
        => new(value.X / scale, value.Y / scale);
}

internal readonly record struct WeatherDrift(WeatherVector Offset, WeatherVector Velocity);

/// <summary>風、漂い、渦、巻き上がりの再現可能な速度場を提供します。</summary>
internal static class WeatherFields
{
    private const double DirectionEpsilon = 1e-18;

    public static WeatherVector Direction(double speed, double angle)
    {
        if (!double.IsFinite(speed) || !double.IsFinite(angle)) return default;
        var radians = ((angle % 360 + 360) % 360) * Math.PI / 180;
        return new(Math.Sin(radians) * speed, Math.Cos(radians) * speed);
    }

    public static WeatherVector WindVelocity(RainfallParameters parameters, double seconds)
    {
        parameters = parameters.Normalize();
        if (!parameters.WindEnabled || parameters.Kind is not (WeatherKind.Rain or WeatherKind.Snow) || parameters.WindSpeed == 0)
            return default;

        var multiplier = 1.0;
        if (parameters.WindRate > 0 && parameters.WindVariation > 0 && double.IsFinite(seconds))
        {
            // 1倍では4秒を一周期とし、強さを0～2倍の範囲で滑らかに変化させます。
            var phase = UnitRandom(0, parameters.Seed, 20) * Math.Tau;
            multiplier += parameters.WindVariation / 100
                * Math.Sin(seconds * parameters.WindRate * Math.Tau / 4 + phase);
        }
        return Direction(parameters.WindSpeed * Math.Max(0, multiplier), parameters.WindAngle);
    }

    public static WeatherVector VortexVelocity(
        RainfallBounds bounds, RainfallParameters parameters, double x, double y)
    {
        if (!bounds.IsValid || parameters.VortexSpeed == 0 || !double.IsFinite(x) || !double.IsFinite(y))
            return default;

        var centerX = (bounds.Left + bounds.Right) / 2 + parameters.LocalCenterX;
        var centerY = (bounds.Top + bounds.Bottom) / 2 + parameters.LocalCenterY;
        var dx = x - centerX;
        var dy = y - centerY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var radius = parameters.VortexRadius;
        if (!double.IsFinite(distance) || distance <= DirectionEpsilon || distance >= radius)
            return default;

        var q = distance / radius;
        var scale = parameters.VortexSpeed * (27.0 / 4) * Math.Pow(1 - q, 2) / radius;
        if (!parameters.VortexClockwise) scale = -scale;
        // 画面座標では右側の点が下へ動く向きを時計回りとします。
        return new(-dy * scale, dx * scale);
    }

    public static WeatherVector UpdraftVelocity(
        RainfallBounds bounds, RainfallParameters parameters, double x, double y)
    {
        if (!bounds.IsValid || parameters.UpdraftSpeed == 0 || !double.IsFinite(x) || !double.IsFinite(y))
            return default;

        var centerX = (bounds.Left + bounds.Right) / 2 + parameters.LocalCenterX;
        var centerY = (bounds.Top + bounds.Bottom) / 2 + parameters.LocalCenterY;
        var u = (x - centerX) / (parameters.UpdraftWidth / 2);
        var v = (y - centerY) / (parameters.UpdraftHeight / 2);
        if (Math.Abs(u) >= 1 || Math.Abs(v) >= 1) return default;

        // 中央を上昇し、上側から左右へ散って側方を下降する無発散の循環流です。
        // 横長の範囲では分母を滑らかに増やし、横速度が縦横比に比例して発散するのを防ぎます。
        var u2 = u * u;
        var v2 = v * v;
        var oneMinusU2 = 1 - u2;
        var oneMinusV2 = 1 - v2;
        var aspectExcess = Math.Max(0, parameters.UpdraftWidth / parameters.UpdraftHeight - 2);
        var profile = aspectExcess * aspectExcess;
        var denominator = 1 + profile * u2;
        var speed = parameters.UpdraftSpeed;
        var horizontal = -6 * speed * (parameters.UpdraftWidth / parameters.UpdraftHeight) * u * v
            * oneMinusU2 * oneMinusU2 * oneMinusU2 / denominator
            * oneMinusV2 * oneMinusV2;
        var vertical = -speed * oneMinusU2 * oneMinusU2
            * (1 - 7 * u2 - profile * u2 * (1 + 5 * u2)) / (denominator * denominator)
            * oneMinusV2 * oneMinusV2 * oneMinusV2;
        return double.IsFinite(horizontal) && double.IsFinite(vertical)
            ? new(horizontal, vertical)
            : default;
    }

    public static WeatherDrift Drift(
        RainfallParameters parameters, int particleIndex, double ageSeconds, bool onsetEnabled)
    {
        if (parameters.Kind != WeatherKind.Snow || !parameters.DriftEnabled || parameters.DriftWidth == 0 ||
            parameters.DriftRate == 0 || !double.IsFinite(ageSeconds) || ageSeconds < 0)
            return default;

        var omega = parameters.DriftRate * Math.Tau / 4;
        var phase = UnitRandom(particleIndex, parameters.Seed, 30) * Math.Tau;
        var irregularity = parameters.DriftIrregularity / 100;
        var regular = Math.Sin(ageSeconds * omega + phase);
        var regularDerivative = omega * Math.Cos(ageSeconds * omega + phase);
        var phase2 = UnitRandom(particleIndex, parameters.Seed, 31) * Math.Tau;
        var phase3 = UnitRandom(particleIndex, parameters.Seed, 32) * Math.Tau;
        var irregular = 0.6 * Math.Sin(ageSeconds * omega * 0.73 + phase2)
            + 0.4 * Math.Sin(ageSeconds * omega * 1.37 + phase3);
        var irregularDerivative = 0.6 * omega * 0.73 * Math.Cos(ageSeconds * omega * 0.73 + phase2)
            + 0.4 * omega * 1.37 * Math.Cos(ageSeconds * omega * 1.37 + phase3);
        var wave = regular * (1 - irregularity) + irregular * irregularity;
        var derivative = regularDerivative * (1 - irregularity) + irregularDerivative * irregularity;

        var envelope = 1.0;
        var envelopeDerivative = 0.0;
        if (onsetEnabled)
        {
            var riseSeconds = 4 / parameters.DriftRate;
            var progress = Math.Clamp(ageSeconds / riseSeconds, 0, 1);
            envelope = progress * progress * (3 - 2 * progress);
            if (progress is > 0 and < 1)
                envelopeDerivative = 6 * progress * (1 - progress) / riseSeconds;
        }

        return new(
            new(parameters.DriftWidth * wave * envelope, 0),
            new(parameters.DriftWidth * (derivative * envelope + wave * envelopeDerivative), 0));
    }

    public static WeatherDrift Sway(
        RainfallParameters parameters, int particleIndex, double ageSeconds, bool onsetEnabled)
    {
        if (!parameters.SwayEnabled || parameters.Shape is not (RainfallShape.Bubble or RainfallShape.LensBubble or RainfallShape.Png) ||
            parameters.SwayAmplitude == 0 || !double.IsFinite(ageSeconds) || ageSeconds < 0)
            return default;

        var omega = Math.Tau / parameters.SwayPeriod;
        var phase = UnitRandom(particleIndex, parameters.Seed, 5) * Math.Tau;
        var wave = Math.Sin(ageSeconds * omega + phase);
        var derivative = omega * Math.Cos(ageSeconds * omega + phase);
        var envelope = 1.0;
        var envelopeDerivative = 0.0;
        if (onsetEnabled)
        {
            var progress = Math.Clamp(ageSeconds / parameters.SwayPeriod, 0, 1);
            envelope = progress * progress * (3 - 2 * progress);
            if (progress is > 0 and < 1)
                envelopeDerivative = 6 * progress * (1 - progress) / parameters.SwayPeriod;
        }

        var lateral = Direction(1, parameters.Angle + 90);
        var offset = parameters.SwayAmplitude * wave * envelope;
        var speed = parameters.SwayAmplitude * (derivative * envelope + wave * envelopeDerivative);
        return new(lateral * offset, lateral * speed);
    }

    public static double DirectionAngle(WeatherVector velocity, double fallbackAngle)
    {
        if (velocity.LengthSquared <= DirectionEpsilon || !double.IsFinite(velocity.LengthSquared))
            return fallbackAngle;
        return Math.Atan2(velocity.X, velocity.Y) * 180 / Math.PI;
    }

    public static double Wrap(double value, double span)
    {
        if (!double.IsFinite(value) || !double.IsFinite(span) || span <= 0) return 0;
        var remainder = value % span;
        return remainder < 0 ? remainder + span : remainder;
    }

    public static double UnitRandom(int index, int seed, uint channel)
    {
        unchecked
        {
            var value = (uint)seed ^ ((uint)index + 1) * 0x9E3779B9u ^ (channel + 1) * 0x85EBCA6Bu;
            value = (value ^ (value >> 16)) * 0x7FEB352Du;
            value = (value ^ (value >> 15)) * 0x846CA68Bu;
            value ^= value >> 16;
            return (value >> 8) / 16777216.0;
        }
    }

    private static double SmoothEdge(double normalizedDistance)
        => 1 - 3 * normalizedDistance * normalizedDistance
            + 2 * normalizedDistance * normalizedDistance * normalizedDistance;
}
