// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Simulation;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace YMM4Rainfall.Rendering;

/// <summary>粒ごとの方向性ブラーを透明画像へ合成します。背景画像は入力にしません。</summary>
internal sealed class RainfallBlurRenderer : IDisposable
{
    private readonly ID2D1DeviceContext context;
    private readonly DirectionalBlur blur;
    private readonly ID2D1Image blurredImage;
    private readonly AffineTransform2D placement;
    private ID2D1Bitmap1? surface;
    private RainfallTrailingBlur? trailing;
    private bool disposed;

    internal int SurfaceCreationCount { get; private set; }
    internal int LastDrawnParticleCount { get; private set; }

    public RainfallBlurRenderer(ID2D1DeviceContext sourceContext)
    {
        using var device = sourceContext.Device;
        context = device.CreateDeviceContext(DeviceContextOptions.None);
        DirectionalBlur? createdBlur = null;
        ID2D1Image? createdImage = null;
        try
        {
            context.SetDpi(96, 96);
            createdBlur = new DirectionalBlur(context)
            {
                BorderMode = BorderMode.Soft,
                Optimization = DirectionalBlurOptimization.Balanced,
            };
            createdImage = createdBlur.Output;
            placement = new AffineTransform2D(context);
            blur = createdBlur;
            blurredImage = createdImage;
        }
        catch
        {
            createdImage?.Dispose();
            createdBlur?.Dispose();
            context.Dispose();
            throw;
        }
    }

    public ID2D1Image Render(RainfallBounds bounds, ReadOnlySpan<RainfallStroke> strokes,
        RainfallParameters parameters, ID2D1DeviceContext recordingContext, Action<RainfallStroke> drawStroke)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var left = Math.Floor(bounds.Left);
        var top = Math.Floor(bounds.Top);
        var width = Math.Ceiling(bounds.Right) - left;
        var height = Math.Ceiling(bounds.Bottom) - top;
        if (!bounds.IsValid || width > context.MaximumBitmapSize || height > context.MaximumBitmapSize)
            throw new InvalidOperationException("モーションブラーの描画範囲がデバイスの画像サイズ上限を超えています。");

