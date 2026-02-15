using System.Diagnostics.CodeAnalysis;

namespace HWKit;

public struct HardwareInfoEntry : IEquatable<HardwareInfoEntry>, IComparable<HardwareInfoEntry>
{
    public HardwareInfoEntry(string? path, Func<float> getter, string unit, IHardwareInfoProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(getter,nameof(getter));
        ArgumentNullException.ThrowIfNull(unit,nameof(unit));
        Provider = provider;
        Path = path;
        Getter = getter;
        Unit = unit;
    }
    public HardwareInfoEntry(Func<float> getter, string unit, IHardwareInfoProvider? provider) : this(null,getter,unit,provider) { }
    public IHardwareInfoProvider? Provider { get; }
    public Func<float> Getter { get; }
    public string Unit { get; }
    public string? Path { get; }
    public float Value => Getter();
    public readonly int CompareTo(HardwareInfoEntry other)
    {
        int cmp;
        if(Path==null) {
            if (other.Path == null)
            {
                if (Getter == other.Getter)
                {
                    cmp = Getter.GetHashCode() - other.GetHashCode();
                }
                return Unit.CompareTo(other.Unit);
            } else
            {
                return 1;
            }
        } 
        if (other.Path == null)
        {
            return -1;
        }

        cmp = Path.CompareTo(other.Path);
        if (cmp == 0) {
            if (Getter == other.Getter)
            {
                cmp = Getter.GetHashCode() - other.GetHashCode();
            }
            return Unit.CompareTo(other.Unit);
        }
        
        return cmp;
    }
    public override readonly bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is HardwareInfoEntry other)
        {
            return Equals(other);
        }
        return false;
    }
    public override readonly int GetHashCode() => HashCode.Combine(Path, Unit, Getter);
    public readonly bool Equals(HardwareInfoEntry other)
    {
        if (other.Path != null && Path != null) {
            return ReferenceEquals(other.Getter, Getter) && other.Path.Equals(Path, StringComparison.Ordinal) && other.Unit.Equals(Unit, StringComparison.Ordinal);
        } else if(object.ReferenceEquals(other.Path,Path))
        {
            return ReferenceEquals(other.Getter, Getter) && other.Unit.Equals(Unit, StringComparison.Ordinal);
        }
        return false;
    }

    public override string ToString()
    {
        var value = Value;
        if (Path != null)
        {
            if (float.IsNaN(value))
            {
                return $"{Path} => ---{Unit}";
            }
            return $"{Path} => {Math.Round(Value,2).ToString("G")}{Unit}";
        } else
        {
            if (float.IsNaN(value))
            {
                return $"(computed) => ---{Unit}";
            }
            return $"(computed) => {Math.Round(Value,2).ToString("G")}{Unit}";
        }
    }
    public static HardwareInfoEntry Empty { get; }= new HardwareInfoEntry(()=>float.NaN,"",null);
}
