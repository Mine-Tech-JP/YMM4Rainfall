// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;

namespace YMM4Rainfall.Rendering;

/// <summary>現在位置の像と、移動方向の後方だけへ減衰するぼかしを合成します。</summary>
internal sealed class RainfallTrailingBlur : IDisposable
{
    private readonly AffineTransform2D align;
    private readonly ConvolveMatrix convolution;
    private readonly AffineTransform2D restore;
    private readonly ID2D1Image output;
    // 重みは半径そのものではなく点数だけで決まります。2～97点の有限な範囲を再利用します。
    private readonly float[]?[] kernels = new float[98][];
    private int configuredCount;
    private float configuredSigma = -1;
    private bool disposed;

    public RainfallTrailingBlur(ID2D1DeviceContext context)
    {
        AffineTransform2D? createdAlign = null;
        ConvolveMatrix? createdConvolution = null;
        AffineTransform2D? createdRestore = null;
        ID2D1Image? createdOutput = null;
        try
        {
            createdAlign = new AffineTransform2D(context);
            createdConvolution = new ConvolveMatrix(context)
            {
                KernelSizeY = 1,
                PreserveAlpha = false,
                BorderMode = BorderMode.Soft,
                Divisor = 1,
                Bias = 0,
            };
            using var aligned = createdAlign.Output;
            createdConvolution.SetInput(0, aligned, true);
            createdRestore = new AffineTransform2D(context);
            using var convolved = createdConvolution.Output;
            createdRestore.SetInput(0, convolved, true);
            createdOutput = createdRestore.Output;
            align = createdAlign;
            convolution = createdConvolution;
            restore = createdRestore;
            output = createdOutput;
        }
        catch
        {
            createdOutput?.Dispose();
            createdRestore?.Dispose();
            createdConvolution?.Dispose();
            createdAlign?.Dispose();
            throw;
        }
    }

    /// <summary>所有する出力画像を貸し出します。呼出側では破棄しません。</summary>
    public ID2D1Image Apply(ID2D1Image input, Vector2 center, Vector2 velocity, float sigma)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Configure(sigma);
        var angle = MathF.Atan2(velocity.Y, velocity.X);
        align.TransformMatrix = Matrix3x2.CreateTranslation(-center) * Matrix3x2.CreateRotation(-angle);
        restore.TransformMatrix = Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(center);
        align.SetInput(0, input, true);
        return output;
    }

    private void Configure(float sigma)
    {
        if (configuredSigma == sigma) return;
        var radius = Math.Clamp(sigma * 3, 0.001f, 96);
        var count = Math.Clamp((int)Math.Ceiling(radius) + 1, 2, 97);
        if (configuredCount != count)
        {
            convolution.KernelSizeX = count;
            convolution.KernelOffset = new Vector2((count - 1) / 2f, 0);
            convolution.KernelMatrix = kernels[count] ??= CreateKernel(count);
            configuredCount = count;
        }
        // 参照SDKのfloatラッパーではなく、ネイティブの二次元単位長を渡します。
        convolution.SetValue((int)ConvolveMatrixProperties.KernelUnitLength, new Vector2(radius / (count - 1), 1));
        configuredSigma = sigma;
    }

    private static float[] CreateKernel(int count)
    {
        var weights = new float[count];
        var sum = 0f;
        for (var i = 0; i < count; i++)
        {
            var distance = i / (float)(count - 1);
            weights[i] = MathF.Exp(-4.5f * distance * distance);
            sum += weights[i];
        }
        for (var i = 0; i < count; i++) weights[i] *= 0.65f / sum;
        // 先頭の像を35%残し、残りを後方へ分配します。総量は1に保ちます。
        weights[0] += 0.35f;
        // ConvolveMatrixは畳み込みの向きで適用するため、現在位置に対応する末尾へ最大重みを置きます。
        Array.Reverse(weights);
        return weights;
    }

    public void ClearInput() => align.SetInput(0, null, true);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { ClearInput(); }
        finally
        {
            output.Dispose();
            restore.Dispose();
            convolution.Dispose();
            align.Dispose();
        }
    }
}
