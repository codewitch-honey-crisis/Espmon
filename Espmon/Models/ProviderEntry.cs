using HWKit;

using System;
using System.ComponentModel;

namespace Espmon;

internal class ProviderEntry : IComparable<ProviderEntry>
{
    public ProviderEntry(IHardwareInfoProvider provider)
    {
        Provider = provider;
    }
    public IHardwareInfoProvider Provider { get; }
    public string Name => Provider.DisplayName;
    public int CompareTo(ProviderEntry? other)
    {
        if (other == null) return 1;
        return Name.CompareTo(other.Name);
    }
}
