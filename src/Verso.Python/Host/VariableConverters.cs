using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verso.Python.Host;

/// <summary>
/// Converters that give a JSON form to the values JSON has none for, so that a date stays a date
/// and an exact decimal stays exact once it reaches Python.
///
/// <para>
/// Every temporal form is written with six fractional digits and, where the value carries one, an
/// explicit numeric offset. Neither detail is cosmetic: CPython before 3.11 refuses both a seven
/// digit fraction, which is what .NET writes by default, and a bare <c>Z</c> suffix. Writing what
/// the oldest supported interpreter accepts costs nothing on the newer ones.
/// </para>
/// </summary>
internal static class VariableConverters
{
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.ffffff";

    internal static string FormatOffset(TimeSpan offset)
    {
        var magnitude = offset.Duration();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(offset < TimeSpan.Zero ? '-' : '+')}{magnitude.Hours:00}:{magnitude.Minutes:00}");
    }

    /// <summary>
    /// Writes a <see cref="DateTime"/>, keeping what it knew about its own zone. A UTC value gains
    /// an explicit <c>+00:00</c> and a local one its actual offset, so both arrive in Python as
    /// aware datetimes; a value that never claimed a zone stays naive rather than acquiring one.
    /// </summary>
    internal sealed class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.DateTime, Format(value));

        internal static string Format(DateTime value)
        {
            var text = value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
            return value.Kind switch
            {
                // TimeZoneInfo is asked for the offset rather than using a format specifier,
                // because a "zzz" specifier reports the machine's offset even for a UTC value.
                DateTimeKind.Utc => text + "+00:00",
                DateTimeKind.Local => text + FormatOffset(TimeZoneInfo.Local.GetUtcOffset(value)),
                _ => text,
            };
        }
    }

    internal sealed class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => WireTag.Write(
                writer,
                WireTag.DateTimeOffset,
                value.ToString(DateTimeFormat, CultureInfo.InvariantCulture) + FormatOffset(value.Offset));
    }

    internal sealed class DateOnlyConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.Date, value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    internal sealed class TimeOnlyConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.Time, value.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
    }

    /// <summary>Travels as total seconds, which is what a Python timedelta is built from.</summary>
    internal sealed class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.TimeSpan, value.TotalSeconds);
    }

    /// <summary>
    /// Travels as text. A decimal written as a JSON number is indistinguishable from a double once
    /// it lands, so the exactness that was the reason for choosing decimal would be lost silently.
    /// </summary>
    internal sealed class DecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.Decimal, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Travels as digits. Left alone it reflects as its own properties, producing an object of
    /// flags where a number was meant.
    /// </summary>
    internal sealed class BigIntegerConverter : JsonConverter<BigInteger>
    {
        public override BigInteger Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.BigInteger, value.ToString(CultureInfo.InvariantCulture));
    }

    internal sealed class GuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.Guid, value.ToString("D", CultureInfo.InvariantCulture));
    }

    internal sealed class ByteArrayConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.Bytes, Convert.ToBase64String(value));
    }

    /// <summary>
    /// Ordinary numbers are written as numbers. NaN and the infinities are tagged instead of
    /// refused, which is what previously discarded the entire array a single one of them sat in.
    /// </summary>
    internal sealed class DoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsFinite(value))
                writer.WriteRawValue(Fractional(value.ToString("R", CultureInfo.InvariantCulture)));
            else
                WireTag.Write(writer, WireTag.Float, NonFiniteName(value));
        }
    }

    /// <summary>
    /// Keeps a whole-numbered fraction looking like one. JSON does not distinguish 3 from 3.0, so
    /// written plainly a column of measurements that happened to land on whole values would arrive
    /// as integers and be typed as such by anything inferring a type from them.
    /// </summary>
    private static string Fractional(string text)
        => text.AsSpan().IndexOfAny('.', 'e', 'E') >= 0 ? text : text + ".0";

    internal sealed class SingleConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        {
            if (float.IsFinite(value))
                writer.WriteRawValue(Fractional(value.ToString("R", CultureInfo.InvariantCulture)));
            else
                WireTag.Write(writer, WireTag.Float, NonFiniteName(value));
        }
    }

    internal static string NonFiniteName(double value)
        => double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity";

    /// <summary>
    /// A query result becomes a list of row objects, which is the shape a DataFrame is built from
    /// directly. Each cell goes back through these same options, so a date column stays a date.
    /// </summary>
    internal sealed class DataTableConverter : JsonConverter<DataTable>
    {
        public override DataTable Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, DataTable value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (DataRow row in value.Rows)
            {
                writer.WriteStartObject();
                foreach (DataColumn column in value.Columns)
                {
                    writer.WritePropertyName(column.ColumnName);
                    var cell = row[column];
                    if (cell is null or DBNull)
                        writer.WriteNullValue();
                    else
                        JsonSerializer.Serialize(writer, cell, cell.GetType(), options);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Writes an explanation in place of a value that describes the program rather than data. It
    /// applies at whatever depth the value sits, so one such property inside an object no longer
    /// costs the whole object.
    /// </summary>
    internal sealed class UnavailableConverter<T> : JsonConverter<T>
    {
        private readonly string _reason;

        public UnavailableConverter(string reason) => _reason = reason;

        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Tagged values are read through HostVariables.FromJson.");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => WireTag.Write(writer, WireTag.Unavailable, _reason);
    }

    internal sealed class UnavailableConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
            => VariableShapes.RefusalReason(typeToConvert) is not null;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(
                typeof(UnavailableConverter<>).MakeGenericType(typeToConvert),
                VariableShapes.RefusalReason(typeToConvert))!;
    }
}
