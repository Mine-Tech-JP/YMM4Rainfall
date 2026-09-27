// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Mathematics;
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall.Rendering;

/// <summary>専用の描画コンテキストで雨筋を記録し、入力に重ねます。入力画像は借用します。</summary>
internal sealed class RainfallRenderer : IDisposable
{
    private readonly ID2D1DeviceContext drawingContext;
    private ID2D1GradientStopCollection gradientStops;
    private ID2D1LinearGradientBrush brush;
    private (double R, double G, double B) tint = (1, 1, 1);
    private readonly ID2D1Image emptyImage;
    private readonly Composite composite;
    private readonly ID2D1Image output;
    private ID2D1Image? rainImage;
    private RainfallBlurRenderer? motionBlur;
    internal int MotionBlurSurfaceCreationCount => motionBlur?.SurfaceCreationCount ?? 0;
    internal int MotionBlurDrawnParticleCount => motionBlur?.LastDrawnParticleCount ?? 0;
    private ID2D1Image? input;
    private bool disposed;
    private readonly RainfallPng png = new();
    private readonly SnowGraphicsCache snow;
    public string PngStatus => png.Status;
    internal int SnowCacheEntryCount => snow.CachedImageCount;
    internal int SnowCacheCreationCount => snow.CreationCount;

    private RainfallRenderer(
        ID2D1DeviceContext drawingContext,
        ID2D1GradientStopCollection gradientStops,
        ID2D1LinearGradientBrush brush,
        ID2D1Image emptyImage,
        Composite composite,
        ID2D1Image output)
    {
        this.drawingContext = drawingContext;
        this.gradientStops = gradientStops;
        this.brush = brush;
        this.emptyImage = emptyImage;
        this.composite = composite;
        this.output = output;
        snow = new SnowGraphicsCache(drawingContext);
    }

