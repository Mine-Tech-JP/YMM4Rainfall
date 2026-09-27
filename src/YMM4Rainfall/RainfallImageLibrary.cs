// SPDX-License-Identifier: MPL-2.0
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using YMM4Rainfall.Rendering;
using YukkuriMovieMaker.Commons;

namespace YMM4Rainfall;

internal sealed record RainfallImageEntry(
    Guid Id, string Name, string Hash, long Revision, int Width, int Height);

/// <summary>PNGをローカルライブラリへコピーし、IDで参照します。原本は変更しません。</summary>
internal sealed class RainfallImageLibrary
{
    private sealed class Registry
    {
        [JsonRequired] public int Version { get; set; } = 1;
        [JsonRequired] public List<RainfallImageEntry> Images { get; set; } = [];
    }
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    // 検証用ライブラリやエラー文の取得だけでは、利用者の設定フォルダーを参照しません。
    private static readonly Lazy<RainfallImageLibrary> shared = new(() =>
        new(Path.Combine(AppDirectories.SettingDirectory, "YMM4Rainfall", "images")));
    public static RainfallImageLibrary Shared => shared.Value;
    public string Root { get; }
    public string RegistryPath => Path.Combine(Root, "images.json");
    private readonly object gate = new();
    private string? registryStamp;
    private IReadOnlyList<RainfallImageEntry> cached = [];
    private readonly Dictionary<Guid, string> verified = [];

    public RainfallImageLibrary(string root) => Root = Path.GetFullPath(root);

    public IReadOnlyList<RainfallImageEntry> Load()
    {
        lock (gate) return ReadRegistry().ToArray();
    }

    private IReadOnlyList<RainfallImageEntry> ReadRegistry(bool force = false)
    {
        var info = new FileInfo(RegistryPath);
        var stamp = info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "missing";
        if (!force && stamp == registryStamp) return cached;
        if (!info.Exists) { cached = []; registryStamp = stamp; verified.Clear(); return cached; }
        using var stream = new FileStream(RegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 256 * 1024) throw new InvalidDataException("画像一覧が大きすぎます。既存の登録は変更しません。");
        Registry data;
        try { data = JsonSerializer.Deserialize<Registry>(stream, Options) ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidDataException("画像一覧を読み込めません。既存の登録は変更しません。"); }
        if (data.Version != 1 || data.Images is null || data.Images.Count > 100 ||
            data.Images.Any(e => e is null || e.Id == Guid.Empty || !ValidName(e.Name) ||
                e.Hash is null || e.Hash.Length != 64 || !e.Hash.All(Uri.IsHexDigit) ||
                e.Revision < 1 || e.Width is < 1 or > 2048 || e.Height is < 1 or > 2048) ||
            data.Images.Select(e => e.Id).Distinct().Count() != data.Images.Count ||
            data.Images.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Images.Count)
            throw new InvalidDataException("画像一覧の形式が不正です。既存の登録は変更しません。");
        cached = data.Images.ToArray();
        registryStamp = stamp;
        verified.Clear();
        return cached;
    }

    public string Resolve(Guid id)
    {
        lock (gate)
        {
            if (id == Guid.Empty) throw new InvalidDataException("PNG画像を登録して選択してください。");
            var entry = ReadRegistry().FirstOrDefault(e => e.Id == id)
                ?? throw new InvalidDataException("登録画像が見つかりません。画像を選び直してください。");
            var path = ImagePath(entry);
            var info = new FileInfo(path);
            if (!info.Exists) throw new InvalidDataException("登録画像のファイルが見つかりません。");
            var stamp = $"{entry.Hash}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            if (!verified.TryGetValue(id, out var previous) || previous != stamp)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length > RainfallPngData.MaximumBytes ||
                    !string.Equals(Convert.ToHexString(SHA256.HashData(stream)), entry.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("登録画像が変更・破損しています。「置換」で登録し直してください。");
                verified[id] = stamp;
            }
            return path;
        }
    }

    public RainfallImageEntry Add(string name, string source) => Save(null, name, source);
    public RainfallImageEntry Replace(Guid id, string source) => Save(id, null, source);

    private RainfallImageEntry Save(Guid? id, string? name, string source)
    {
        var png = RainfallPngData.Load(source);
        lock (gate)
        {
            using var writeLock = LockWrites();
            var entries = ReadRegistry(force: true).ToList();
            var previous = id is null ? null : entries.FirstOrDefault(e => e.Id == id)
                ?? throw new InvalidDataException("置換する画像が見つかりません。");
            name = previous?.Name ?? name?.Trim();
            if (!ValidName(name)) throw new InvalidDataException("登録名は1～64文字で入力してください。");
            if (previous is null && entries.Count >= 100) throw new InvalidDataException("画像の登録は100件までです。");
            if (entries.Any(e => e.Id != id && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("同じ登録名があります。一覧から選んで「置換」を使ってください。");
            var entry = new RainfallImageEntry(previous?.Id ?? Guid.NewGuid(), name!, Convert.ToHexString(SHA256.HashData(png.Bytes)),
                checked((previous?.Revision ?? 0) + 1), png.Width, png.Height);
            var path = ImagePath(entry);
            var created = false;
            try
            {
                created = !File.Exists(path);
                WriteAtomic(path, png.Bytes);
                entries.RemoveAll(e => e.Id == entry.Id);
                entries.Add(entry);
                SaveRegistry(entries);
            }
            catch
            {
                if (created) TryDelete(path);
                throw;
            }
            if (previous is not null && ImagePath(previous) != path) TryDelete(ImagePath(previous));
            return entry;
        }
    }

    public bool Delete(Guid id)
    {
        lock (gate)
        {
            using var writeLock = LockWrites();
            var entries = ReadRegistry(force: true).ToList();
            var entry = entries.FirstOrDefault(e => e.Id == id) ?? throw new InvalidDataException("削除する画像が見つかりません。");
            entries.Remove(entry);
            SaveRegistry(entries);
            return TryDelete(ImagePath(entry));
        }
    }

    private FileStream LockWrites()
    {
        Directory.CreateDirectory(Root);
        return new FileStream(Path.Combine(Root, "library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    // パスは検証済みIDとハッシュだけから構成し、登録名をファイル名に使用しません。
    private string ImagePath(RainfallImageEntry entry) => Path.Combine(Root, $"{entry.Id:N}-{entry.Hash.ToUpperInvariant()}.png");
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && !name.Any(char.IsControl);
    private void SaveRegistry(List<RainfallImageEntry> entries)
    {
        WriteAtomic(RegistryPath, JsonSerializer.SerializeToUtf8Bytes(new Registry { Images = entries }, Options));
        registryStamp = null;
        verified.Clear();
    }
    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { TryDelete(temporary); }
    }
    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static string ErrorText(Exception exception) => exception is InvalidDataException ? exception.Message
        : "画像ライブラリを操作できません。ファイルの形式・使用状態・アクセス権を確認してください。";
}
