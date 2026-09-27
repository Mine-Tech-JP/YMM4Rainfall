// SPDX-License-Identifier: MPL-2.0
using System.Diagnostics;
using Vortice.Direct2D1;
using YMM4Rainfall.Rendering;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM4Rainfall;

/// <summary>現在フレームの粒を描き、位置や拡大率などの描画情報はYMM4へそのまま返します。</summary>
internal sealed class RainfallEffectProcessor : IVideoEffectProcessor
{
    private readonly RainfallEffect item;
    private readonly ID2D1Bitmap emptyBitmap;
    private readonly RainfallRenderer? renderer;
    private ID2D1Image? input;
    private bool drawingFailed;
    private bool errorReported;
    private bool disposed;
    private readonly WeatherFrameSource frames = new();

    public RainfallEffectProcessor(IGraphicsDevicesAndContext devices, RainfallEffect item)
        : this(devices?.DeviceContext ?? throw new ArgumentNullException(nameof(devices)), item) { }

    internal RainfallEffectProcessor(ID2D1DeviceContext context, RainfallEffect item)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        this.item = item;
        emptyBitmap = context.CreateEmptyBitmap();
        try
        {
            renderer = RainfallRenderer.Create(context);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    public ID2D1Image Output
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return drawingFailed ? input ?? emptyBitmap : renderer?.Output ?? input ?? emptyBitmap;
        }
    }

    public void SetInput(ID2D1Image? input)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        this.input = input;
        drawingFailed = false;
        try
        {
            renderer?.SetInput(input);
        }
        catch (Exception exception)
        {
            HandleDrawingFailure(exception);
        }
    }

    public void ClearInput() => SetInput(null);

    public DrawDescription Update(EffectDescription effectDescription)
    {
        UpdateFrame(effectDescription.ItemPosition.Frame, effectDescription.ItemDuration.Frame, effectDescription.FPS);
        return effectDescription.DrawDescription;
    }

    internal void UpdateFrame(long frame, long duration, int fps)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (renderer is not null && !drawingFailed && input is not null)
        {
            try
            {
                if (!item.IsSupported)
                {
                    renderer.ClearRain();
                    return;
                }
                var seconds = RainfallSimulation.GetSeconds(frame, fps);
                var signature = item.MotionSignature(duration, fps);
                var pngPath = string.Empty;
                var pngSourceSize = 0;
                if (item.IsPng)
                {
                    try
                    {
                        var builtIn = item.PngImageId == RainfallBuiltInImage.PigId;
                        pngPath = builtIn ? RainfallBuiltInImage.PigPath : RainfallImageLibrary.Shared.Resolve(item.PngImageId);
                        var entry = builtIn ? RainfallBuiltInImage.PigEntry
                            : RainfallImageLibrary.Shared.Load().Single(image => image.Id == item.PngImageId);
                        pngSourceSize = Math.Max(entry.Width, entry.Height);
                        signature += "|" + entry.Hash;
                    }
                    catch (Exception exception)
                    {
                        renderer.ClearRain();
                        item.SetPngStatus(RainfallImageLibrary.ErrorText(exception));
                        return;
                    }
                }
                var parameters = item.GetParameters(frame, duration, fps, pngSourceSize);
                renderer.Render(parameters, seconds, pngPath: pngPath,
                    frameFactory: bounds => frames.Evaluate(bounds, frame, duration, fps, signature,
                        sampleFrame => item.GetParameters(sampleFrame, duration, fps, pngSourceSize)));
                if (item.IsPng) item.SetPngStatus(renderer.PngStatus);
            }
            catch (Exception exception)
            {
                HandleDrawingFailure(exception);
            }
        }

    }

    private void HandleDrawingFailure(Exception exception)
    {
        drawingFailed = true;
        ReportError(exception);
        try
        {
            renderer?.ClearRain();
        }
        catch (Exception clearException)
        {
            WriteDiagnostic("降雨の入力合成から雨を切り離せませんでした", clearException);
        }
    }

    private void ReportError(Exception exception)
    {
        if (errorReported)
        {
            return;
        }

        errorReported = true;
        WriteDiagnostic("降雨の描画に失敗したため入力映像をそのまま返します", exception);
    }

    private static void WriteDiagnostic(string message, Exception exception)
    {
        try
        {
            Trace.TraceError($"{message}: {exception}");
        }
        catch
        {
            // 診断先の失敗で入力映像の表示を中断しません。
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
            renderer?.Dispose();
        }
        finally
        {
            emptyBitmap.Dispose();
        }
    }
}
