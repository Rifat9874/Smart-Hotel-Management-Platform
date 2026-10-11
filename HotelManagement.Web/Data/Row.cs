using System.Globalization;

namespace HotelManagement.Web.Data;

/// <summary>
/// One database row. Think of it as a tiny DataRow: row.Str("NAME"), row.Int("PPN") ...
/// Asking for a column the query did not return throws a clear error (easy to debug).
/// </summary>
public sealed class Row
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    internal void Set(string column, object? value) => _values[column] = value is DBNull ? null : value;

    public bool Has(string column) => _values.ContainsKey(column);

    public object? this[string column]
    {
        get
        {
            if (!_values.TryGetValue(column, out var v))
                throw new KeyNotFoundException($"Column '{column}' was not returned by the query.");
            return v;
        }
    }

    public bool IsNull(string column) => this[column] is null;

    public string Str(string column) => Convert.ToString(this[column], CultureInfo.InvariantCulture) ?? "";

    public int Int(string column)
    {
        var v = this[column];
        return v is null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    public decimal Dec(string column)
    {
        var v = this[column];
        return v is null ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }

    public DateTime? Date(string column)
    {
        var v = this[column];
        if (v is null) return null;
        if (v is DateTime dt) return dt;
        return DateTime.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Date formatted like 2026-10-09 (falls back to the raw text if it is not a date).</summary>
    public string DateStr(string column, string format = "yyyy-MM-dd")
    {
        var d = Date(column);
        return d.HasValue ? d.Value.ToString(format, CultureInfo.InvariantCulture) : Str(column);
    }
}
