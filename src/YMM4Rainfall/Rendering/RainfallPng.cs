// SPDX-License-Identifier: MPL-2.0
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall.Rendering;

/// <summary>PNGを読み込み、ファイル更新と色変更に応じて自分のビットマップを更新します。</summary>
internal sealed class RainfallPng : IDisposable
{
    private string? key;
    private byte[]? pixels;
    private int width, height;
    private (double R, double G, double B)? tint;
    private ID2D1Bitmap1? bitmap;
    public string Status { get; private set; } = "PNG画像を選択してください。";
    public float WidthRatio => (float)width / Math.Max(width, height);
    public float HeightRatio => (float)height / Math.Max(width, height);

    public ID2D1Bitmap1? Update(ID2D1DeviceContext context, string path, RainfallParameters parameters)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) { Reset(null); Status = "PNG画像を選択してください。"; return null; }
            var builtIn = path == RainfallBuiltInImage.PigPath;
            var file = builtIn ? null : new FileInfo(path);
            if (!builtIn && !file!.Exists) { Reset(null); Status = "画像が見つかりません。選び直してください。"; return null; }
            var next = builtIn ? RainfallBuiltInImage.PigPath : $"{file!.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            if (key != next)
            {
                Reset(next);
                var data = builtIn ? RainfallBuiltInImage.LoadPig() : RainfallPngData.Load(path);
                width = data.Width;
                height = data.Height;
                pixels = data.Pixels;
                Status = $"{width} × {height} px";
            }
            if (pixels is null) return null;
            var nextTint = (parameters.Red, parameters.Green, parameters.Blue);
            if (bitmap is null || tint != nextTint)
            {
                var colored = (byte[])pixels.Clone();
                for (var i = 0; i < colored.Length; i += 4)
                {
                    colored[i] = (byte)Math.Round(colored[i] * parameters.Blue);
                    colored[i + 1] = (byte)Math.Round(colored[i + 1] * parameters.Green);
                    colored[i + 2] = (byte)Math.Round(colored[i + 2] * parameters.Red);
                }
                var pin = GCHandle.Alloc(colored, GCHandleType.Pinned);
                ID2D1Bitmap1 candidate;
                try
                {
                    candidate = context.CreateBitmap(new SizeI(width, height), pin.AddrOfPinnedObject(), (width * 4),
                        new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
                }
                finally { pin.Free(); }
                bitmap?.Dispose();
                bitmap = candidate;
                tint = nextTint;
            }
            return bitmap;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or System.IO.FileFormatException)
        {
            key = null;
            pixels = null;
            bitmap?.Dispose();
            bitmap = null;
            Status = exception is InvalidDataException ? exception.Message : "画像を読み込めません。PNGファイルを確認してください。";
            return null;
        }
    }

    private void Reset(string? next)
    {
        bitmap?.Dispose();
        bitmap = null;
        pixels = null;
        tint = null;
        key = next;
    }
    public void Dispose() => Reset(null);
}
