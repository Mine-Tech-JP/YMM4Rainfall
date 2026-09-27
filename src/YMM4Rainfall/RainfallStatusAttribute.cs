// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YukkuriMovieMaker.Commons;

namespace YMM4Rainfall;

/// <summary>画像の状態を編集できないテキストとして表示します。</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class RainfallStatusAttribute : PropertyEditorAttribute
{
    public override FrameworkElement Create()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(Control.FontSize))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1),
        });
        return text;
    }
    public override void SetBindings(FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
        => control.SetBinding(TextBlock.TextProperty, new Binding(propertyInfo.Name)
        {
            Source = propertyOwner,
            Mode = BindingMode.OneWay,
        });
    public override void ClearBindings(FrameworkElement control)
        => BindingOperations.ClearBinding(control, TextBlock.TextProperty);
}
