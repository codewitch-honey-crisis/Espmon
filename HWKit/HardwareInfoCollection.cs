using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace HWKit
{
    public interface IHardwareProviderCollection : ICollection<IHardwareInfoProvider>,INotifyCollectionChanged
    {

    }
    sealed class HardwareInfoTrackingEntry
    {
        public HardwareInfoTrackingEntry(long lastAccess,long duration)
        {
            LastAccess = lastAccess;
            Duration = duration;
            History = new();
        }
        public long LastAccess { get; set; }
        public long Duration { get; set; }
        public List<KeyValuePair<long, HardwareInfoValue>> History { get; }
    }
    public sealed class HardwareInfoCollection : IReadOnlyCollection<HardwareInfoEntry>, IDisposable
    {
        private sealed class ProviderCollection : IHardwareProviderCollection
        {
            HardwareInfoCollection _parent;

            public event NotifyCollectionChangedEventHandler? CollectionChanged;

            internal ProviderCollection(HardwareInfoCollection parent)
            { 
                _parent = parent;
            }
            
            public int Count { get { return _parent._entries.Count; } }
            public bool IsReadOnly { get { return false; } }

            public void Add(IHardwareInfoProvider item)
            {
                _parent.AddProvider(item);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new object[] { item}));
            }

            public void Clear()
            {
                foreach(var item in _parent._entries.Keys)
                {
                    _parent.RemoveProvider(item);
                }
                _parent._entries.Clear();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            public bool Contains(IHardwareInfoProvider item)
            {
                return _parent._entries.ContainsKey(item);
            }

            public void CopyTo(IHardwareInfoProvider[] array, int arrayIndex)
            {
                _parent._entries.Keys.CopyTo(array, arrayIndex);
            }

            public IEnumerator<IHardwareInfoProvider> GetEnumerator()
            {
                return _parent._entries.Keys.GetEnumerator();
            }

            public bool Remove(IHardwareInfoProvider item)
            {
                if (!Contains(item)) return false;
                _parent.RemoveProvider(item);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, new object[] { item }));
                return true;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

        }
        Dictionary<IHardwareInfoProvider,List<HardwareInfoEntry>> _entries = new ();
        Dictionary<HardwareInfoExpression, IEnumerable<HardwareInfoEntry>> _cache = new();
        private bool _isDisposed = false;
        public IHardwareProviderCollection Providers { get; private set; } 
        public int Count { get; }
        Dictionary<HardwareInfoExpression, HardwareInfoTrackingEntry> _trackingState = new();
        void AddProvider(IHardwareInfoProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider, nameof(provider));
            if (_entries.ContainsKey(provider))
            {
                throw new ArgumentException("The provider was already added.", nameof(provider));
            }
            var provEntries = new List<HardwareInfoEntry>();
            _entries.Add(provider, provEntries);
            provider.StateChanged += Provider_StateChanged;
            provider.Published += Provider_Published;
            provider.Revoked += Provider_Revoked;
        }

        private void Provider_StateChanged(object? sender, EventArgs e)
        {
            if(sender is IHardwareInfoProvider provider)
            {
                if(provider.State==HardwareInfoProviderState.Stopped)
                {
                    foreach(var entry in _entries[provider].ToArray())
                    {
                        RevokePath(provider, entry.Path!);
                    }
                }
            }
            _cache.Clear();
        }

        bool RemoveProvider(IHardwareInfoProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider, nameof(provider));
            if (!_entries.ContainsKey(provider))
            {
                return false;
            }
            if (provider.State == HardwareInfoProviderState.Started)
            {

                foreach (var entry in _entries[provider])
                {
                    RevokePath(provider,entry.Path!);
                }
            }
            return true;
        }
        private void Provider_Revoked(object? sender, HardwareInfoProviderRevokedEventArgs e)
        {
            if (sender is IHardwareInfoProvider provider)
            {
                RevokePath(provider, e.Path);
            }

        }

        private void Provider_Published(object? sender, HardwareInfoProviderPublishedEventArgs e)
        {
            if (sender is IHardwareInfoProvider provider)
            {
                HardwareInfoEntry entry = new HardwareInfoEntry(e.Path, e.Getter, e.Unit, e.Provider);
                PublishPath(provider, e.Path, ref entry);
            }
            _cache.Clear();
        }
        void PublishPath(IHardwareInfoProvider provider, string path, ref HardwareInfoEntry entry)
        {
            var entries = _entries[provider];
            _cache.Clear();
            entries.Add(entry);
        }
        private int IndexOfEntry(IHardwareInfoProvider provider, string path)
        {
            var entries = _entries[provider];
            for(var i = 0;i<entries.Count;++i)
            {
                if (entries[i].Path!.Equals(path, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }
        void RevokePath(IHardwareInfoProvider provider, string path)
        {
            var idx = IndexOfEntry(provider,path);
            if (idx<0)
            {
                throw new InvalidOperationException("Unable to revoke path");
            }
            _cache.Clear();
            _entries[provider].RemoveAt(idx);
        }
        public TimeSpan MinimumTrackingInterval { get; set; } = TimeSpan.Zero;
        public void StartAll(bool throwOnError = false)
        {
            if (throwOnError)
            {
                foreach (var prov in _entries.Keys)
                {
                    prov.Start();
                }
            }
            else
            {
                foreach (var prov in _entries.Keys)
                {
                    try
                    {
                        prov.Start();
                    }
                    catch { }
                }
            }
        }
        public HardwareInfoCollection()
        {
            Providers = new ProviderCollection(this);
        }
        public void StopAll()
        {
            var provs = _entries.Keys.ToArray();
            foreach (var prov in provs)
            {
                try
                {
                    prov.Stop();
                }
                catch { }
            }
        }
        public IEnumerable<HardwareInfoValue> Track(HardwareInfoExpression expression, long millis)
        {
            if (millis < 100) throw new ArgumentOutOfRangeException(nameof(millis), "The duration must be at least 100 milliseconds");
            if (millis >= TimeSpan.FromDays(7).TotalMilliseconds) throw new ArgumentOutOfRangeException(nameof(millis), "The duration must be less than 1 week");
            var eval = expression.Evaluate(this);
            var now = (long)TimeSpan.FromTicks(DateTime.Now.Ticks).TotalMilliseconds;
            if (_trackingState.TryGetValue(expression, out var cached))
            {
                
                cached.Duration = millis;
                
                cached.LastAccess = now;
                var history = cached.History;
                if (MinimumTrackingInterval.TotalMilliseconds==0 || cached.History.Count == 0 || cached.History[cached.History.Count - 1].Key + (long)MinimumTrackingInterval.TotalMilliseconds <= now)
                {
                    foreach (var he in eval)
                    {
                        history.Add(new KeyValuePair<long, HardwareInfoValue>(now, new HardwareInfoValue(he.Value, he.Unit)));
                    }
                }
                int i = 0;
                while (i < history.Count)
                {
                    var h = history[i];
                    if (now - h.Key < cached.Duration)
                    {
                        yield return h.Value;
                    }
                    ++i;
                }
                yield break;
            }
            var entry = new HardwareInfoTrackingEntry((long)TimeSpan.FromTicks(DateTime.Now.Ticks).TotalMilliseconds, millis);
            _trackingState.Add(expression, entry);
            foreach (var he in eval)
            {
                var e = new HardwareInfoValue(he.Value, he.Unit);
                entry.History.Add(new KeyValuePair<long, HardwareInfoValue> (now,e));
                yield return e;
            }
        }
        
        public void ExpireTracking()
        {
            var now = (long)TimeSpan.FromTicks(DateTime.Now.Ticks).TotalMilliseconds;
            var toExpire = new List<HardwareInfoExpression>();
            foreach (var entry in _trackingState)
            {
                if (entry.Value.LastAccess - now > entry.Value.Duration)
                {
                    toExpire.Add(entry.Key);
                }
                else
                {
                    var history = entry.Value.History;
                    int toRemove = 0;
                    while (history.Count > toRemove)
                    {
                        var diff = now - history[toRemove].Key;
                        if (diff >= entry.Value.Duration)
                        {
                            ++toRemove;
                        } else
                        {
                            break;
                        }
                    }
                    if(toRemove>0)
                    {
                        history.RemoveRange(0,toRemove);
                    }
                }
            }
            for (var i = 0; i < toExpire.Count; i++)
            {
                _trackingState.Remove(toExpire[i]);
            }
        }
        public IEnumerable<HardwareInfoEntry> Query(HardwareInfoQueryExpression expression)
        {
            ArgumentNullException.ThrowIfNull(expression, nameof(expression));

            if(_cache.TryGetValue(expression,out var cache))
            {
                return cache;
            }
            IEnumerable<HardwareInfoEntry>? result = null;
            if (expression is HardwareInfoPathExpression pathExpr)
            {
                var path = pathExpr.Path;
                if (!string.IsNullOrEmpty(path))
                {
                    foreach (var kvp in _entries)
                    {
                        var subentries = kvp.Value;
                        for (var j = 0; j < subentries.Count; ++j)
                        {
                            var subentry = subentries[j];
                            if (subentry.Path!.Equals(path, StringComparison.Ordinal))
                            {
                                result = [subentry];
                                _cache.Add(expression, result);
                                return result;
                            }
                        }
                    }
                }
                result = [];
                _cache.Add(expression, result);
                return result;
            } else if(expression is HardwareInfoMatchExpression matchExpr)
            {
                var match = matchExpr.Match;
                if(match==null)
                {
                    result = [];
                    _cache.Add(expression, result);
                    return result;
                }
                var list = new List<HardwareInfoEntry>();
                foreach (var kvp in _entries)
                {
                    var subentries = kvp.Value;
                    for (var j = 0; j < subentries.Count; ++j)
                    {
                        var subentry = subentries[j];
                        if (match.IsMatch(subentry.Path!))
                        {
                            list.Add(subentry);
                        }
                    }
                }
                result = list;
                _cache.Add(expression, result);
                return result;
            }
            throw new NotSupportedException("This query expression is not supported");
        }
        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    StopAll();
                }

                _isDisposed = true;
            }
        }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public IEnumerator<HardwareInfoEntry> GetEnumerator()
        {
            foreach(var list in _entries.Values)
            {
                foreach(var entry in list)
                {
                    yield return entry;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
