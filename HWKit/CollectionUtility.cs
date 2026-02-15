using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;

namespace HWKit
{
    public interface IIndexedEnumerator<T> : IEnumerator<KeyValuePair<int, T>>, IEnumerator<T>
    {
        new T Current { get; }
        public int Index { get; }
    }
    public interface IIndexedEnumerable<T> : IEnumerable<KeyValuePair<int, T>>, IEnumerable<T>
    {
        new IIndexedEnumerator<T> GetEnumerator();
    }
    public class LazyIndexedEnumeratorAdapter<T> : IIndexedEnumerator<T>
    {
        int _index;
        int _state = -1;
        private readonly IEnumerator<T> _inner;
        public int Index { get => _index; }
        KeyValuePair<int, T> IEnumerator<KeyValuePair<int, T>>.Current
        {
            get
            {
                CheckState();
                return new KeyValuePair<int, T>(_index, _inner.Current);
            }
        }
        public T Current { get { CheckState(); return _inner.Current; } }
        object? IEnumerator.Current
        {
            get
            {
                CheckState();
                return _inner.Current;
            }
        }
        protected void CheckDisposed()
        {
            if (_inner == null || _state == -3)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
        protected void CheckState()
        {
            CheckDisposed();
            if (_state == -1)
            {
                throw new InvalidOperationException("The cursor is before the beginning");
            }
            else if (_state == -2)
            {
                throw new InvalidOperationException("The cursor is after the end");
            }
        }
        public LazyIndexedEnumeratorAdapter(IEnumerator<T> inner)
        {
            if (inner == null) { throw new ArgumentNullException(nameof(inner)); }
            _inner = inner;
            _index = -1;
            _state = -1;
        }
        public bool MoveNext()
        {
            CheckDisposed();
            if (_state == -2)
            {
                return false;
            }
            else if (_state == -1)
            {
                _state = 0;
            }
            if (_inner.MoveNext())
            {
                ++_index;
                return true;
            }
            // subtle bastard
            ++_index;
            return false;
        }

        public void Reset()
        {
            _inner.Reset();
            _index = -1;
        }
        void IDisposable.Dispose()
        {
            _inner.Dispose();
            _index = -1;
        }
    }
    class IndexedEnumerableAdapter<T> : IIndexedEnumerable<T>
    {
        IEnumerable<T> _inner;
        public IndexedEnumerableAdapter(IEnumerable<T> unindexedContainer)
        {
            if (unindexedContainer == null)
            {
                throw new ArgumentNullException(nameof(unindexedContainer));
            }
            _inner = unindexedContainer;
        }
        public IIndexedEnumerator<T> GetEnumerator()
        {
            return new LazyIndexedEnumeratorAdapter<T>(_inner.GetEnumerator());
        }

        IEnumerator<KeyValuePair<int, T>> IEnumerable<KeyValuePair<int, T>>.GetEnumerator()
        {
            return new LazyIndexedEnumeratorAdapter<T>(_inner.GetEnumerator());
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new LazyIndexedEnumeratorAdapter<T>(_inner.GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new LazyIndexedEnumeratorAdapter<T>(_inner.GetEnumerator());
        }

    }
    class LazyListAdapter<T> : IReadOnlyList<T>, IList<T>
    {
        int _count = -1;
        IIndexedEnumerable<T> _container;
        IIndexedEnumerator<T> _inner;
        public T this[int index]
        {
            get
            {
                _CheckDisposed();
                if (index < 0 || (_count > -1 && index >= _count))
                {
                    throw new IndexOutOfRangeException();
                }
                // have to back up
                if (index < _inner.Index)
                {
                    _InnerReset();
                }
                while (index > _inner.Index)
                {
                    if (!_inner.MoveNext())
                    {
                        throw new IndexOutOfRangeException("Index may be out of range, or the collection has changed");

                    }
                }
                return _inner.Current!;
            }
        }
        private void _ThrowNoSupport()
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("The list is read only");
            }
            throw new NotSupportedException("This operation cannot be performed on this list.");
        }

        public virtual bool Remove(T? item)
        {
            var i = IndexOf(item);
            if (i > -1)
            {
                RemoveAt(i);
                return true;
            }

            return false;
        }