        EnsureSurface((int)width, (int)height);
        LastDrawnParticleCount = 0;
        context.Target = surface;
        context.Transform = Matrix3x2.CreateTranslation(-(float)left, -(float)top);
        context.BeginDraw();
        try
        {
            context.Clear(new Color4(0, 0, 0, 0));
            context.PushAxisAlignedClip(new((float)bounds.Left, (float)bounds.Top,
                (float)bounds.Right, (float)bounds.Bottom), AntialiasMode.Aliased);
            try
            {
                foreach (var stroke in strokes)
                {
                    var sigma = StandardDeviation(stroke, parameters);
                    if (stroke.Opacity <= 0 || !Intersects(bounds, stroke, parameters.Shape, sigma)) continue;
                    using var particle = RecordParticle(recordingContext, stroke, drawStroke);
                    if (sigma <= 0)
                        context.DrawImage(particle);
                    else if (parameters.MotionBlurMode == MotionBlurMode.Trailing)
                    {
                        trailing ??= new RainfallTrailingBlur(context);
                        var tail = trailing.Apply(particle, stroke.Head, stroke.BlurVelocity, sigma);
                        context.DrawImage(tail);
                    }
                    else
                    {
                        blur.StandardDeviation = sigma;
                        // Direct2Dの角度はX軸から反時計回りです。画面座標のYは下向きです。
                        blur.Angle = (float)(Math.Atan2(-stroke.BlurVelocity.Y, stroke.BlurVelocity.X) * 180 / Math.PI);
                        blur.SetInput(0, particle, true);
                        context.DrawImage(blurredImage);
                    }
                    LastDrawnParticleCount++;
                }
            }
            finally { context.PopAxisAlignedClip(); }
        }
        finally
        {
            try { context.EndDraw().CheckError(); }
            finally
            {
                context.Target = null;
                context.Transform = Matrix3x2.Identity;
                blur.SetInput(0, null, true);
                trailing?.ClearInput();
            }
        }
        placement.TransformMatrix = Matrix3x2.CreateTranslation((float)left, (float)top);
        return placement.Output;
    }

    // 強さ100%は2/15秒分の移動距離を6σへ対応させます。FPSでぼけ幅を変えません。
    internal static float StandardDeviation(RainfallStroke stroke, RainfallParameters parameters)
    {
        if (!parameters.HasMotionBlur || !float.IsFinite(stroke.BlurVelocity.X) || !float.IsFinite(stroke.BlurVelocity.Y)) return 0;
        var speed = Math.Sqrt((double)stroke.BlurVelocity.X * stroke.BlurVelocity.X + (double)stroke.BlurVelocity.Y * stroke.BlurVelocity.Y);
        var sigma = speed * parameters.MotionBlurStrength / 100 / 45;
        // 既存の循環領域を変えず、画面に見える途中で粒が折り返さない範囲に収めます。
        var margin = parameters.Shape == RainfallShape.Streak
            ? RainfallParameters.MaximumLength * 1.4 + RainfallParameters.MaximumThickness * 2
            : (parameters.Shape == RainfallShape.Png && parameters.PngSourceSize > 0
                ? parameters.ParticleSizeLimit : RainfallParameters.MaximumParticleSize) * 2 + 2;
        var support = parameters.Shape == RainfallShape.Streak
            ? Vector2.Distance(stroke.Tail, stroke.Head) + stroke.Thickness
            : ParticleRadius(stroke, parameters.Shape);
        return (float)Math.Clamp(sigma, 0, Math.Min(32, Math.Max(0, margin - support - 2) / 3));
    }

    private static float ParticleRadius(RainfallStroke stroke, RainfallShape shape)
        => stroke.Size * (shape == RainfallShape.CartoonDrop ? 1 : 0.72f) + 1;

    private static bool Intersects(RainfallBounds bounds, RainfallStroke stroke, RainfallShape shape, float sigma)
    {
        var radius = (shape == RainfallShape.Streak ? stroke.Thickness : ParticleRadius(stroke, shape)) + sigma * 3 + 1;
        var tail = shape == RainfallShape.Streak ? stroke.Tail : stroke.Head;
        return Math.Max(tail.X, stroke.Head.X) + radius >= bounds.Left &&
            Math.Min(tail.X, stroke.Head.X) - radius <= bounds.Right &&
            Math.Max(tail.Y, stroke.Head.Y) + radius >= bounds.Top &&
            Math.Min(tail.Y, stroke.Head.Y) - radius <= bounds.Bottom;
    }

    private static ID2D1CommandList RecordParticle(ID2D1DeviceContext context, RainfallStroke stroke,
        Action<RainfallStroke> drawStroke)
    {
        var commands = context.CreateCommandList();
        try
        {
            context.Target = commands;
            context.Transform = Matrix3x2.Identity;
            context.AntialiasMode = AntialiasMode.PerPrimitive;
            context.BeginDraw();
            try { drawStroke(stroke); }
            finally { context.EndDraw().CheckError(); }
            context.Target = null;
            commands.Close();
            return commands;
        }
        catch
        {
            commands.Dispose();
            throw;
        }
        finally
        {
            context.Target = null;
            context.Transform = Matrix3x2.Identity;
        }
    }

    private void EnsureSurface(int width, int height)
    {
        if (surface is not null && surface.PixelSize.Width == width && surface.PixelSize.Height == height) return;
        var replacement = context.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96, 96, BitmapOptions.Target));
        try { placement.SetInput(0, replacement, true); }
        catch { replacement.Dispose(); throw; }
        surface?.Dispose();
        surface = replacement;
        SurfaceCreationCount++;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        trailing?.Dispose();
        blur.SetInput(0, null, true);
        placement.SetInput(0, null, true);
        placement.Dispose();
        blurredImage.Dispose();
        blur.Dispose();
        surface?.Dispose();
        context.Dispose();
    }
}
