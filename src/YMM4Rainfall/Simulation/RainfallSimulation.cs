// SPDX-License-Identifier: MPL-2.0
using System.Numerics;

namespace YMM4Rainfall.Simulation;

internal readonly record struct RainfallBounds(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;

    public bool IsValid =>
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Right) && double.IsFinite(Bottom) &&
        Math.Abs(Left) <= 1_000_000 && Math.Abs(Top) <= 1_000_000 &&
        Math.Abs(Right) <= 1_000_000 && Math.Abs(Bottom) <= 1_000_000 &&
        Width is > 0 and <= 32768 && Height is > 0 and <= 32768;
}

/// <summary>粒の出生時刻の移動量と方向を、アイテムのAnimationから評価します。</summary>
internal sealed record RainfallEmission(Func<double, RainfallTravel> TravelAt, Func<double, double> AngleAt);

internal readonly record struct RainfallStroke(Vector2 Tail, Vector2 Head, float Thickness, float Opacity, float Size = 8, float Rotation = 0, bool FlipHorizontal = false, int Variant = 0)
{
    // 画像自体の向きとは分離した、画面端で循環する前の移動速度（px/秒）です。
    public Vector2 BlurVelocity { get; init; }
}

/// <summary>フレーム間の状態を持たず、同じ時刻と設定から同じ雨筋を作ります。</summary>
internal static class RainfallSimulation
{
    public const int MaximumStrokeCount = 4000;
    private const double FullHdArea = 1920.0 * 1080.0;

    public static double GetSeconds(double frame, double fps)
    {
        if (!double.IsFinite(frame) || !double.IsFinite(fps) || fps <= 0)
        {
            return 0;
        }

        var seconds = frame / fps;
        return double.IsFinite(seconds) ? seconds : 0;
    }