        void _InnerReset()
        {
            _CheckDisposed();
            if (_inner == null)
            {
                _inner = _container.GetEnumerator();
            }
            if (_inner.Index != -1)
            {
                try
                {
                    _inner.Reset();
                }
                catch
                {
                    _inner.Dispose();
                    _inner = _container.GetEnumerator();
                }
            }
        }
        public virtual int Count
        {
            get
            {
                _CheckDisposed();
                if (_count == -1) // need to fetch
                {
                    // Count from current position to end
                    while (_inner.MoveNext()) ;
                    _count = _inner.Index;
                    // Now we know the count and we're at the end
                    // Just reset to beginning - the indexer will handle seeking efficiently
                    _InnerReset(); // This sets Index back to -1
                }
                return _count;
            }
        }

        public virtual bool IsReadOnly { get => true; }

        T IList<T>.this[int index]
        {
            get
            {
                return this[index];
            }
            set
            {
                _ThrowNoSupport();
            }
        }
        void _CheckDisposed()
        {
            if (_inner == null && _container == null)
            {
                throw new ObjectDisposedException(nameof(LazyListAdapter<T>));
            }
        }
        public LazyListAdapter(IIndexedEnumerable<T> container, IIndexedEnumerator<T>? inner = null)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }
            _container = container;
            if (inner == null)
            {
                _inner = _container.GetEnumerator();
            }
            else
            {
                _inner = inner;

            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var delim = "";
            int i = 0;
            foreach (var item in _container)
            {
                sb.Append($"[{i}]: {delim}{{{item}}}");
                delim = " ";
                ++i;
            }
            return sb.ToString();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _container.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _container.GetEnumerator();
        }
        protected virtual bool ItemEquals(T? x, T? y)
        {
            return object.Equals(x, y);
        }
        public virtual int IndexOf(T? item)
        {
            var checkedEnd = -1;
            if (_inner.Index > -1 && (_count < 0 || _inner.Index < _count))
            {
                checkedEnd = _inner.Index;
                if (ItemEquals(item, _inner.Current))
                {
                    return _inner.Index;
                }
                while (_inner.MoveNext())
                {
                    if (!ItemEquals(item, _inner.Current))
                    {
                        break;
                    }
                }

                if (_count > -1)
                {
                    if (_inner.Index != _count)
                    {
                        throw new InvalidOperationException("The list has changed");
                    }
                }
                else
                {
                    _count = _inner.Index;
                    _InnerReset();
                }

            }
            _InnerReset();
            while ((checkedEnd == -1 || _inner.Index < checkedEnd) && _inner.MoveNext())
            {
                if (ItemEquals(item, _inner.Current))
                {
                    return _inner.Index;
                }
            }
            if (_count > -1)
            {
                if (_inner.Index != _count)
                {
                    throw new InvalidOperationException("The list has changed");
                }
            }
            else
            {
                _count = _inner.Index;

            }
            _InnerReset();
            return -1;
        }
        protected void InsertImpl(int index, T? item)
        {
            _ThrowNoSupport();
        }
        public void Insert(int index, T? item)
        {
            InsertImpl(index, item);
        }

        public virtual void RemoveAt(int index)
        {
            _ThrowNoSupport();
        }

        public virtual void Add(T? item)
        {
            _ThrowNoSupport();
        }

        void ICollection<T>.Clear()
        {
            ClearImpl();
        }
        protected virtual void ClearImpl()
        {
            _ThrowNoSupport();
        }
        public virtual bool Contains(T? item)
        {
            if (object.ReferenceEquals(item, null)) return false;
            return IndexOf(item) > -1;
        }

