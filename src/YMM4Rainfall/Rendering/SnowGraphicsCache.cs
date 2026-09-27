// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Simulation;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace YMM4Rainfall.Rendering;

/// <summary>雪の形と柔らかさを描画済み画像として保持し、粒ごとのぼかし処理を避けます。</summary>
internal sealed class SnowGraphicsCache : IDisposable
{
    private const int ImageSize = 128;
    private readonly ID2D1DeviceContext context;
    private readonly Dictionary<(RainfallShape Shape, int Variant), ID2D1Bitmap1> images = [];
    private (double Red, double Green, double Blue, double Softness)? configuration;
    private bool disposed;

    public int CachedImageCount => images.Count;
    public int CreationCount { get; private set; }
    public bool IsDisposed => disposed;

    public SnowGraphicsCache(ID2D1DeviceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public void Prepare(RainfallParameters parameters, ReadOnlySpan<RainfallStroke> strokes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsSnow(parameters.Shape))
        {
            return;
        }

        var softness = parameters.Shape is RainfallShape.SnowRound or RainfallShape.SnowClump
            ? parameters.SnowSoftness : 0;
        var next = (parameters.Red, parameters.Green, parameters.Blue, softness);
        if (configuration != next)
        {
            Clear();
            configuration = next;
        }

        Span<bool> needed = stackalloc bool[RainfallShapeVariants.Count(parameters.Shape)];
        foreach (var stroke in strokes)
        {
            needed[NormalizeVariant(parameters.Shape, stroke.Variant)] = true;
        }

        for (var variant = 0; variant < needed.Length; variant++)
        {
            var key = (parameters.Shape, variant);
            if (needed[variant] && !images.ContainsKey(key))
            {
                images.Add(key, CreateImage(parameters, variant, softness));
                CreationCount++;
            }
        }
    }

