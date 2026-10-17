using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Idle.Core;

/// <summary>A nonnegative decimal floating-point value with an extended exponent range.</summary>
[JsonConverter(typeof(BigValueJsonConverter))]
public readonly struct BigValue : IComparable<BigValue>, IEquatable<BigValue>
{
    public double Mantissa { get; }
    public long Exponent { get; }
    public bool IsZero => Mantissa == 0;
    public static BigValue Zero => default;
    public static BigValue One => new(1, 0);

    public BigValue(double mantissa, long exponent)
    {
        if (!double.IsFinite(mantissa) || mantissa < 0)
            throw new ArgumentOutOfRangeException(nameof(mantissa), "The mantissa must be finite and nonnegative.");
        if (mantissa == 0)
        {
            Mantissa = 0;
            Exponent = 0;
            return;
        }

        var shift = (int)Math.Floor(Math.Log10(mantissa));
        // Scaling in two steps also handles subnormal doubles (10^-324 underflows).
        if (shift < -300)
        {
            mantissa *= 1e300;
            exponent = checked(exponent - 300);
            shift = (int)Math.Floor(Math.Log10(mantissa));
        }
        mantissa /= Math.Pow(10, shift);
        exponent = checked(exponent + shift);
        if (mantissa >= 10)
        {
            mantissa /= 10;
            exponent = checked(exponent + 1);
        }
        else if (mantissa < 1)
        {
            mantissa *= 10;
            exponent = checked(exponent - 1);
        }
        Mantissa = mantissa;
        Exponent = exponent;
    }

    public static BigValue FromDouble(double value) => new(value, 0);

    public static BigValue Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (text.Length == 0) throw new FormatException("An amount is required.");
        var parts = text.Split(['e', 'E']);
        if (parts.Length > 2) throw new FormatException("Invalid scientific notation.");
        var coefficient = double.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture);
        var exponent = parts.Length == 2
            ? long.Parse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            : 0;
        return new BigValue(coefficient, exponent);
    }

    public static BigValue Pow(double value, double power)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (!double.IsFinite(power)) throw new ArgumentOutOfRangeException(nameof(power));
        if (power == 0) return One;
        if (value == 0)
        {
            if (power < 0) throw new DivideByZeroException();
            return Zero;
        }
        var logarithm = Math.Log10(value) * power;
        if (!double.IsFinite(logarithm) || logarithm >= long.MaxValue || logarithm <= long.MinValue)
            throw new OverflowException("The amount exceeds the exponent range.");
        var exponent = (long)Math.Floor(logarithm);
        return new BigValue(Math.Pow(10, logarithm - exponent), exponent);
    }

    public BigValue Floor()
    {
        if (IsZero || Exponent < 0) return Zero;
        if (Exponent >= 16) return this;
        // Read the round-trip decimal representation rather than multiplying two
        // binary doubles: 1.13 * 100 may otherwise become 112.99999999999999.
        var scaled = decimal.Parse(Mantissa.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        for (var index = 0L; index < Exponent; index++) scaled *= 10;
        return FromDouble((double)decimal.Floor(scaled));
    }

    public BigValue Sqrt()
    {
        if (IsZero) return Zero;
        if (Exponent is >= 0 and <= 15)
        {
            var scaled = decimal.Parse(Mantissa.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            for (var index = 0L; index < Exponent; index++) scaled *= 10;
            return FromDouble(Math.Sqrt((double)scaled));
        }
        var exponent = Exponent / 2;
        var remainder = Exponent % 2;
        if (remainder < 0)
        {
            exponent--;
            remainder += 2;
        }
        return new BigValue(Math.Sqrt(Mantissa * (remainder == 1 ? 10 : 1)), exponent);
    }

    /// <summary>Converts to a finite double, saturating values outside its representable range.</summary>
    public double ToDouble()
    {
        if (IsZero || Exponent < -324) return 0;
        if (Exponent > 308) return double.MaxValue;
        var value = Exponent < -300
            ? Mantissa * 1e-300 * Math.Pow(10, Exponent + 300)
            : Mantissa * Math.Pow(10, Exponent);
        return double.IsFinite(value) ? value : double.MaxValue;
    }

    public string Format()
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        if (IsZero) return "0";
        if (Exponent < 3 && Exponent >= -2) return ToDouble().ToString("0.##", culture);
        string[] suffixes = ["", "mil", "mi", "bi", "tri", "qua", "qui"];
        if (Exponent >= 3 && Exponent < suffixes.Length * 3)
        {
            var group = (int)(Exponent / 3);
            return (Mantissa * Math.Pow(10, Exponent % 3)).ToString("0.##", culture) + " " + suffixes[group];
        }
        return Mantissa.ToString("0.##", culture) + "e" + Exponent.ToString(CultureInfo.InvariantCulture);
    }

    public override string ToString() => IsZero ? "0" : Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" + Exponent.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(BigValue other)
    {
        if (IsZero) return other.IsZero ? 0 : -1;
        if (other.IsZero) return 1;
        var exponentComparison = Exponent.CompareTo(other.Exponent);
        return exponentComparison == 0 ? Mantissa.CompareTo(other.Mantissa) : exponentComparison;
    }

    public bool Equals(BigValue other) => Mantissa == other.Mantissa && Exponent == other.Exponent;
    public override bool Equals(object? obj) => obj is BigValue other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Mantissa, Exponent);

    public static BigValue operator +(BigValue left, BigValue right)
    {
        if (left.IsZero) return right;
        if (right.IsZero) return left;
        if (left < right) (left, right) = (right, left);
        // Compare before subtracting to avoid overflow for extreme exponents.
        if ((double)left.Exponent - right.Exponent > 17) return left;
        return new BigValue(left.Mantissa + right.Mantissa * Math.Pow(10, right.Exponent - left.Exponent), left.Exponent);
    }

    public static BigValue operator -(BigValue left, BigValue right)
    {
        if (left <= right) return Zero;
        if (right.IsZero || (double)left.Exponent - right.Exponent > 17) return left;
        return new BigValue(Math.Max(0, left.Mantissa - right.Mantissa * Math.Pow(10, right.Exponent - left.Exponent)), left.Exponent);
    }

    public static BigValue operator *(BigValue left, BigValue right) => left.IsZero || right.IsZero
        ? Zero : new BigValue(left.Mantissa * right.Mantissa, checked(left.Exponent + right.Exponent));

    public static BigValue operator /(BigValue left, BigValue right)
    {
        if (right.IsZero) throw new DivideByZeroException();
        return left.IsZero ? Zero : new BigValue(left.Mantissa / right.Mantissa, checked(left.Exponent - right.Exponent));
    }

    public static BigValue operator +(BigValue left, double right) => left + FromDouble(right);
    public static BigValue operator +(double left, BigValue right) => FromDouble(left) + right;
    public static BigValue operator -(BigValue left, double right) => left - FromDouble(right);
    public static BigValue operator -(double left, BigValue right) => FromDouble(left) - right;
    public static BigValue operator *(BigValue left, double right) => left * FromDouble(right);
    public static BigValue operator *(double left, BigValue right) => FromDouble(left) * right;
    public static BigValue operator /(BigValue left, double right) => left / FromDouble(right);
    public static BigValue operator /(double left, BigValue right) => FromDouble(left) / right;
    public static bool operator ==(BigValue left, BigValue right) => left.Equals(right);
    public static bool operator !=(BigValue left, BigValue right) => !left.Equals(right);
    public static bool operator <(BigValue left, BigValue right) => left.CompareTo(right) < 0;
    public static bool operator >(BigValue left, BigValue right) => left.CompareTo(right) > 0;
    public static bool operator <=(BigValue left, BigValue right) => left.CompareTo(right) <= 0;
    public static bool operator >=(BigValue left, BigValue right) => left.CompareTo(right) >= 0;
    public static bool operator ==(BigValue left, double right) => left == FromDouble(right);
    public static bool operator !=(BigValue left, double right) => left != FromDouble(right);
    public static bool operator <(BigValue left, double right) => left < FromDouble(right);
    public static bool operator >(BigValue left, double right) => left > FromDouble(right);
    public static bool operator <=(BigValue left, double right) => left <= FromDouble(right);
    public static bool operator >=(BigValue left, double right) => left >= FromDouble(right);
    public static bool operator ==(double left, BigValue right) => FromDouble(left) == right;
    public static bool operator !=(double left, BigValue right) => FromDouble(left) != right;
    public static bool operator <(double left, BigValue right) => FromDouble(left) < right;
    public static bool operator >(double left, BigValue right) => FromDouble(left) > right;
    public static bool operator <=(double left, BigValue right) => FromDouble(left) <= right;
    public static bool operator >=(double left, BigValue right) => FromDouble(left) >= right;
}

public sealed class BigValueJsonConverter : JsonConverter<BigValue>
{
    public override BigValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Amounts must use invariant scientific-notation strings.");
        try { return BigValue.Parse(reader.GetString()!); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { throw new JsonException("Invalid amount.", ex); }
    }

    public override void Write(Utf8JsonWriter writer, BigValue value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