    public static RainfallStroke[] CreateFrame(
        RainfallBounds bounds,
        RainfallParameters parameters,
        double seconds, RainfallTravel? travel = null, RainfallEmission? emission = null)
    {
        parameters = parameters.Normalize();
        if (!bounds.IsValid || !double.IsFinite(seconds) || parameters.Amount == 0 || parameters.Opacity == 0)
        {
            return [];
        }

        // サイズや回転のAnimationで配置が跳ばないよう、許容する最大寸法から固定余白を取ります。
        // 粒は中心基準。最大2倍の水滴と縁を含めても、この半径内に収まります。
        var margin = parameters.Shape == RainfallShape.Streak
            ? RainfallParameters.MaximumLength * 1.4 + RainfallParameters.MaximumThickness * 2
            : (parameters.Shape == RainfallShape.Png && parameters.PngSourceSize > 0
                ? parameters.ParticleSizeLimit : RainfallParameters.MaximumParticleSize) * 2 + 2;
        var spanX = bounds.Width + margin * 2;
        var spanY = bounds.Height + margin * 2;
        // 大きな余白で画面内の粒密度が減らないよう、粒形状では画面外の粒も補います。
        var populationArea = parameters.Shape == RainfallShape.Streak ? bounds.Width * bounds.Height : spanX * spanY;
        var count = Math.Clamp(
            (int)Math.Ceiling(populationArea / FullHdArea * parameters.Amount * 10),
            1,
            MaximumStrokeCount);
        var strokes = new List<RainfallStroke>(count);
        var radians = ((parameters.Angle % 360 + 360) % 360) * Math.PI / 180;
        var directionX = Math.Sin(radians);
        var directionY = Math.Cos(radians);

        for (var index = 0; index < count; index++)
        {
            var randomBirth = UnitRandom(index, parameters.Seed, 4);
            // 出生時刻の累積割合を経過率の2乗にし、少ない粒から徐々に増やします。
            // 描画中の粒を間引かず、追加する粒も画面外で出生させます。
            var birth = parameters.StartSeconds + (parameters.RampEnabled
                ? Math.Sqrt(randomBirth) * parameters.RampSeconds
                : randomBirth * parameters.AppearanceSeconds);
            if (parameters.OnsetEnabled && seconds < birth)
            {
                continue;
            }
            var depth = 0.6 + UnitRandom(index, parameters.Seed, 2) * 0.8;
            var length = parameters.Length * depth;
            var thickness = parameters.Thickness * (1 + (depth - 1) * parameters.ThicknessVariation / 40);
            var size = parameters.ParticleSize * (1 + (depth - 1) * parameters.ThicknessVariation / 40);
            var speed = parameters.Speed * (1 + (depth - 1) * parameters.SpeedVariation / 40);
            var originX = bounds.Left - margin + UnitRandom(index, parameters.Seed, 0) * spanX;
            var originY = bounds.Top - margin + UnitRandom(index, parameters.Seed, 1) * spanY;
            var motionX = travel.HasValue
                ? travel.Value.X + (depth - 1) * travel.Value.VariationX
                : Travel(seconds, directionX * speed, spanX);
            var motionY = travel.HasValue
                ? travel.Value.Y + (depth - 1) * travel.Value.VariationY
                : Travel(seconds, directionY * speed, spanY);
            if (parameters.OnsetEnabled)
            {
                var birthAngle = emission?.AngleAt(birth) ?? parameters.Angle;
                var heading = RainfallTravel.Velocity(1, birthAngle);
                (originX, originY) = EntryPoint(bounds, heading.X, heading.Y,
                    UnitRandom(index, parameters.Seed, 0), UnitRandom(index, parameters.Seed, 1),
                    parameters.Shape == RainfallShape.Streak ? 9 : size + 1);
                if (travel.HasValue && emission is not null)
                {
                    var initial = emission.TravelAt(birth);
                    motionX -= initial.X + (depth - 1) * initial.VariationX;
                    motionY -= initial.Y + (depth - 1) * initial.VariationY;
                }
                else
                {
                    motionX = Travel(seconds - birth, directionX * speed, spanX);
                    motionY = Travel(seconds - birth, directionY * speed, spanY);
                }
            }
            // 揺れは進行方向の直角方向へ加え、回転や速度の積分には影響させません。
            var age = parameters.OnsetEnabled ? seconds - birth : seconds;
            if (parameters.SwayEnabled && parameters.Shape is RainfallShape.Bubble or RainfallShape.LensBubble or RainfallShape.Png)
            {
                var phase = UnitRandom(index, parameters.Seed, 5) * Math.Tau;
                var wave = Math.Sin((age % parameters.SwayPeriod) / parameters.SwayPeriod * Math.Tau + phase);
                // 出生時は変位0。最初の一周期で滑らかに振幅を増やします。
                var progress = parameters.OnsetEnabled ? Math.Clamp(age / parameters.SwayPeriod, 0, 1) : 1;
                var envelope = progress * progress * (3 - 2 * progress);
                var offset = parameters.SwayAmplitude * wave * envelope;
                motionX += directionY * offset;
                motionY -= directionX * offset;
            }
            var x = bounds.Left - margin + Wrap(originX - (bounds.Left - margin) + motionX, spanX);
            var y = bounds.Top - margin + Wrap(originY - (bounds.Top - margin) + motionY, spanY);
            var head = new Vector2((float)x, (float)y);
            var tail = new Vector2((float)(x - directionX * length), (float)(y - directionY * length));
            var opacity = parameters.Opacity / 100 * (1 - UnitRandom(index, parameters.Seed, 3) * parameters.OpacityVariation / 100);
            // 正の回転は画面上で時計回り。降り始め有効時は各粒の出生から回し始めます。
            var spin = parameters.RotationSpeed == 0 ? 0
                : (age % (360 / Math.Abs(parameters.RotationSpeed))) * parameters.RotationSpeed;
            var rotation = (parameters.RotationAngle % 360 + spin - (parameters.FollowDirection ? parameters.Angle % 360 : 0)) % 360;
            var flipHorizontal = false;
            if (parameters.KeepUpright && parameters.Shape != RainfallShape.Streak)
            {
                rotation = (rotation + 540) % 360 - 180;
                if (rotation > 90) { rotation -= 180; flipHorizontal = parameters.MirrorWhenUpright; }
                else if (rotation < -90) { rotation += 180; flipHorizontal = parameters.MirrorWhenUpright; }
            }
            strokes.Add(new RainfallStroke(tail, head, (float)Math.Max(0.01, thickness), (float)opacity, (float)Math.Max(0.01, size), (float)rotation, flipHorizontal,
                RainfallShapeVariants.Select(parameters, UnitRandom(index, parameters.Seed, 50)))
            {
                BlurVelocity = parameters.HasMotionBlur
                    ? new Vector2((float)(directionX * speed), (float)(directionY * speed)) + SwayVelocity()
                    : default,
            });

            Vector2 SwayVelocity()
            {
                if (!parameters.SwayEnabled || parameters.Shape is not (RainfallShape.Bubble or RainfallShape.LensBubble or RainfallShape.Png)) return default;
                var omega = Math.Tau / parameters.SwayPeriod;
                var phase = (age % parameters.SwayPeriod) * omega + UnitRandom(index, parameters.Seed, 5) * Math.Tau;
                var progress = parameters.OnsetEnabled ? Math.Clamp(age / parameters.SwayPeriod, 0, 1) : 1;
                var envelope = progress * progress * (3 - 2 * progress);
                var rise = progress is > 0 and < 1 ? 6 * progress * (1 - progress) / parameters.SwayPeriod : 0;
                var lateral = parameters.SwayAmplitude * (omega * Math.Cos(phase) * envelope + Math.Sin(phase) * rise);
                return new((float)(directionY * lateral), (float)(-directionX * lateral));
            }
        }

        return strokes.ToArray();
    }

    private static (double X, double Y) EntryPoint(
        RainfallBounds bounds, double directionX, double directionY, double randomX, double randomY, double padding)
    {
        // 最大の太さでも、出生フレームに入力領域内へ描画しない距離を取ります。

        var x = bounds.Left + randomX * bounds.Width;
        var y = bounds.Top + randomY * bounds.Height;
        var distanceX = Math.Abs(directionX) < 1e-12 ? double.PositiveInfinity
            : directionX > 0 ? (x - bounds.Left + padding) / directionX
            : (bounds.Right + padding - x) / -directionX;
        var distanceY = Math.Abs(directionY) < 1e-12 ? double.PositiveInfinity
            : directionY > 0 ? (y - bounds.Top + padding) / directionY
            : (bounds.Bottom + padding - y) / -directionY;
        var distance = Math.Min(distanceX, distanceY);
        return (x - directionX * distance, y - directionY * distance);
    }

    private static double Travel(double seconds, double velocity, double span)
    {
        if (velocity == 0)
        {
            return 0;
        }

        // 時刻と速度を直接乗算せず、長い動画でもオーバーフローを避けます。
        var period = span / Math.Abs(velocity);
        return double.IsFinite(period)
            ? Wrap(seconds, period) * velocity
            : seconds * velocity;
    }

    private static double Wrap(double value, double span)
    {
        var remainder = value % span;
        return remainder < 0 ? remainder + span : remainder;
    }

    private static double UnitRandom(int index, int seed, uint channel)
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
}