    public ID2D1Bitmap1 Get(RainfallShape shape, int variant)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return images[(shape, NormalizeVariant(shape, variant))];
    }

    private ID2D1Bitmap1 CreateImage(RainfallParameters parameters, int variant, double softness)
    {
        var source = CreateTargetBitmap();
        try
        {
            DrawSource(source, parameters.Shape, variant, parameters);
            if (softness <= 0 || parameters.Shape is not (RainfallShape.SnowRound or RainfallShape.SnowClump))
            {
                return source;
            }

            var result = CreateTargetBitmap();
            try
            {
                using var blur = new GaussianBlur(context)
                {
                    StandardDeviation = (float)(0.5 + softness * 0.075),
                    Optimization = GaussianBlurOptimization.Speed,
                    BorderMode = BorderMode.Soft,
                };
                blur.SetInput(0, source, true);
                using var blurred = blur.Output;
                DrawImageToBitmap(result, blurred);
                blur.SetInput(0, null, true);
                source.Dispose();
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private void DrawSource(ID2D1Bitmap1 target, RainfallShape shape, int variant, RainfallParameters parameters)
    {
        // 柔らかくするほど中心も少し薄くし、広がった縁との濃淡差を保ちます。
        var alpha = shape is RainfallShape.SnowRound or RainfallShape.SnowClump
            ? (float)(1 - parameters.SnowSoftness * 0.004) : 1;
        using var brush = context.CreateSolidColorBrush(new Color4(
            (float)parameters.Red, (float)parameters.Green, (float)parameters.Blue, alpha));
        using var previousTarget = context.Target;
        var previousTransform = context.Transform;
        var previousAntialias = context.AntialiasMode;
        context.Target = target;
        context.Transform = Matrix3x2.Identity;
        context.AntialiasMode = AntialiasMode.PerPrimitive;
        var drawing = false;
        try
        {
            context.BeginDraw();
            drawing = true;
            context.Clear(new Color4(0, 0, 0, 0));
            DrawShape(shape, variant, parameters.SnowSoftness, brush);
            var result = context.EndDraw();
            drawing = false;
            result.CheckError();
        }
        catch
        {
            if (drawing)
            {
                try { context.EndDraw(); } catch { }
            }
            throw;
        }
        finally
        {
            context.Target = previousTarget;
            context.Transform = previousTransform;
            context.AntialiasMode = previousAntialias;
        }
    }

    private void DrawShape(RainfallShape shape, int variant, double softness, ID2D1SolidColorBrush brush)
    {
        var center = new Vector2(ImageSize / 2f, ImageSize / 2f);
        switch (shape)
        {
            case RainfallShape.SnowRound:
                var softScale = (float)(1 - softness * 0.0015);
                var softWidth = (48 + variant) * softScale;
                var softHeight = (50 - variant) * softScale;
                context.FillEllipse(new Ellipse(center + FineOffset(variant), softWidth, softHeight), brush);
                break;
            case RainfallShape.SnowClump:
                DrawClump(center, variant, (float)(1 - softness * 0.0015), brush);
                break;
            case RainfallShape.SnowCrystal:
                SnowCrystalDrawing.Draw(context, center, variant, brush);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "雪の形状ではありません。");
        }
    }

    private static Vector2 FineOffset(int variant) => variant switch
    {
        0 => new Vector2(-1, 1),
        1 => new Vector2(1.5f, -0.5f),
        2 => new Vector2(0, 1.5f),
        _ => new Vector2(-1.5f, -1),
    };

    private void DrawClump(Vector2 center, int variant, float scale, ID2D1SolidColorBrush brush)
    {
        ReadOnlySpan<(float X, float Y, float Radius)> lobes = variant switch
        {
            0 => [(-18, -8, 23), (12, -15, 26), (20, 15, 22), (-12, 18, 25), (0, 0, 27)],
            1 => [(-22, -2, 21), (2, -20, 25), (23, -4, 20), (13, 20, 24), (-15, 17, 22), (0, 0, 25)],
            2 => [(-17, -17, 22), (17, -12, 25), (21, 16, 21), (-4, 21, 26), (-23, 6, 20), (0, 0, 27)],
            _ => [(-23, -12, 20), (1, -20, 27), (22, -6, 22), (18, 21, 21), (-13, 20, 26), (-3, 1, 28)],
        };
        foreach (var lobe in lobes)
        {
            context.FillEllipse(new Ellipse(center + new Vector2(lobe.X, lobe.Y) * scale,
                lobe.Radius * scale, lobe.Radius * scale), brush);
        }
    }

    private ID2D1Bitmap1 CreateTargetBitmap() => context.CreateBitmap(
        new SizeI(ImageSize, ImageSize),
        new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96, BitmapOptions.Target));

    private void DrawImageToBitmap(ID2D1Bitmap1 target, ID2D1Image image)
    {
        using var previousTarget = context.Target;
        var previousTransform = context.Transform;
        var previousAntialias = context.AntialiasMode;
        context.Target = target;
        context.Transform = Matrix3x2.Identity;
        context.AntialiasMode = AntialiasMode.PerPrimitive;
        var drawing = false;
        try
        {
            context.BeginDraw();
            drawing = true;
            context.Clear(new Color4(0, 0, 0, 0));
            context.DrawImage(image);
            var result = context.EndDraw();
            drawing = false;
            result.CheckError();
        }
        catch
        {
            if (drawing)
            {
                try { context.EndDraw(); } catch { }
            }
            throw;
        }
        finally
        {
            context.Target = previousTarget;
            context.Transform = previousTransform;
            context.AntialiasMode = previousAntialias;
        }
    }

    private static bool IsSnow(RainfallShape shape) => shape is
        RainfallShape.SnowRound or RainfallShape.SnowClump or RainfallShape.SnowCrystal;

    private static int NormalizeVariant(RainfallShape shape, int variant)
    {
        var count = RainfallShapeVariants.Count(shape);
        return (variant % count + count) % count;
    }

    private void Clear()
    {
        foreach (var image in images.Values)
        {
            image.Dispose();
        }
        images.Clear();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Clear();
    }
}