    public ID2D1Image Output
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return output;
        }
    }

    public static RainfallRenderer Create(ID2D1DeviceContext hostContext)
    {
        ArgumentNullException.ThrowIfNull(hostContext);
        ID2D1DeviceContext? context = null;
        ID2D1GradientStopCollection? stops = null;
        ID2D1LinearGradientBrush? createdBrush = null;
        ID2D1Image? empty = null;
        Composite? createdComposite = null;
        ID2D1Image? createdOutput = null;
        try
        {
            using var device = hostContext.Device;
            context = device.CreateDeviceContext(DeviceContextOptions.None);
            context.SetDpi(96, 96);
            stops = context.CreateGradientStopCollection(
                [
                    new GradientStop(0, new Color4(1, 1, 1, 0)),
                    new GradientStop(0.7f, new Color4(1, 1, 1, 0.9f)),
                    new GradientStop(1, new Color4(1, 1, 1, 0.2f)),
                ]);
            createdBrush = context.CreateLinearGradientBrush(
                new LinearGradientBrushProperties(Vector2.Zero, Vector2.UnitY), stops);
            empty = Record(context, createdBrush, default, []);
            createdComposite = new Composite(hostContext) { Mode = CompositeMode.SourceOver };
            createdComposite.SetInput(0, empty, true);
            createdComposite.SetInput(1, empty, true);
            createdOutput = createdComposite.Output;
            return new RainfallRenderer(context, stops, createdBrush, empty, createdComposite, createdOutput);
        }
        catch
        {
            createdComposite?.SetInput(0, null, true);
            createdComposite?.SetInput(1, null, true);
            createdOutput?.Dispose();
            createdComposite?.Dispose();
            empty?.Dispose();
            createdBrush?.Dispose();
            stops?.Dispose();
            context?.Dispose();
            throw;
        }
    }

    public void SetInput(ID2D1Image? image)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        composite.SetInput(0, image ?? emptyImage, true);
        input = image;
        ClearRain();
    }

    public void Render(RainfallParameters parameters, double seconds, RainfallTravel? travel = null, RainfallEmission? emission = null, string pngPath = "", Func<RainfallBounds, RainfallStroke[]>? frameFactory = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (input is null)
        {
            ClearRain();
            return;
        }

        parameters = parameters.Normalize();
        var pngBitmap = parameters.Shape == RainfallShape.Png ? png.Update(drawingContext, pngPath, parameters) : null;
        if (parameters.Shape == RainfallShape.Png && pngBitmap is null)
        {
            ClearRain();
            return;
        }
        if (pngBitmap is not null)
            parameters = (parameters with { PngSourceSize = Math.Max(pngBitmap.PixelSize.Width, pngBitmap.PixelSize.Height) }).Normalize();
        var rawBounds = drawingContext.GetImageLocalBounds(input);
        var bounds = new RainfallBounds(rawBounds.Left, rawBounds.Top, rawBounds.Right, rawBounds.Bottom);
        var strokes = frameFactory?.Invoke(bounds) ?? RainfallSimulation.CreateFrame(bounds, parameters, seconds, travel, emission);
        if (strokes.Length == 0)
        {
            ClearRain();
            return;
        }

        UpdateColor(parameters);
        snow.Prepare(parameters, strokes);
        using var background = parameters.Shape == RainfallShape.LensBubble
            ? RainfallBackgroundSnapshot.Capture(drawingContext, input, bounds) : null;
        if (parameters.HasMotionBlur) motionBlur ??= new RainfallBlurRenderer(drawingContext);
        ID2D1Image? candidate = Record(drawingContext, brush, bounds, strokes, parameters, pngBitmap,
            png.WidthRatio, png.HeightRatio, background, snow, parameters.HasMotionBlur ? motionBlur : null);
        try
        {
            composite.SetInput(1, candidate, true);
            var previousImage = rainImage;
            rainImage = candidate;
            candidate = null;
            previousImage?.Dispose();
        }
        finally
        {
            candidate?.Dispose();
        }
    }

    private void UpdateColor(RainfallParameters parameters)
    {
        var next = (parameters.Red, parameters.Green, parameters.Blue);
        if (tint == next) return;
        var r = (float)parameters.Red;
        var g = (float)parameters.Green;
        var b = (float)parameters.Blue;
        var stops = drawingContext.CreateGradientStopCollection([
            new GradientStop(0, new Color4(r, g, b, 0)),
            new GradientStop(0.7f, new Color4(r, g, b, 0.9f)),
            new GradientStop(1, new Color4(r, g, b, 0.2f)),
        ]);
        ID2D1LinearGradientBrush candidate;
        try
        {
            candidate = drawingContext.CreateLinearGradientBrush(
                new LinearGradientBrushProperties(Vector2.Zero, Vector2.UnitY), stops);
        }
        catch
        {
            stops.Dispose();
            throw;
        }
        brush.Dispose();
        gradientStops.Dispose();
        brush = candidate;
        gradientStops = stops;
        tint = next;
    }

    public void ClearRain()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        composite.SetInput(1, emptyImage, true);
        rainImage?.Dispose();
        rainImage = null;
    }

    private static ID2D1Image Record(
        ID2D1DeviceContext context,
        ID2D1LinearGradientBrush gradientBrush,
        RainfallBounds bounds,
        ReadOnlySpan<RainfallStroke> strokes, RainfallParameters parameters = default, ID2D1Bitmap1? pngBitmap = null, float pngWidth = 1, float pngHeight = 1, RainfallBackgroundSnapshot? background = null, SnowGraphicsCache? snow = null, RainfallBlurRenderer? motionBlur = null)
    {
        using var body = parameters.Shape == RainfallShape.Streak ? null
            : context.CreateSolidColorBrush(new Color4((float)parameters.Red, (float)parameters.Green, (float)parameters.Blue, 1));
        using var outline = parameters.Shape is RainfallShape.CartoonDrop or RainfallShape.Bubble or RainfallShape.LensBubble
            ? context.CreateSolidColorBrush(new Color4(0.04f, 0.06f, 0.08f, 1)) : null;
        using var highlight = parameters.Shape is RainfallShape.CartoonDrop or RainfallShape.Bubble or RainfallShape.LensBubble
            ? context.CreateSolidColorBrush(new Color4(1, 1, 1, 1)) : null;
        using var geometry = parameters.Shape == RainfallShape.CartoonDrop ? CreateDroplet(context) : null;
        using var bubbleHighlight = parameters.Shape == RainfallShape.Bubble ? CreateBubbleHighlight(context) : null;
        using var reflection = parameters.Shape == RainfallShape.LensBubble && background is not null
            ? context.CreateImageBrush(background.Bitmap, new ImageBrushProperties(
                new Rect(0, 0, background.Bitmap.PixelSize.Width, background.Bitmap.PixelSize.Height),
                ExtendMode.Clamp, ExtendMode.Clamp, InterpolationMode.Linear), null) : null;
        using var glowStops = parameters.Shape == RainfallShape.LensBubble ? context.CreateGradientStopCollection([
            new GradientStop(0, new Color4(1, 1, 1, 0.85f)),
            new GradientStop(1, new Color4(1, 1, 1, 0)),
        ]) : null;
        using var glow = glowStops is null ? null : context.CreateRadialGradientBrush(
            new RadialGradientBrushProperties(new Vector2(-0.23f, -0.28f), Vector2.Zero, 0.15f, 0.08f), glowStops);
        if (motionBlur is not null)
            return motionBlur.Render(bounds, strokes, parameters, context, DrawStroke);

        var commands = context.CreateCommandList();
        try
        {
            context.Target = commands;
            context.Transform = Matrix3x2.Identity;
            context.AntialiasMode = AntialiasMode.PerPrimitive;
            context.BeginDraw();
            try
            {
                if (bounds.IsValid)
                {
                    context.PushAxisAlignedClip(
                        new RawRectF((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom),
                        AntialiasMode.Aliased);
                    try
                    {
                        foreach (var stroke in strokes) DrawStroke(stroke);
                    }
                    finally
                    {
                        context.PopAxisAlignedClip();
                    }
                }
            }
            finally
            {
                context.EndDraw().CheckError();
            }

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
        }

        void DrawStroke(RainfallStroke stroke)
        {
            gradientBrush.StartPoint = stroke.Tail;
            gradientBrush.EndPoint = stroke.Head;
            gradientBrush.Opacity = stroke.Opacity;
            if (parameters.Shape == RainfallShape.Streak)
            {
                context.DrawLine(stroke.Tail, stroke.Head, gradientBrush, stroke.Thickness);
                return;
            }
            var size = stroke.Size;
            if (reflection is not null)
            {
                // 背景は泡自身の回転に追従させず、現在位置の周囲を反転・縮小します。
                context.Transform = Matrix3x2.Identity;
                reflection.Transform = background!.ToWorld * Matrix3x2.CreateTranslation(-stroke.Head)
                    * Matrix3x2.CreateScale(-0.6f) * Matrix3x2.CreateTranslation(stroke.Head);
                reflection.Opacity = stroke.Opacity * (float)parameters.LensReflection / 100;
                context.FillEllipse(new Ellipse(stroke.Head, size * 0.485f, size * 0.485f), reflection);
            }
            // 水滴の既存パスを中心基準へ移し、拡大と回転を粒の中心に適用します。
            var localOffset = parameters.Shape == RainfallShape.CartoonDrop ? 0.8f : 0;
            context.Transform = Matrix3x2.CreateTranslation(0, localOffset)
                * Matrix3x2.CreateScale(stroke.FlipHorizontal ? -size : size, size)
                * Matrix3x2.CreateRotation(stroke.Rotation * MathF.PI / 180)
                * Matrix3x2.CreateTranslation(stroke.Head);
            if (parameters.Shape == RainfallShape.Png)
            {
                context.DrawBitmap(pngBitmap!, new RawRectF(-pngWidth / 2, -pngHeight / 2, pngWidth / 2, pngHeight / 2),
                    stroke.Opacity, BitmapInterpolationMode.Linear, null);
                context.Transform = Matrix3x2.Identity;
                return;
            }
            if (parameters.Shape is RainfallShape.SnowRound or RainfallShape.SnowClump or RainfallShape.SnowCrystal)
            {
                context.DrawBitmap(snow!.Get(parameters.Shape, stroke.Variant),
                    new RawRectF(-0.5f, -0.5f, 0.5f, 0.5f), stroke.Opacity,
                    BitmapInterpolationMode.Linear, null);
                context.Transform = Matrix3x2.Identity;
                return;
            }
            body!.Opacity = stroke.Opacity;
            if (parameters.Shape == RainfallShape.Circle)
            {
                context.FillEllipse(new Ellipse(Vector2.Zero, 0.5f, 0.5f), body);
            }
            else if (parameters.Shape == RainfallShape.LensBubble)
            {
                body.Opacity = stroke.Opacity * 0.025f;
                context.FillEllipse(new Ellipse(Vector2.Zero, 0.485f, 0.485f), body);
                body.Opacity = stroke.Opacity * 0.45f * (float)parameters.OutlineOpacity / 100;
                context.DrawEllipse(new Ellipse(Vector2.Zero, 0.48f, 0.48f), body, 0.015f);
                outline!.Opacity = stroke.Opacity * 0.15f * (float)parameters.OutlineOpacity / 100;
                context.DrawEllipse(new Ellipse(Vector2.Zero, 0.492f, 0.492f), outline, 0.016f);
                glow!.Opacity = stroke.Opacity;
                context.FillEllipse(new Ellipse(new Vector2(-0.23f, -0.28f), 0.15f, 0.08f), glow);
                highlight!.Opacity = stroke.Opacity * 0.3f;
                context.FillEllipse(new Ellipse(new Vector2(0.3f, 0.26f), 0.035f, 0.025f), highlight);
            }
            else if (parameters.Shape == RainfallShape.Bubble)
            {
                // 中央は塗らず、二重の輪郭と反射光だけで泡を表現します。
                outline!.Opacity = stroke.Opacity * 0.35f * (float)parameters.OutlineOpacity / 100;
                context.DrawEllipse(new Ellipse(Vector2.Zero, 0.485f, 0.485f), outline, 0.03f);
                body.Opacity = stroke.Opacity * (float)parameters.OutlineOpacity / 100;
                context.DrawEllipse(new Ellipse(Vector2.Zero, 0.475f, 0.475f), body, 0.02f);
                highlight!.Opacity = stroke.Opacity;
                context.DrawGeometry(bubbleHighlight!, highlight, 0.04f);
                highlight.Opacity = stroke.Opacity * 0.7f;
                context.FillEllipse(new Ellipse(new Vector2(0.27f, 0.27f), 0.035f, 0.035f), highlight);
            }
            else
            {
                context.FillGeometry(geometry!, body);
                outline!.Opacity = stroke.Opacity * 0.8f;
                context.DrawGeometry(geometry!, outline, 0.06f);
                highlight!.Opacity = stroke.Opacity * 0.9f;
                context.FillEllipse(new Ellipse(new Vector2(-0.18f, -0.6f), 0.09f, 0.19f), highlight);
            }
            context.Transform = Matrix3x2.Identity;
        }
    }

    private static ID2D1PathGeometry CreateBubbleHighlight(ID2D1DeviceContext context)
    {
        using var factory = context.Factory;
        var geometry = factory.CreatePathGeometry();
        try
        {
            using var sink = geometry.Open();
            sink.BeginFigure(new Vector2(-0.36f, -0.18f), FigureBegin.Hollow);
            sink.AddBezier(new BezierSegment(new Vector2(-0.30f, -0.33f), new Vector2(-0.18f, -0.39f), new Vector2(-0.08f, -0.395f)));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
            return geometry;
        }
        catch { geometry.Dispose(); throw; }
    }

    private static ID2D1PathGeometry CreateDroplet(ID2D1DeviceContext context)
    {
        using var factory = context.Factory;
        var geometry = factory.CreatePathGeometry();
        try
        {
            using var sink = geometry.Open();
            sink.BeginFigure(new Vector2(0, -1.6f), FigureBegin.Filled);
            sink.AddBezier(new BezierSegment(new Vector2(-0.12f, -1.1f), new Vector2(-0.5f, -0.85f), new Vector2(-0.5f, -0.5f)));
            sink.AddBezier(new BezierSegment(new Vector2(-0.5f, 0.16f), new Vector2(0.5f, 0.16f), new Vector2(0.5f, -0.5f)));
            sink.AddBezier(new BezierSegment(new Vector2(0.5f, -0.85f), new Vector2(0.12f, -1.1f), new Vector2(0, -1.6f)));
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
            return geometry;
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        input = null;
        try
        {
            composite.SetInput(0, null, true);
            composite.SetInput(1, null, true);
        }
        finally
        {
            motionBlur?.Dispose();
            snow.Dispose();
            png.Dispose();
            rainImage?.Dispose();
            output.Dispose();
            composite.Dispose();
            emptyImage.Dispose();
            brush.Dispose();
            gradientStops.Dispose();
            drawingContext.Dispose();
        }
    }
}