        public void CopyTo(T?[] array, int arrayIndex)
        {
            _InnerReset();
            while (_inner.MoveNext())
            {
                array[arrayIndex++] = _inner.Current;
            }
            if (_count > -1 && _inner.Index != _count)
            {
                throw new InvalidOperationException("The list has changed");
            }
            _count = _inner.Index;
            _InnerReset();
        }
    }
    public interface IObservableCollection<T> : ICollection<T>, INotifyCollectionChanged
    {
        void Refresh();
    }
    sealed class ObservableCollectionAdapter<T> : IObservableCollection<T>
    {
        readonly ICollection<T> _inner;
        public ObservableCollectionAdapter(ICollection<T> inner)
        {
            ArgumentNullException.ThrowIfNull(inner, nameof(inner));
            _inner = inner;
        }

        public int Count { get; }
        public bool IsReadOnly { get; }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public void Add(T item)
        {
            _inner.Add(item);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (T[])[item]));
        }

        public void Clear()
        {
            _inner.Clear();
            Refresh();
        }

        public bool Contains(T item)
        {
            return _inner.Contains(item);  
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _inner.CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _inner.GetEnumerator();
        }

        public void Refresh()
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset, null));
        }

        public bool Remove(T item)
        {
            if (_inner.Remove(item))
            {
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, (T[])[item]));
                return true;
            }
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    sealed class ObservableListAdapter<T> : IObservableCollection<T>, IList<T>
    {
        readonly IList<T> _inner;

        public ObservableListAdapter(IList<T> inner)
        {
            ArgumentNullException.ThrowIfNull(inner, nameof(inner));
            _inner = inner;
        }

        public int Count => _inner.Count;
        public bool IsReadOnly => _inner.IsReadOnly;

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public T this[int index]
        {
            get => _inner[index];
            set
            {
                T oldItem = _inner[index];
                _inner[index] = value;
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    value,
                    oldItem,
                    index));
            }
        }

        public void Add(T item)
        {
            _inner.Add(item);
            int index = _inner.Count - 1;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                item,
                index));
        }

        public void Clear()
        {
            _inner.Clear();
            Refresh();
        }

        public bool Contains(T item)
        {
            return _inner.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _inner.CopyTo(array, arrayIndex);
        }

        public int IndexOf(T item)
        {
            return _inner.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            _inner.Insert(index, item);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                item,
                index));
        }

        public bool Remove(T item)
        {
            int index = _inner.IndexOf(item);
            if (index >= 0)
            {
                _inner.RemoveAt(index);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove,
                    item,
                    index));
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            T item = _inner[index];
            _inner.RemoveAt(index);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                item,
                index));
        }

        public void Refresh()
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    class IndexedEnumeratorList<T> : IReadOnlyList<T>, IList<T>
    {
        int _count = -1;
        IIndexedEnumerable<T> _container;
        IIndexedEnumerator<T> _inner;
        public T this[int index]
        {
            get
            {
                _CheckDisposed();
                if (index < 0 || (_count > -1 && index >= _count))
                {
                    throw new IndexOutOfRangeException();
                }
                // have to back up
                if (index < _inner.Index)
                {
                    _InnerReset();
                }
                while (index > _inner.Index)
                {
                    if (!_inner.MoveNext())
                    {
                        throw new IndexOutOfRangeException("Index may be out of range, or the collection has changed");

                    }
                }
                return _inner.Current!;
            }
        }
        private void _ThrowNoSupport()
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("The list is read only");
            }
            throw new NotSupportedException("This operation cannot be performed on this list.");
        }

        public virtual bool Remove(T? item)
        {
            var i = IndexOf(item);
            if (i > -1)
            {
                RemoveAt(i);
                return true;
            }

            return false;
        }

        void _InnerReset()
        {
            _CheckDisposed();
            if (_inner == null)
            {
                _inner = _container.GetEnumerator();
            }
            if (_inner.Index != -1)
            {
                try
                {
                    _inner.Reset();
                }
                catch
                {
                    _inner.Dispose();
                    _inner = _container.GetEnumerator();
                }
            }
        }
        public virtual int Count
        {
            get
            {
                _CheckDisposed();
                if (_count == -1) // need to fetch
                {
                    // Count from current position to end
                    while (_inner.MoveNext()) ;
                    _count = _inner.Index;
                    // Now we know the count and we're at the end
                    // Just reset to beginning - the indexer will handle seeking efficiently
                    _InnerReset(); // This sets Index back to -1
                }
                return _count;
            }
        }

        public virtual bool IsReadOnly { get => true; }

        T IList<T>.this[int index]
        {
            get
            {
                return this[index];
            }
            set
            {
                SetIndex(index, value);
            }
        }
        protected void SetIndex(int index, T? value)
        {
            _ThrowNoSupport();
        }
        void _CheckDisposed()
        {
            if (_inner == null && _container == null)
            {
                throw new ObjectDisposedException(nameof(IndexedEnumeratorList<T>));
            }
        }
        public IndexedEnumeratorList(IIndexedEnumerable<T> container, IIndexedEnumerator<T>? inner = null)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }
            _container = container;
            if (inner == null)
            {
                _inner = _container.GetEnumerator();
            }
            else
            {
                _inner = inner;

            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var delim = "";
            int i = 0;
            foreach (var item in _container)
            {
                sb.Append($"[{i}]: {delim}{{{item}}}");
                delim = " ";
                ++i;
            }
            return sb.ToString();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _container.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _container.GetEnumerator();
        }
        protected virtual bool ItemEquals(T? x, T? y)
        {
            return object.Equals(x, y);
        }
        public virtual int IndexOf(T? item)
        {
            var checkedEnd = -1;
            if (_inner.Index > -1 && (_count < 0 || _inner.Index < _count))
            {
                checkedEnd = _inner.Index;
                if (ItemEquals(item, _inner.Current))
                {
                    return _inner.Index;
                }
                while (_inner.MoveNext())
                {
                    if (!ItemEquals(item, _inner.Current))
                    {
                        break;
                    }
                }

                if (_count > -1)
                {
                    if (_inner.Index != _count)
                    {
                        throw new InvalidOperationException("The list has changed");
                    }
                }
                else
                {
                    _count = _inner.Index;
                    _InnerReset();
                }

            }
            _InnerReset();
            while ((checkedEnd == -1 || _inner.Index < checkedEnd) && _inner.MoveNext())
            {
                if (ItemEquals(item, _inner.Current))
                {
                    return _inner.Index;
                }
            }
            if (_count > -1)
            {
                if (_inner.Index != _count)
                {
                    throw new InvalidOperationException("The list has changed");
                }
            }
            else
            {
                _count = _inner.Index;

            }
            _InnerReset();
            return -1;
        }
        protected void InsertImpl(int index, T? item)
        {
            _ThrowNoSupport();
        }
        public void Insert(int index, T? item)
        {
            InsertImpl(index, item);
        }

        public virtual void RemoveAt(int index)
        {
            _ThrowNoSupport();
        }

        public virtual void Add(T? item)
        {
            _ThrowNoSupport();
        }

        void ICollection<T>.Clear()
        {
            ClearImpl();
        }
        protected virtual void ClearImpl()
        {
            _ThrowNoSupport();
        }
        public virtual bool Contains(T? item)
        {
            if (object.ReferenceEquals(item, null)) return false;
            return IndexOf(item) > -1;
        }

        public void CopyTo(T?[] array, int arrayIndex)
        {
            _InnerReset();
            while (_inner.MoveNext())
            {
                array[arrayIndex++] = _inner.Current;
            }
            if (_count > -1 && _inner.Index != _count)
            {
                throw new InvalidOperationException("The list has changed");
            }
            _count = _inner.Index;
            _InnerReset();
        }

    }
    public static class CollectionUtility 
    {
        public static IList<T> ToObservableList<T>(this IList<T> list)
        {
            ArgumentNullException.ThrowIfNull(list, nameof(list));
            if (list is IObservableCollection<T>) return list;
            return new ObservableListAdapter<T>(list);
        }

        public static ICollection<T> ToObservable<T>(this ICollection<T> collection)
        {
            ArgumentNullException.ThrowIfNull(collection, nameof(collection));
            if (collection is IObservableCollection<T>) return collection;
            if (collection is IList<T> list) return new ObservableListAdapter<T>(list);
            return new ObservableCollectionAdapter<T>(collection);
        }
        public static IList<T> ToLazyList<T>(this IEnumerable<T> enumerable)
        {
            ArgumentNullException.ThrowIfNull(enumerable,nameof(enumerable));
            if(enumerable is IList<T>) return (IList<T>)enumerable;
            if(enumerable is IIndexedEnumerable<T> indexedEnumerable)
            {
                return new LazyListAdapter<T>(indexedEnumerable);
            }
            return new LazyListAdapter<T>(new IndexedEnumerableAdapter<T>(enumerable));
        }
    }
}
