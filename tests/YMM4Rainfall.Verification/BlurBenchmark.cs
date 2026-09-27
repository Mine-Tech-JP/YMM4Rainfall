// SPDX-License-Identifier: MPL-2.0
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM4Rainfall.Rendering;
using YMM4Rainfall.Simulation;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace YMM4Rainfall.Verification;

/// <summary>固定粒の描画を単独プロセスで比較します。YMM4全体の再生FPSではありません。</summary>
internal static class BlurBenchmark
{
    public static void Run(string outputPath)
    {
        if (File.Exists(outputPath)) throw new IOException("測定結果の上書きは行いません。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var rows = new List<object>();
        foreach (var driver in new[] { DriverType.Hardware, DriverType.Warp })
        {
            D3D11.D3D11CreateDevice(null, driver, DeviceCreationFlags.BgraSupport,
                [Vortice.Direct3D.FeatureLevel.Level_11_0], out var created).CheckError();
            using var d3d = created ?? throw new InvalidOperationException("測定用デバイスを作成できません。");
            using var dxgi = d3d.QueryInterface<IDXGIDevice>();
            using var factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
            using var device = factory.CreateDevice(dxgi);
            using var context = device.CreateDeviceContext(DeviceContextOptions.None);
            var width = driver == DriverType.Hardware ? 1920 : 640;
            var height = driver == DriverType.Hardware ? 1080 : 360;
            using var input = Bitmap(context, width, height, BitmapOptions.Target);
            using var target = Bitmap(context, width, height, BitmapOptions.Target);
            using var readback = Bitmap(context, width, height, BitmapOptions.CpuRead | BitmapOptions.CannotDraw);
            context.Target = input;
            context.BeginDraw(); context.Clear(new Color4(0, 0, 0, 0)); context.EndDraw().CheckError(); context.Target = null;
            foreach (var count in new[] { 100, 1000, 4000 })
            foreach (var varied in new[] { false, true })
            foreach (var mode in new[] { "OFF", "Symmetric", "Trailing" })
            {
                using var renderer = RainfallRenderer.Create(context);
                renderer.SetInput(input);
                var strokes = Enumerable.Range(0, count).Select(i =>
                {
                    var center = new Vector2(100 + i % 80 * (width - 200) / 80f, 100 + i / 80 * (height - 200) / 50f);
                    return new RainfallStroke(center, center, 2, 0.4f, 8)
                    {
                        BlurVelocity = varied ? new(60 + i % 997, -400 + i % 797) : new(500, 200),
                    };
                }).ToArray();
                var p = RainfallParameters.Default with { Shape = RainfallShape.Circle, MotionBlurEnabled = mode != "OFF",
                    MotionBlurMode = mode == "Trailing" ? MotionBlurMode.Trailing : MotionBlurMode.Symmetric, MotionBlurStrength = 100 };
                Func<RainfallBounds, RainfallStroke[]> frame = _ => strokes;
                void Draw()
                {
                    renderer.Render(p, 1, frameFactory: frame);
                    context.Target = target;
                    context.BeginDraw();
                    try { context.Clear(new Color4(0, 0, 0, 0)); context.DrawImage(renderer.Output); }
                    finally { try { context.EndDraw().CheckError(); } finally { context.Target = null; } }
                    // GPU完了を待つため毎回読み戻します。画像配列とハッシュ処理は測定外です。
                    readback.CopyFromBitmap(target);
                    _ = readback.Map(MapOptions.Read); readback.Unmap();
                }
                Draw(); Draw();
                var times = new double[5]; var allocations = new long[5];
                for (var i = 0; i < times.Length; i++)
                {
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp(); Draw();
                    times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
                }
                var pixels = new byte[width * height * 4];
                var map = readback.Map(MapOptions.Read);
                try { for (var y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(map.Bits, y * (int)map.Pitch), pixels, y * width * 4, width * 4); }
                finally { readback.Unmap(); }
                var milliseconds = times.Order().ElementAt(2);
                var bytes = allocations.Order().ElementAt(2);
                rows.Add(new { Driver = driver.ToString(), Width = width, Height = height, Count = count, Varied = varied, Mode = mode,
                    MedianMilliseconds = milliseconds, MedianAllocatedBytes = bytes, Milliseconds = times, AllocatedBytes = allocations,
                    PixelSHA256 = Convert.ToHexString(SHA256.HashData(pixels)) });
                Console.WriteLine($"{driver} {count}粒 速度差={varied} {mode}: 中央値 {milliseconds:F2} ms、管理割当 {bytes} bytes/フレーム");
            }
        }
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new { Date = DateTimeOffset.Now, Warmup = 2, Samples = 5,
            Scope = "固定粒描画と同期読戻し。管理割当は呼出スレッドのみ。シミュレーション・YMM4・ネイティブメモリを含まない。", Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ID2D1Bitmap1 Bitmap(ID2D1DeviceContext context, int width, int height, BitmapOptions options)
        => context.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, options));
}
