using System.Diagnostics.CodeAnalysis;

namespace HWKit
{
    public struct HardwareInfoValue : IEquatable<HardwareInfoValue>, IComparable<HardwareInfoValue>
    {
        public float Value { get; set; } = float.NaN;
        public string Unit { get; set; } = string.Empty;

        public HardwareInfoValue(float value,string unit)
        {
            Value = value; 
            Unit = unit; 
        }
        public readonly bool Equals(HardwareInfoValue other)
        {
            if((float.IsNaN(Value) && float.IsNaN(other.Value) || Value==other.Value))
            {
                return Unit.Equals(other.Unit,StringComparison.Ordinal);
            }
            return false;
        }
        public readonly override bool Equals([NotNullWhen(true)] object? obj)
        {
            if(obj is HardwareInfoValue rhs)
            {
                return Equals(rhs);
            }
            return false;
        }
        public readonly override int GetHashCode()
        {
            return HashCode.Combine(Value, Unit);
        }
        public readonly override string ToString()
        {
            if (float.IsNaN(Value)) return $"---{Unit}";
            return $"{Math.Round(Value, 2).ToString("G")}{Unit}";
        }
        public int CompareTo(HardwareInfoValue other)
        {
            var cmp = Unit.CompareTo(other.Unit);
            if(cmp==0)
            {
                return Value.CompareTo(other.Value);
            }
            return cmp;
        }

        private static HardwareInfoValue _empty = new HardwareInfoValue(float.NaN, string.Empty);
        public static HardwareInfoValue Empty => _empty;

        public static bool operator==(HardwareInfoValue lhs, HardwareInfoValue rhs)
        {
            return lhs.Equals(rhs); 
        }
        public static bool operator !=(HardwareInfoValue lhs, HardwareInfoValue rhs)
        {
            return !lhs.Equals(rhs);
        }
        public static bool operator>(HardwareInfoValue lhs, HardwareInfoValue rhs)
        {
            return lhs.CompareTo(rhs) > 0;  
        }
        public static bool operator <(HardwareInfoValue lhs, HardwareInfoValue rhs)
        {
            return rhs.CompareTo(lhs) > 0;
        }
    }
}
