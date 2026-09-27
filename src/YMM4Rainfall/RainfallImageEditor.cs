// SPDX-License-Identifier: MPL-2.0
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Views.Converters;

namespace YMM4Rainfall;

internal sealed class RainfallImageEditorAttribute : PropertyEditorAttribute2
{
    public RainfallImageEditorAttribute() => PropertyEditorSize = PropertyEditorSize.FullWidth;
    public override FrameworkElement Create() => new RainfallImageEditor();
    public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
        => control.SetBinding(RainfallImageEditor.ImageReferenceProperty,
            ItemPropertiesBinding.Create2(itemProperties, BindingMode.TwoWay, UpdateSourceTrigger.PropertyChanged, IsMultiEditing, delay: 0));
    public override void ClearBindings(FrameworkElement control) => BindingOperations.ClearBinding(control, RainfallImageEditor.ImageReferenceProperty);
}

/// <summary>ライブラリの編集と、アイテムで使用する画像IDの変更を分けます。</summary>
internal sealed class RainfallImageEditor : UserControl, IPropertyEditorControl2
{
    public static readonly DependencyProperty ImageReferenceProperty = DependencyProperty.Register(nameof(ImageReference), typeof(string),
        typeof(RainfallImageEditor), new PropertyMetadata(string.Empty, (sender, _) => ((RainfallImageEditor)sender).Refresh()));
    private readonly RainfallImageLibrary library;
    private readonly ComboBox selector = new() { MinWidth = 140, MaxWidth = 220, DisplayMemberPath = nameof(RainfallImageEntry.Name) };
    private readonly TextBox name = new() { MinWidth = 110, MaxLength = 64 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private bool refreshing;
    public string ImageReference { get => (string)GetValue(ImageReferenceProperty); set => SetValue(ImageReferenceProperty, value); }
    private Guid ImageId => Guid.TryParse(ImageReference.Split('|')[0], out var id) ? id : Guid.Empty;
    public event EventHandler? BeginEdit;
    public event EventHandler? EndEdit;
    public RainfallImageEditor() : this(RainfallImageLibrary.Shared) { }
    internal RainfallImageEditor(RainfallImageLibrary library)
    {
        this.library = library;
        var root = new StackPanel { MinWidth = 320 };
        var select = new StackPanel { Orientation = Orientation.Horizontal };
        select.Children.Add(selector);
        select.Children.Add(Button("使用", () =>
        {
            var entry = Selected();
            if (entry.Id != RainfallBuiltInImage.PigId) _ = library.Resolve(entry.Id);
            Apply(entry.Id, entry.Revision);
            status.Text = $"「{entry.Name}」を使用します。";
        }));
        select.Children.Add(Button("置換", () =>
        {
            var entry = Selected();
            if (entry.Id == RainfallBuiltInImage.PigId) { status.Text = "標準画像は置換・削除できません。別のPNGを追加して使用してください。"; return; }
            if (!Confirm($"「{entry.Name}」を置き換えますか？\nこの画像を使うすべてのアイテムに反映されます。ライブラリの変更はUndoできません。")) return;
            var path = SelectPng();
            if (path is null) return;
            var saved = library.Replace(entry.Id, path);
            Refresh();
            Apply(saved.Id, saved.Revision);
            status.Text = $"「{saved.Name}」を置き換えました。";
        }));
        select.Children.Add(Button("削除", () =>
        {
            var entry = Selected();
            if (entry.Id == RainfallBuiltInImage.PigId) { status.Text = "標準画像は置換・削除できません。別のPNGを追加して使用してください。"; return; }
            if (!Confirm($"「{entry.Name}」の登録とコピー画像を削除しますか？\nこの画像を使うすべてのアイテムで表示できなくなります。ライブラリの変更はUndoできません。元PNGは削除しません。")) return;
            var removed = library.Delete(entry.Id);
            Refresh();
            status.Text = removed ? $"「{entry.Name}」を削除しました。" : "登録を削除しました。コピー画像は削除できず残っています。";
        }));
        var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        add.Children.Add(new TextBlock { Text = "登録名", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        add.Children.Add(name);
        add.Children.Add(Button("PNGを追加", () =>
        {
            var path = SelectPng();
            if (path is null) return;
            var saved = library.Add(name.Text, path);
            Apply(saved.Id, saved.Revision);
            Refresh();
            status.Text = $"「{saved.Name}」を登録しました。元PNGを移動しても使えます。";
        }));
        root.Children.Add(select);
        root.Children.Add(status);
        root.Children.Add(add);
        Content = root;
        Loaded += (_, _) => Refresh();
        selector.DropDownOpened += (_, _) => Refresh();
    }
    public void SetEditorInfo(IEditorInfo? info) { }
    public void SetFocus() => selector.Focus();
    private RainfallImageEntry Selected() => selector.SelectedItem as RainfallImageEntry
        ?? throw new System.IO.InvalidDataException("画像を一覧から選択してください。");
    internal void Apply(Guid id, long revision)
    {
        BeginEdit?.Invoke(this, EventArgs.Empty);
        try { SetCurrentValue(ImageReferenceProperty, $"{id:N}|{revision}"); }
        finally { EndEdit?.Invoke(this, EventArgs.Empty); }
    }
    private void Refresh()
    {
        if (refreshing || library is null) return;
        refreshing = true;
        try
        {
            RainfallImageEntry[] entries = [RainfallBuiltInImage.PigEntry];
            string? libraryError = null;
            try { entries = [.. entries, .. library.Load()]; }
            catch (Exception exception) { libraryError = RainfallImageLibrary.ErrorText(exception); }
            selector.ItemsSource = entries;
            selector.SelectedItem = entries.FirstOrDefault(e => e.Id == ImageId);
            status.Text = libraryError ?? (ImageId != Guid.Empty && selector.SelectedItem is null
                ? "現在の登録画像がありません。選び直してください。" : "登録したPNGを一覧から選んで使用します。");
        }
        catch (Exception exception) { status.Text = RainfallImageLibrary.ErrorText(exception); }
        finally { refreshing = false; }
    }
    private Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(5, 1, 5, 1) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception exception) { status.Text = RainfallImageLibrary.ErrorText(exception); }
        };
        return button;
    }
    private static string? SelectPng()
    {
        var dialog = new OpenFileDialog { Filter = "PNG画像 (*.png)|*.png", Title = "登録するPNGを選択", CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    private static bool Confirm(string message) => MessageBox.Show(message, "雨雪と浮上", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
