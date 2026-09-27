// SPDX-License-Identifier: MPL-2.0
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Simulation;

namespace YMM4Rainfall.Rendering;

/// <summary>入力のエフェクトグラフを映り込みの描画コマンドから切り離します。</summary>
internal sealed class RainfallBackgroundSnapshot : IDisposable
{
    public ID2D1Bitmap1 Bitmap { get; }
    public Matrix3x2 ToWorld { get; }
    private RainfallBackgroundSnapshot(ID2D1Bitmap1 bitmap, Matrix3x2 toWorld)
    { Bitmap = bitmap; ToWorld = toWorld; }

    public static RainfallBackgroundSnapshot Capture(ID2D1DeviceContext context, ID2D1Image input, RainfallBounds bounds)
    {
        // 大きな入力でも、映り込み用画像の割り当てを4096×4096以内に抑えます。
        var scale = (float)Math.Min(1, 4096 / Math.Max(bounds.Width, bounds.Height));
        var bitmap = context.CreateBitmap(new SizeI((int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale)),
            new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96, 96, BitmapOptions.Target));
        try
        {
            context.Target = bitmap;
            context.Transform = Matrix3x2.CreateTranslation(-(float)bounds.Left, -(float)bounds.Top) * Matrix3x2.CreateScale(scale);
            context.BeginDraw();
            try
            {
                context.Clear(new Color4(0, 0, 0, 0));
                context.DrawImage(input);
            }
            finally { context.EndDraw().CheckError(); }
            return new(bitmap, Matrix3x2.CreateScale(1 / scale) * Matrix3x2.CreateTranslation((float)bounds.Left, (float)bounds.Top));
        }
        catch { bitmap.Dispose(); throw; }
        finally { context.Target = null; context.Transform = Matrix3x2.Identity; }
    }
    public void Dispose() => Bitmap.Dispose();
}
