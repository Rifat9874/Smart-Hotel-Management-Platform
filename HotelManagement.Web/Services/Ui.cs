using System.Globalization;
using System.Security.Claims;

namespace HotelManagement.Web.Services;

public static class Ui
{
    public static string Money(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture) + " BDT";
    public static string Tk(decimal v) => "TK " + v.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>CSS class for a coloured status pill.</summary>
    public static string Badge(string? status)
    {
        var s = (status ?? "").Trim().ToUpperInvariant();
        return s switch
        {
            "PAID" or "APPROVED" or "COMPLETE" or "COMPLETED" or "CLEANED" or "CONFIRMED" => "b-ok",
            "PENDING" or "DIRTY" or "BOOKED" => "b-warn",
            "CHECK-IN" or "WORK IN PROGRESS" or "WORK IN PROGESS" or "IN PROGRESS" => "b-info",
            "CHECK-OUT" => "b-muted",
            "CANCELLED" or "DISMISSED" or "REJECTED" => "b-bad",
            _ => "b-muted"
        };
    }

    public static string Stars(int rating)
    {
        rating = Math.Clamp(rating, 0, 5);
        return new string('\u2605', rating) + new string('\u2606', 5 - rating);
    }

    /// <summary>Masks a card/phone-like detail for display.</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant() : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    public static string D(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
}

public static class UserExtensions
{
    public static string Email(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Email) ?? "";
    public static string FullName(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Name) ?? "";
    public static string Role(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Role) ?? "";
}

/// <summary>Input checks reused by several pages (same rules as the WinForms forms).</summary>
public static class Check
{
    public static bool Email(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 50) return false;
        try { return new System.Net.Mail.MailAddress(s).Address == s; }
        catch { return false; }
    }

    /// <summary>Bangladesh mobile style: exactly 11 digits (same rule as the registration form).</summary>
    public static bool Phone(string? s) => s != null && s.Length == 11 && s.All(char.IsDigit);

    public static bool Password(string? s) => s != null && s.Length >= 8 && s.Length <= 100;

    public static bool TryDate(string? s, out DateTime date) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
