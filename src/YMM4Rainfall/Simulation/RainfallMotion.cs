// SPDX-License-Identifier: MPL-2.0
namespace YMM4Rainfall.Simulation;

/// <summary>基準移動量と、粒の奥行き係数へ掛けるばらつき分の移動量です。</summary>
internal readonly record struct RainfallTravel(double X, double Y, double VariationX = 0, double VariationY = 0)
{
    public static RainfallTravel Velocity(double speed, double angle, double variation = 40)
    {
        speed = double.IsFinite(speed) ? Math.Clamp(speed, 0, RainfallParameters.MaximumSpeed) : RainfallParameters.DefaultSpeed;
        angle = double.IsFinite(angle) ? (angle % 360 + 360) % 360 : RainfallParameters.DefaultAngle;
        var radians = angle * Math.PI / 180;
        var x = Math.Sin(radians) * speed;
        var y = Math.Cos(radians) * speed;
        var scale = (double.IsFinite(variation) ? Math.Clamp(variation, 0, 100) : 40) / 40;
        return new(x, y, x * scale, y * scale);
    }

    public static RainfallTravel operator +(RainfallTravel a, RainfallTravel b) => new(a.X + b.X, a.Y + b.Y, a.VariationX + b.VariationX, a.VariationY + b.VariationY);
    public static RainfallTravel operator *(RainfallTravel a, double scale) => new(a.X * scale, a.Y * scale, a.VariationX * scale, a.VariationY * scale);
}

/// <summary>フレーム境界の速度を台形則で積分し、120フレームごとの移動量を保存します。</summary>
internal sealed class RainfallMotion
{
    private const int BlockSize = 120;
    private readonly List<RainfallTravel> checkpoints = [default];
    private string? definition;
    private readonly Dictionary<long, RainfallTravel> samples = [];

    public RainfallTravel Evaluate(long frame, int fps, string signature, Func<long, RainfallTravel> velocity)
    {
        if (fps <= 0 || frame <= 0) return default;
        if (definition != signature)
        {
            checkpoints.Clear();
            samples.Clear();
            checkpoints.Add(default);
            definition = signature;
        }
        if (samples.TryGetValue(frame, out var cached)) return cached;
        var block = checked((int)(frame / BlockSize));
        while (checkpoints.Count <= block)
        {
            var start = (long)(checkpoints.Count - 1) * BlockSize;
            checkpoints.Add(Integrate(checkpoints[^1], start, start + BlockSize, fps, velocity));
        }
        var result = Integrate(checkpoints[block], (long)block * BlockSize, frame, fps, velocity);
        // 粒の出現時刻は繰り返し使うため保持し、長尺の再生ではメモリ量を制限します。
        if (samples.Count >= 16384) samples.Clear();
        samples[frame] = result;
        return result;
    }

    public RainfallTravel EvaluateSeconds(double seconds, int fps, string signature, Func<long, RainfallTravel> velocity)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || fps <= 0) return default;
        var position = seconds * fps;
        var frame = checked((long)Math.Floor(position));
        var fraction = position - frame;
        var start = Evaluate(frame, fps, signature, velocity);
        if (fraction == 0) return start;
        // 同一フレーム内も台形則の速度補間を積分し、描画フレームとの連続性を保ちます。
        var first = velocity(frame);
        var last = velocity(frame + 1);
        return start + first * ((fraction - fraction * fraction / 2) / fps)
            + last * (fraction * fraction / 2 / fps);
    }

    private static RainfallTravel Integrate(RainfallTravel value, long from, long to, int fps, Func<long, RainfallTravel> velocity)
    {
        var previous = velocity(from);
        for (var frame = from; frame < to; frame++)
        {
            var next = velocity(frame + 1);
            value += (previous + next) * (0.5 / fps);
            previous = next;
        }
        return value;
    }
}
