using HotelManagement.Web.Data;

namespace HotelManagement.Web.Models;

// ---- rooms -------------------------------------------------------------
public sealed class RoomListVm
{
    public List<Row> Rooms { get; set; } = new();
    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
    public string Search { get; set; } = "";
    public string Sort { get; set; } = "";
    public string Show { get; set; } = "";
    public string? Error { get; set; }
    public int Nights => Math.Max(1, (CheckOut - CheckIn).Days);
}

public sealed class RoomDetailsVm
{
    public Row Room { get; set; } = null!;
    public List<string> Images { get; set; } = new();
    public List<Row> BookedRanges { get; set; } = new();
    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
    public DateTime Today { get; set; }
    public bool Available { get; set; }
    public int Nights => Math.Max(1, (CheckOut - CheckIn).Days);
}

// ---- payment -----------------------------------------------------------
public sealed class PayVm
{
    public Row Booking { get; set; } = null!;
    public int Nights { get; set; }
    public int Price { get; set; }
    public decimal Total { get; set; }
    public string Method { get; set; } = "";
    public string? Error { get; set; }
}

// ---- customer profile --------------------------------------------------
public sealed class ProfileVm
{
    public Row Customer { get; set; } = null!;
    public List<Row> Bookings { get; set; } = new();
    public List<Row> ReviewableBookings { get; set; } = new();
    public List<Row> MyReviews { get; set; } = new();
}

public sealed class FacilitiesVm
{
    public List<Row> MyBookings { get; set; } = new();
}

// ---- reception ---------------------------------------------------------
public sealed class ReceptionVm
{
    public List<Row> Bookings { get; set; } = new();
    public string Status { get; set; } = "";
    public string ReceptionistName { get; set; } = "";
}

// ---- employee (staff + receptionist) -----------------------------------
public sealed class EmployeeProfileVm
{
    public Row Person { get; set; } = null!;
    public Row? Salary { get; set; }
    public List<Row> Leaves { get; set; } = new();
    public int AnnualLeave { get; set; } = 20;
    public int UsedLeave { get; set; }
    public int Approved { get; set; }
    public int Pending { get; set; }
    public int Rejected { get; set; }
    public int Remaining => AnnualLeave - UsedLeave;
}

public sealed class StaffVm
{
    public List<Row> Assignments { get; set; } = new();
    public string Search { get; set; } = "";
    public string StaffName { get; set; } = "";
}

public sealed class MyTasksVm
{
    public List<Row> Tasks { get; set; } = new();
    public List<string> MyRooms { get; set; } = new();
    public List<Row> MyRequests { get; set; } = new();
}

// ---- admin -------------------------------------------------------------
public sealed class AdminDashboardVm
{
    public int TotalBookings { get; set; }
    public int OccupiedRooms { get; set; }
    public int TodaysCheckIns { get; set; }
    public decimal TodaysRevenue { get; set; }
    public List<Row> Occupied { get; set; } = new();
    public List<Row> History { get; set; } = new();
}

public sealed class AdminRoomsVm
{
    public List<Row> Maintenance { get; set; } = new();
    public List<Row> Rooms { get; set; } = new();
}

public sealed class AssignTaskVm
{
    public List<Row> StaffList { get; set; } = new();
    public List<string> RoomIds { get; set; } = new();
    public List<Row> Assignments { get; set; } = new();
}

public sealed class UsersVm
{
    public List<Row> Users { get; set; } = new();
    public string Type { get; set; } = "";
}

public sealed class DateRangeVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Type { get; set; } = "Daily";
    public List<Row> Rows { get; set; } = new();
    public decimal TotalRevenue { get; set; }
    public int TotalBookings { get; set; }
}
