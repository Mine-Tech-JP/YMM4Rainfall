// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Windows;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace YMM4Rainfall;

/// <summary>種類の切替で参照先が変わるAnimationを、既存のスライダーへ再バインドします。</summary>
internal sealed class WeatherAnimationSliderAttribute(
    string stringFormat,
    string unitText,
    double defaultMin,
    double defaultMax)
    : AnimationSliderAttribute(stringFormat, unitText, defaultMin, defaultMax)
{
    private readonly Dictionary<FrameworkElement, BindingState> states = [];

    public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(itemProperties);
        if (itemProperties.Length == 0)
        {
            ClearBindings(control);
            return;
        }
        RemoveState(control);
        base.SetBindings(control, itemProperties);

        var state = new BindingState(control, itemProperties, RefreshBindings);
        states.Add(control, state);
        state.Subscribe();
    }

    public override void ClearBindings(FrameworkElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        RemoveState(control);
        base.ClearBindings(control);
    }

    private void RefreshBindings(BindingState state, PropertyChangedEventArgs args)
    {
        if (state.IsRebinding || !state.IsRelevant(args.PropertyName) || !state.HasReferenceChanged())
            return;

        state.IsRebinding = true;
        try
        {
            base.SetBindings(state.Control, state.ItemProperties);
            state.CaptureReferences();
        }
        finally
        {
            state.IsRebinding = false;
        }
    }

    private void RemoveState(FrameworkElement control)
    {
        if (!states.Remove(control, out var state))
            return;
        state.Unsubscribe();
    }

    private sealed class BindingState(
        FrameworkElement control,
        ItemProperty[] itemProperties,
        Action<BindingState, PropertyChangedEventArgs> refresh)
    {
        private readonly Action<BindingState, PropertyChangedEventArgs> refresh = refresh;
        private readonly INotifyPropertyChanged[] owners = GetOwners(itemProperties);
        private Animation?[] references = GetReferences(itemProperties);

        public FrameworkElement Control { get; } = control;
        public ItemProperty[] ItemProperties { get; } = [.. itemProperties];
        public string PropertyName { get; } = itemProperties[0].PropertyInfo.Name;
        public bool IsRebinding { get; set; }

        public void Subscribe()
        {
            foreach (var owner in owners)
                owner.PropertyChanged += Owner_PropertyChanged;
        }

        public void Unsubscribe()
        {
            foreach (var owner in owners)
                owner.PropertyChanged -= Owner_PropertyChanged;
        }

        public bool IsRelevant(string? propertyName) =>
            string.IsNullOrEmpty(propertyName) || propertyName == nameof(RainfallEffect.Kind) || propertyName == PropertyName;

        public bool HasReferenceChanged()
        {
            var current = GetReferences(ItemProperties);
            if (current.Length != references.Length)
                return true;
            for (var index = 0; index < current.Length; index++)
            {
                if (!ReferenceEquals(current[index], references[index]))
                    return true;
            }
            return false;
        }

        public void CaptureReferences() => references = GetReferences(ItemProperties);

        private void Owner_PropertyChanged(object? sender, PropertyChangedEventArgs args) => refresh(this, args);

        private static Animation?[] GetReferences(IEnumerable<ItemProperty> properties) =>
            properties.Select(property => property.GetValue<Animation>()).ToArray();

        private static INotifyPropertyChanged[] GetOwners(IEnumerable<ItemProperty> properties)
        {
            var owners = new HashSet<INotifyPropertyChanged>(ReferenceEqualityComparer.Instance);
            foreach (var property in properties)
            {
                if (property.PropertyOwner is INotifyPropertyChanged owner)
                    owners.Add(owner);
            }
            return [.. owners];
        }
    }
}
