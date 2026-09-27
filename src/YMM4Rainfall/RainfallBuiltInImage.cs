// SPDX-License-Identifier: MPL-2.0
using System.IO;
using YMM4Rainfall.Rendering;

namespace YMM4Rainfall;

/// <summary>利用者の画像ライブラリへ登録・書き込みをせず、DLL内の標準画像を読みます。</summary>
internal static class RainfallBuiltInImage
{
    internal static readonly Guid PigId = new("b512c68a-572c-46d0-84c9-b2138377ab7d");
    internal const string PigPath = "builtin://YMM4Rainfall/pig/1";
    internal static RainfallPngData LoadPig()
    {
        using var stream = typeof(RainfallBuiltInImage).Assembly.GetManifestResourceStream("YMM4Rainfall.BuiltIn.Pig.png")
            ?? throw new InvalidDataException("標準画像のブタを読み込めません。プラグインを再配置してください。");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return RainfallPngData.Decode(buffer.ToArray());
    }
    private static readonly Lazy<RainfallImageEntry> Entry = new(() =>
    {
        var data = LoadPig();
        return new(PigId, "ブタ（標準画像）", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data.Bytes)),
            1, data.Width, data.Height);
    });
    internal static RainfallImageEntry PigEntry => Entry.Value;
}
