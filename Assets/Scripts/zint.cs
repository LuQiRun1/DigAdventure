using System;

public interface IZBase
{
	public object GetObjectValue();
}

public struct zint : IZBase
{
	public static System.Random RndObj = new System.Random();

	private int orig;
	private int mask;
	private int _val;

	private int Value
	{
		get
		{
			int result = _val ^ mask;
			if (orig != result)
			{
				TDEvent.MemError(1, orig.ToString(), result.ToString());
			}
			return result;
		}
		set
		{
			orig = value;
			mask = zint.RndObj.Next();
			_val = value ^ mask;
		}
	}

	public zint(int val = 0)
	{
		_val = 0;
		mask = 0;
		orig = 0;
		Value = val;
	}

	public object GetObjectValue()
	{
		return (object)Value;
	}

	public static implicit operator zint(int val) => new zint(val);
	public static implicit operator int(zint val) => val.Value;

	public static bool operator ==(zint a, zint b) => a.Value == b.Value;
	public static bool operator ==(zint a, int b) => a.Value == b;
	public static bool operator !=(zint a, zint b) => a.Value != b.Value;
	public static bool operator !=(zint a, int b) => a.Value != b;

	public static zint operator ++(zint a)
	{
		a.Value++;
		return a;
	}

	public static zint operator --(zint a)
	{
		a.Value--;
		return a;
	}

	public static zint operator +(zint a, zint b) => new zint(a.Value + b.Value);
	public static zint operator +(zint a, int b) => new zint(a.Value + b);

	public static zint operator -(zint a, zint b) => new zint(a.Value - b.Value);
	public static zint operator -(zint a, int b) => new zint(a.Value - b);

	public static zint operator *(zint a, zint b) => new zint(a.Value * b.Value);
	public static zint operator *(zint a, int b) => new zint(a.Value * b);

	public static zint operator /(zint a, zint b) => new zint(a.Value / b.Value);
	public static zint operator /(zint a, int b) => new zint(a.Value / b);

	public static zint operator %(zint a, zint b) => new zint(a.Value % b.Value);
	public static zint operator %(zint a, int b) => new zint(a.Value % b);

	public override string ToString() => Value.ToString();

	public override int GetHashCode() => Value.GetHashCode();

	public override bool Equals(object obj) => Value.Equals(obj is zint ? ((zint)obj).Value : obj);
}
