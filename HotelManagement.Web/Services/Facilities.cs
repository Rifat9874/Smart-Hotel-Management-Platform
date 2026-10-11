namespace HotelManagement.Web.Services;

public sealed record Facility(string Name, string Description, decimal Price, string Unit, int MaxUnits, string UnitLabel, string Icon);

/// <summary>The 5 bookable facilities (same names and prices as the WinForms app).</summary>
public static class FacilityCatalog
{
    public static readonly IReadOnlyList<Facility> All = new[]
    {
        new Facility("Swimming Pool",    "Enjoy our heated swimming pool",                         1000m, "per person",    4, "Guests", "pool"),
        new Facility("Breakfast Buffet", "Start your day with our delicious breakfast buffet",     1200m, "per person",    4, "Guests", "breakfast"),
        new Facility("Spa Services",     "Relax with our premium spa treatments",                  2500m, "per treatment", 2, "Guests", "spa"),
        new Facility("Fitness Center",   "Stay fit with our modern gym equipment",                 1000m, "per person",    1, "Guests", "gym"),
        new Facility("Business Center",  "Access computers, printers, and meeting rooms",           800m, "per hour",      1, "Hours",  "travel"),
    };

    public static Facility? Find(string? name) =>
        All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}
