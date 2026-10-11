namespace HotelManagement.Web.Services;

/// <summary>Fixed lists and status words used across the app.</summary>
public static class Lists
{
    public static readonly string[] LeaveReasons =
    {
        "Medical Leave", "Family Emergency", "Personal Reason", "Vacation", "Urgent Work at Home",
        "Attending a Wedding", "Childcare Leave", "Mental Health Day", "Religious Festival", "Travel to Native Place"
    };

    public static readonly string[] MaintenanceProblems =
    {
        "Leaking bathroom faucet", "Broken showerhead", "AC not working", "Light bulb fused", "TV remote not functioning",
        "Clogged toilet", "Window won't close properly", "Door lock jammed", "Electric socket loose", "Mini fridge not cooling",
        "Wi-Fi not connecting", "Water heater not working", "Stained or damaged carpet", "Wall paint peeling", "Bad smell from drain"
    };

    public static readonly string[] PaymentMethods = { "Credit/Debit Card", "bKash", "Rocket", "Nagad", "Bank Transfer" };

    /// <summary>Filter shown on the receptionist dashboard.</summary>
    public static readonly string[] BookingStatuses = { "PENDING", "PAID", "BOOKED", "CHECK-IN", "CHECK-OUT", "CANCELLED" };

    public static readonly string[] FacilityStatuses = { "PENDING", "CONFIRMED", "COMPLETED", "CANCELLED" };

    public static readonly string[] UserTypes = { "CUSTOMER", "STAFF", "RECEPTIONIST", "ADMIN" };

    /// <summary>Booking statuses that no longer block a room.</summary>
    public const string InactiveStatuses = "'CHECK-OUT','CANCELLED'";
}
