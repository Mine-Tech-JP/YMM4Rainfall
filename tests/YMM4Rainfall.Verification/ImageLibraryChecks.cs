// SPDX-License-Identifier: MPL-2.0
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YMM4Rainfall.Rendering;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class ImageLibraryChecks
{
    public static void Run(Action<string, Action> check, Action<bool> require)
    {
        var root = Path.GetFullPath(Path.Combine("tmp", "image-library-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string Source(string name, byte red)
        {
            var pixels = new byte[4 * 2 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = red; pixels[i + 3] = 255; }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 16)));
            var path = Path.Combine(root, name);
            using var file = File.Create(path);
            encoder.Save(file);
            return path;
        }
        void Fails(Action action)
        {
            var failed = false;
            try { action(); }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { failed = true; }
            require(failed);
        }
        check("画像ライブラリUI: 一覧破損時も標準ブタを選べ、既存ファイルを保持", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "broken-editor"));
            Directory.CreateDirectory(library.Root);
            File.WriteAllText(library.RegistryPath, "invalid");
            var before = File.ReadAllBytes(library.RegistryPath);
            var editor = new RainfallImageEditor(library) { ImageReference = $"{Guid.Empty:N}|0" };
            var panel = (System.Windows.Controls.StackPanel)editor.Content;
            var row = (System.Windows.Controls.StackPanel)panel.Children[0];
            var selector = row.Children.OfType<System.Windows.Controls.ComboBox>().Single();
            var status = (System.Windows.Controls.TextBlock)panel.Children[1];
            require(selector.Items.Count == 1 && selector.Items[0] is RainfallImageEntry { Id: var id } && id == RainfallBuiltInImage.PigId);
            require(status.Text.Contains("画像一覧を読み込めません", StringComparison.Ordinal));
            selector.SelectedIndex = 0;
            var use = row.Children.OfType<System.Windows.Controls.Button>().Single(button => (string)button.Content == "使用");
            use.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            require(editor.ImageReference == $"{RainfallBuiltInImage.PigId:N}|1");
            require(before.SequenceEqual(File.ReadAllBytes(library.RegistryPath)));
            require(Directory.GetFiles(library.Root).Length == 1);
        });
        check("画像ライブラリUI: 選択・同じIDの置換・編集通知・バインディング解放", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "editor"));
            var entry = library.Add("ブタのサンプル", Source("editor.png", 255));
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var editor = new RainfallImageEditor(library);
            var attribute = new RainfallImageEditorAttribute();
            var property = typeof(RainfallEffect).GetProperty(nameof(RainfallEffect.PngSelection))!;
            var cache = new YukkuriMovieMaker.Commons.PropertiesCache();
            var begin = 0; var end = 0;
            editor.BeginEdit += (_, _) => begin++;
            editor.EndEdit += (_, _) => end++;
            attribute.SetBindings(editor, [new YukkuriMovieMaker.Commons.ItemProperty(effect, effect, property, cache)]);
            editor.Apply(entry.Id, entry.Revision);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            require(effect.PngImageId == entry.Id && effect.PngImageRevision == 1);
            editor.Apply(entry.Id, 2);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            require(effect.PngImageRevision == 2 && begin == 2 && end == 2);
            var json = YmmJson.GetJsonText(effect);
            require(!json.Contains("PngSelection"));
            require(YmmJson.LoadFromText<RainfallEffect>(json)!.PngImageRevision == 2);
            editor.Measure(new System.Windows.Size(320, double.PositiveInfinity));
            editor.Arrange(new System.Windows.Rect(0, 0, 320, editor.DesiredSize.Height));
            editor.UpdateLayout();
            var height = (int)Math.Ceiling(editor.ActualHeight);
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 320, height));
                draw.DrawRectangle(new VisualBrush(editor), null, new System.Windows.Rect(0, 0, 320, height));
            }
            var preview = new RenderTargetBitmap(320, height, 96, 96, PixelFormats.Pbgra32);
            preview.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(preview));
            using (var file = File.Create(Path.GetFullPath("tmp/rainfall-image-editor.png"))) encoder.Save(file);
            attribute.ClearBindings(editor);
            require(!System.Windows.Data.BindingOperations.IsDataBound(editor, RainfallImageEditor.ImageReferenceProperty));
        });
        check("画像ライブラリ: 原本のコピー・移動後の読込・IDだけの保存", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "copy"));
            require(library.Load().Count == 0 && !Directory.Exists(library.Root));
            var source = Source("source.png", 255);
            var hash = SHA256.HashData(File.ReadAllBytes(source));
            var entry = library.Add("ブタ", source);
            var copied = library.Resolve(entry.Id);
            require(copied != source && File.Exists(source));
            require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(copied))));
            File.Move(source, source + ".moved");
            require(RainfallPngData.Load(library.Resolve(entry.Id)).Width == 4);
            var json = YmmJson.GetJsonText(new RainfallEffect(useNewItemDefaults: false) { PngImageId = entry.Id });
            require(!json.Contains(root) && !json.Contains("PngPath"));
            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            require(new RainfallImageLibrary(library.Root).Resolve(restored.PngImageId) == copied);
        });
        check("画像ライブラリ: 同じIDの置換を別インスタンスで反映・削除は原本保持", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "replace"));
            var source = Source("replace-source.png", 255);
            var second = Source("replace-second.png", 80);
            var first = library.Add("画像", source);
            var reader = new RainfallImageLibrary(library.Root);
            var old = reader.Resolve(first.Id);
            var next = library.Replace(first.Id, second);
            require(next.Id == first.Id && next.Revision == 2 && next.Hash != first.Hash);
            var latest = reader.Resolve(first.Id);
            require(latest != old && RainfallPngData.Load(latest).Pixels[2] == 80);
            library.Delete(next.Id);
            Fails(() => reader.Resolve(next.Id));
            require(File.Exists(source) && File.Exists(second));
        });
        check("画像ライブラリ: 重複名・破損PNG・不正な一覧で登録を壊さない", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "invalid"));
            var source = Source("valid.png", 100);
            var entry = library.Add("登録", source);
            var before = File.ReadAllBytes(library.RegistryPath);
            Fails(() => library.Add("登録", source));
            var broken = Path.Combine(root, "broken.png");
            File.WriteAllText(broken, "broken");
            Fails(() => library.Replace(entry.Id, broken));
            require(before.SequenceEqual(File.ReadAllBytes(library.RegistryPath)));
            File.WriteAllText(library.RegistryPath, "invalid");
            Fails(() => library.Add("追加", source));
            require(File.ReadAllText(library.RegistryPath) == "invalid");
        });
        check("画像ライブラリ: 一覧保存が失敗しても元の参照と画像を保持", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "atomic"));
            var source = Source("atomic-source.png", 255);
            var next = Source("atomic-next.png", 50);
            var entry = library.Add("画像", source);
            var original = library.Resolve(entry.Id);
            var before = File.ReadAllBytes(library.RegistryPath);
            using (var locked = new FileStream(library.RegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Fails(() => library.Replace(entry.Id, next));
            require(before.SequenceEqual(File.ReadAllBytes(library.RegistryPath)));
            require(library.Resolve(entry.Id) == original);
            require(Directory.GetFiles(library.Root, "*.png").Length == 1);
        });
        check("画像ライブラリ: コピーの改変を検出し、元画像での置換で復旧", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "integrity"));
            var source = Source("integrity.png", 255);
            var entry = library.Add("画像", source);
            var path = library.Resolve(entry.Id);
            File.WriteAllText(path, "corrupt");
            Fails(() => library.Resolve(entry.Id));
            var restored = library.Replace(entry.Id, source);
            require(restored.Hash == entry.Hash && RainfallPngData.Load(library.Resolve(entry.Id)).Pixels[2] == 255);
        });
        check("画像ライブラリ: 一覧のパス混入を拒否・同時書き込みを保護", () =>
        {
            var library = new RainfallImageLibrary(Path.Combine(root, "guard"));
            var source = Source("guard.png", 255);
            var entry = library.Add("画像", source);
            using (var locked = new FileStream(Path.Combine(library.Root, "library.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Fails(() => library.Replace(entry.Id, source));
            var document = JsonSerializer.Serialize(new { Version = 1, Images = new[] { entry with { Hash = "../../other.png" } } });
            File.WriteAllText(library.RegistryPath, document);
            Fails(() => library.Load());
            require(File.Exists(source));
        });
    }
}
