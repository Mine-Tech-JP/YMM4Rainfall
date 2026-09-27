// SPDX-License-Identifier: MPL-2.0
using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YMM4Rainfall.Rendering;

/// <summary>保存するPNG原本と、描画用の乗算済みアルファ画素を同じ読み込みから得ます。</summary>
internal sealed record RainfallPngData(byte[] Bytes, byte[] Pixels, int Width, int Height)
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public static RainfallPngData Load(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PNG形式の画像を選択してください。");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length > MaximumBytes) throw new InvalidDataException("画像は16 MB以下にしてください。");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return Decode(bytes);
    }

    internal static RainfallPngData Decode(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("画像は16 MB以下にしてください。");
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("画像を読み込めません。PNG画像が破損しています。");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is < 1 or > 2048 || height is < 1 or > 2048)
            throw new InvalidDataException("画像の縦・横は各2048 px以下にしてください。");
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
        if (source.PixelWidth != width || source.PixelHeight != height) throw new InvalidDataException("PNG画像の寸法が不正です。");
        var pixels = new byte[checked(width * height * 4)];
        source.CopyPixels(pixels, width * 4, 0);
        return new(bytes, pixels, width, height);
    }
}
