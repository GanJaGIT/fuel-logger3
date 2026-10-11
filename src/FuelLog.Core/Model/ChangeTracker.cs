using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;

namespace FuelLog.Core.Model;

/// <summary>
/// Следит за любыми изменениями в графе объектов проекта (свойства и коллекции на любой глубине)
/// и сообщает о них одним событием — основа автосохранения.
/// </summary>
public sealed class ChangeTracker : IDisposable
{
    private readonly HashSet<object> _tracked = new(ReferenceEqualityComparer.Instance);

    // Последний известный состав каждой коллекции: позволяет корректно отписаться даже после Reset.
    private readonly Dictionary<object, List<object>> _collectionItems = new(ReferenceEqualityComparer.Instance);

    public ChangeTracker(object root) => Attach(root);

    public event EventHandler? Changed;

    public void Dispose()
    {
        foreach (var node in _tracked)
        {
            if (node is INotifyPropertyChanged npc)
                npc.PropertyChanged -= OnPropertyChanged;
            if (node is INotifyCollectionChanged ncc)
                ncc.CollectionChanged -= OnCollectionChanged;
        }
        _tracked.Clear();
        _collectionItems.Clear();
    }

    private void Attach(object? node)
    {
        if (node is null || node is string || node.GetType().IsValueType || !_tracked.Add(node))
            return;

        if (node is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnCollectionChanged;
            var items = ((IEnumerable)node).Cast<object>().ToList();
            _collectionItems[node] = items;
            foreach (var item in items)
                Attach(item);
            return;
        }

        if (node is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnPropertyChanged;

        foreach (var property in TrackableProperties(node.GetType()))
            Attach(property.GetValue(node));
    }

    private void Detach(object? node)
    {
        if (node is null || !_tracked.Remove(node))
            return;

        if (node is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged -= OnCollectionChanged;
            if (_collectionItems.Remove(node, out var items))
                foreach (var item in items)
                    Detach(item);
            return;
        }

        if (node is INotifyPropertyChanged npc)
            npc.PropertyChanged -= OnPropertyChanged;

        foreach (var property in TrackableProperties(node.GetType()))
            Detach(property.GetValue(node));
    }

    private static IEnumerable<PropertyInfo> TrackableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0
                && !p.PropertyType.IsValueType && p.PropertyType != typeof(string)
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is null)
            return;

        var current = ((IEnumerable)sender).Cast<object>().ToList();
        var previous = _collectionItems.GetValueOrDefault(sender) ?? [];
        var currentSet = new HashSet<object>(current, ReferenceEqualityComparer.Instance);
        var previousSet = new HashSet<object>(previous, ReferenceEqualityComparer.Instance);

        foreach (var removed in previous.Where(i => !currentSet.Contains(i)))
            Detach(removed);
        foreach (var added in current.Where(i => !previousSet.Contains(i)))
            Attach(added);

        _collectionItems[sender] = current;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
