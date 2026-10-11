namespace HotelManagement.Web.Services;

/// <summary>Hotel name / contact info (edit in appsettings.json, section "Hotel").</summary>
public sealed class HotelInfo
{
    public string Name { get; set; } = "Rifat Grand Hotel";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Server clocks on free hosting are usually US time. Bangladesh = UTC+6.</summary>
    public int UtcOffsetHours { get; set; } = 6;
}

/// <summary>"Now" and "Today" in the hotel's own time zone.</summary>
public sealed class Clock
{
    private readonly int _offset;
    public Clock(Microsoft.Extensions.Options.IOptions<HotelInfo> hotel) => _offset = hotel.Value.UtcOffsetHours;

    public DateTime Now => DateTime.UtcNow.AddHours(_offset);
    public DateTime Today => Now.Date;
}
