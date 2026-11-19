using System;

public struct zlong : IZBase
{
	private long orig;
	private int mask;
	private long _val;

	private long Value {
		get {
			long result = _val ^ mask;
			if (orig != result) {
                TDEvent.MemError(2, orig.ToString(), result.ToString());
			}
			return result;
		}
		set {
			orig = value;
			mask = zint.RndObj.Next();
			_val = value ^ mask;
		}
	}

	public zlong(long val = 0)
	{
		_val = 0;
		mask = 0;
		orig = 0;
		Value = val;
	}

	public object GetObjectValue() {
		return (object)Value;
	}

	public static implicit operator zlong(long val) => new zlong(val);
	public static implicit operator long(zlong val) => val.Value;

	public static implicit operator zlong(int val) => new zlong((long)val);
	public static implicit operator int(zlong val) => (int)val.Value;


	public static bool operator ==(zlong a, zlong b) => a.Value == b.Value;
	public static bool operator ==(zlong a, long b) => a.Value == b;
	public static bool operator ==(zlong a, int b) => a.Value == (long)b;
	public static bool operator !=(zlong a, zlong b) => a.Value != b.Value;
	public static bool operator !=(zlong a, long b) => a.Value != b;
	public static bool operator !=(zlong a, int b) => a.Value != (long)b;

	public static zlong operator ++(zlong a) {
		a.Value++;
		return a;
	}

	public static zlong operator --(zlong a) {
		a.Value--;
		return a;
	}

	public static zlong operator + (zlong a, zlong b) => new zlong(a.Value + b.Value);
	public static zlong operator + (zlong a, long b) => new zlong(a.Value + b);
	public static zlong operator + (zlong a, int b) => new zlong(a.Value + b);

	public static zlong operator - (zlong a, zlong b) => new zlong(a.Value - b.Value);
	public static zlong operator - (zlong a, long b) => new zlong(a.Value - b);
	public static zlong operator - (zlong a, int b) => new zlong(a.Value - b);

	public static zlong operator * (zlong a, zlong b) => new zlong(a.Value * b.Value);
	public static zlong operator * (zlong a, long b) => new zlong(a.Value * b);
	public static zlong operator * (zlong a, int b) => new zlong(a.Value * b);

	public static zlong operator / (zlong a, zlong b) => new zlong(a.Value / b.Value);
	public static zlong operator / (zlong a, long b) => new zlong(a.Value / b);
	public static zlong operator / (zlong a, int b) => new zlong(a.Value / b);

	public static zlong operator % (zlong a, zlong b) => new zlong(a.Value % b.Value);
	public static zlong operator % (zlong a, long b) => new zlong(a.Value % b);
	public static zlong operator % (zlong a, int b) => new zlong(a.Value % b);

	public override string ToString() => Value.ToString();

	public override int GetHashCode() => Value.GetHashCode();

	public override bool Equals(object obj) => Value.Equals(obj is zlong ? ((zlong) obj).Value : obj);
}
