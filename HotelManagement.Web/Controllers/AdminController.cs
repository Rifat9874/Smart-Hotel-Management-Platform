using System.Globalization;
using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>Admin_Dashboard.cs + Handle_Leave_RequestcsAdmin.cs + Handle_Rooms_Admin.cs + Assign_Staff_Task.cs + Manage_Users_Admin.cs.</summary>
[Authorize(Roles = "ADMIN")]
public class AdminController : AppController
{
    public AdminController(Db db, Clock clock) : base(db, clock) { }

    // Nights of a booking (at least 1) and the day the booking was made. BOOKING_DATE is stored as text "yyyy-MM-dd HH:mm:ss".
    private const string Nights = "(CASE WHEN DATEDIFF(DAY, B.CHECK_IN, B.CHECK_OUT) < 1 THEN 1 ELSE DATEDIFF(DAY, B.CHECK_IN, B.CHECK_OUT) END)";
    private const string BookedOn = "CAST(TRY_CONVERT(datetime, B.BOOKING_DATE, 120) AS date)";
    // Unpaid (PENDING) and cancelled bookings are not revenue.
    private const string RevenueStatuses = "B.BOOKING_STATUS NOT IN ('PENDING', 'CANCELLED')";

    // =================================================================== dashboard
    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var today = Clock.Today;
        var vm = new AdminDashboardVm
        {
            TotalBookings = await Db.ScalarAsync<int>("SELECT COUNT(*) FROM BOOKINGS_TABLE"),
            OccupiedRooms = await Db.ScalarAsync<int>("SELECT COUNT(*) FROM BOOKINGS_TABLE WHERE BOOKING_STATUS = 'CHECK-IN'"),
            TodaysCheckIns = await Db.ScalarAsync<int>(
                "SELECT COUNT(*) FROM BOOKINGS_TABLE WHERE CHECK_IN = @today AND BOOKING_STATUS <> 'CANCELLED'", new { today }),
            TodaysRevenue = await Db.ScalarAsync<decimal>(
                $@"SELECT ISNULL(SUM({Nights} * R.PPN), 0)
                   FROM BOOKINGS_TABLE B JOIN ROOM_TABLE R ON R.ROOM_ID = B.ROOM_ID
                   WHERE {BookedOn} = @today AND {RevenueStatuses}", new { today }),
            Occupied = await Db.QueryAsync(
                @"SELECT B.ROOM_ID, U.NAME, B.CHECK_IN, B.CHECK_OUT
                  FROM BOOKINGS_TABLE B JOIN USER_TABLE U ON U.EMAIL = B.EMAIL
                  WHERE B.BOOKING_STATUS = 'CHECK-IN' ORDER BY B.ROOM_ID"),
            History = await Db.QueryAsync(
                @"SELECT TOP 100 B.BOOKING_ID, B.ROOM_ID, U.NAME, B.BOOKING_DATE, B.BOOKING_STATUS
                  FROM BOOKINGS_TABLE B JOIN USER_TABLE U ON U.EMAIL = B.EMAIL
                  ORDER BY TRY_CAST(B.BOOKING_ID AS INT) DESC, B.BOOKING_ID DESC")
        };
        return View(vm);
    }

    // ======================================================================== leave
    [HttpGet]
    public async Task<IActionResult> Leave()
    {
        var rows = await Db.QueryAsync(
            @"SELECT L.LEAVE_ID, U.NAME, U.USER_TYPE, L.FROM_DATE, L.TO_DATE, L.REASON, L.STATUS
              FROM LEAVE_REQUEST L JOIN USER_TABLE U ON U.EMAIL = L.EMAIL
              ORDER BY CASE WHEN L.STATUS = 'Pending' THEN 0 ELSE 1 END, L.LEAVE_ID DESC");
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> DecideLeave(string id, string? decision)
    {
        decision = (decision ?? "").ToUpperInvariant();
        if (decision != "APPROVED" && decision != "DISMISSED") { FlashError("Invalid decision."); return RedirectToAction(nameof(Leave)); }

        var n = await Db.ExecAsync(
            "UPDATE LEAVE_REQUEST SET STATUS = @decision WHERE LEAVE_ID = @id AND STATUS = 'Pending'", new { decision, id });
        if (n > 0) FlashOk($"Leave request {id} {decision.ToLowerInvariant()}.");
        else FlashError("That request was not found or has already been decided.");
        return RedirectToAction(nameof(Leave));
    }

    // ================================================================ rooms + repairs
    [HttpGet]
    public async Task<IActionResult> Rooms()
    {
        var vm = new AdminRoomsVm
        {
            Maintenance = await Db.QueryAsync(
                @"SELECT M.M_ID, M.ROOM_ID, M.PROBLEM, M.STATUS, M.SUBMIT_DATE, U.NAME
                  FROM MaintenanceRequests M JOIN USER_TABLE U ON U.EMAIL = M.EMAIL
                  WHERE M.STATUS <> 'COMPLETE' ORDER BY M.SUBMIT_DATE DESC"),
            Rooms = await Db.QueryAsync("SELECT ROOM_ID, [TYPE], PPN FROM ROOM_TABLE ORDER BY ROOM_ID")
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> MaintenanceStatus(string id, string? status)
    {
        status = (status ?? "").ToUpperInvariant();
        if (status != "WORK IN PROGRESS" && status != "COMPLETE") { FlashError("Invalid status."); return RedirectToAction(nameof(Rooms)); }

        var n = await Db.ExecAsync("UPDATE MaintenanceRequests SET STATUS = @status WHERE M_ID = @id", new { status, id });
        if (n > 0) FlashOk($"Request {id} set to {status}.");
        else FlashError("Request not found.");
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost]
    public async Task<IActionResult> SetPrice(string roomId, int price)
    {
        if (price < 0 || price > 10_000_000) { FlashError("Please enter a sensible price."); return RedirectToAction(nameof(Rooms)); }
        var n = await Db.ExecAsync("UPDATE ROOM_TABLE SET PPN = @price WHERE ROOM_ID = @roomId", new { price, roomId });
        if (n > 0) FlashOk($"Price of room {roomId} updated.");
        else FlashError("Room not found.");
        return RedirectToAction(nameof(Rooms));
    }

    // ============================================================== assign housekeeping
    [HttpGet]
    public async Task<IActionResult> AssignTask()
    {
        var vm = new AssignTaskVm
        {
            StaffList = await Db.QueryAsync("SELECT EMAIL, NAME FROM USER_TABLE WHERE USER_TYPE = 'STAFF' ORDER BY NAME"),
            RoomIds = (await Db.QueryAsync("SELECT ROOM_ID FROM ROOM_TABLE ORDER BY ROOM_ID")).Select(r => r.Str("ROOM_ID")).ToList(),
            Assignments = await Db.QueryAsync(
                @"SELECT TOP 100 A.ASSIGNMENT_ID, A.ROOM_ID, U.NAME, A.ASSIGNED_DATE, A.ROOM_STATUS
                  FROM ROOM_ASSIGNMENTS A JOIN USER_TABLE U ON U.EMAIL = A.EMAIL
                  ORDER BY TRY_CAST(A.ASSIGNMENT_ID AS INT) DESC, A.ASSIGNMENT_ID DESC")
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> AssignTask(string? staffEmail, string? roomId)
    {
        var okStaff = await Db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM USER_TABLE WHERE EMAIL = @staffEmail AND USER_TYPE = 'STAFF'", new { staffEmail });
        var okRoom = await Db.ScalarAsync<int>("SELECT COUNT(*) FROM ROOM_TABLE WHERE ROOM_ID = @roomId", new { roomId });
        if (okStaff == 0 || okRoom == 0) { FlashError("Please select a staff member and a room."); return RedirectToAction(nameof(AssignTask)); }

        await using (var tx = await Db.BeginAsync())
        {
            var next = await tx.ScalarAsync<int>(NextNumericIdSql("ROOM_ASSIGNMENTS", "ASSIGNMENT_ID"));
            await tx.ExecAsync(
                @"INSERT INTO ROOM_ASSIGNMENTS (ASSIGNMENT_ID, ROOM_ID, ASSIGNED_DATE, EMAIL, ROOM_STATUS)
                  VALUES (@aid, @roomId, @today, @staffEmail, 'DIRTY')",
                new { aid = next.ToString("D4", CultureInfo.InvariantCulture), roomId, today = Clock.Today, staffEmail });
            await tx.CommitAsync();
        }
        FlashOk($"Room {roomId} assigned.");
        return RedirectToAction(nameof(AssignTask));
    }

    // ===================================================================== users
    [HttpGet]
    public async Task<IActionResult> Users(string? type)
    {
        var filter = Lists.UserTypes.FirstOrDefault(t => t.Equals(type, StringComparison.OrdinalIgnoreCase)) ?? "";
        var vm = new UsersVm
        {
            Type = filter,
            Users = await Db.QueryAsync(
                @"SELECT NAME, PHONE_NUMBER, EMAIL, USER_TYPE FROM USER_TABLE
                  WHERE (@type = '' OR USER_TYPE = @type) ORDER BY USER_TYPE, NAME", new { type = filter })
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(string email)
    {
        var u = await Db.QueryOneAsync("SELECT NAME, PHONE_NUMBER, EMAIL, USER_TYPE FROM USER_TABLE WHERE EMAIL = @email", new { email });
        return u == null ? NotFound() : View(u);
    }

    [HttpPost]
    public async Task<IActionResult> EditUser(string email, string? name, string? phone, string? password)
    {
        name = (name ?? "").Trim();
        phone = (phone ?? "").Trim();

        string? error = null;
        if (name.Length == 0 || name.Length > 50) error = "Invalid Name";
        else if (!Check.Phone(phone)) error = "Invalid Number (11 digits)";
        else if (!string.IsNullOrEmpty(password) && !Check.Password(password)) error = "A new password must be at least 8 characters.";

        if (error != null)
        {
            ViewData["Error"] = error;
            return View(await Db.QueryOneAsync("SELECT NAME, PHONE_NUMBER, EMAIL, USER_TYPE FROM USER_TABLE WHERE EMAIL = @email", new { email }));
        }

        var n = await Db.ExecAsync("UPDATE USER_TABLE SET NAME = @name, PHONE_NUMBER = @phone WHERE EMAIL = @email", new { name, phone, email });
        if (n == 0) return NotFound();
        if (!string.IsNullOrEmpty(password))
            await Db.ExecAsync("UPDATE USER_TABLE SET PASSWORD = @pw WHERE EMAIL = @email", new { pw = PasswordService.Hash(password), email });

        FlashOk("User updated.");
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public IActionResult AddEmployee() => View();

    [HttpPost]
    public async Task<IActionResult> AddEmployee(string? type, string? name, string? phone, string? email, string? password,
                                                 string? department, string? position, string? address, decimal salary)
    {
        name = (name ?? "").Trim();
        phone = (phone ?? "").Trim();
        email = (email ?? "").Trim();
        type = (type ?? "").Trim().ToUpperInvariant();

        string? error = null;
        if (type != "STAFF" && type != "RECEPTIONIST") error = "Please choose Staff or Receptionist.";
        else if (name.Length == 0 || name.Length > 50) error = "Invalid Name";
        else if (!Check.Phone(phone)) error = "Invalid Number (11 digits)";
        else if (!Check.Email(email)) error = "Invalid Email";
        else if (!Check.Password(password)) error = "Password must be at least 8 characters!";
        else if (salary <= 0 || salary > 99_999_999) error = "Please enter a valid salary.";
        else if ((department ?? "").Length > 100 || (position ?? "").Length > 100 || (address ?? "").Length > 255) error = "A text field is too long.";
        else if (await Db.ScalarAsync<int>("SELECT COUNT(*) FROM USER_TABLE WHERE EMAIL = @email", new { email }) > 0)
            error = "This email is already registered.";

        if (error != null)
        {
            ViewData["Error"] = error;
            return View();
        }

        await using (var tx = await Db.BeginAsync())
        {
            await tx.ExecAsync(
                @"INSERT INTO USER_TABLE (NAME, PHONE_NUMBER, EMAIL, PASSWORD, USER_TYPE, ADDRESS, DEPARTMENT, POSITION, JOIN_DATE)
                  VALUES (@name, @phone, @email, @pw, @type, @address, @department, @position, @joined)",
                new
                {
                    name, phone, email, pw = PasswordService.Hash(password!), type,
                    address = Blank(address), department = Blank(department), position = Blank(position), joined = Clock.Today
                });
            await tx.ExecAsync(
                @"INSERT INTO EMPLOYEE_SALARY (EMPLOYEE_EMAIL, SALARY_AMOUNT, PAYMENT_DATE, PAYMENT_METHOD, ALLOWANCES, DEDUCTIONS, BONUS_AMOUNT, BONUS_REASON)
                  VALUES (@email, @salary, @today, 'Bank Transfer', 0, 0, 0, NULL)",
                new { email, salary, today = Clock.Today });
            await tx.CommitAsync();
        }

        FlashOk($"{type} {name} was added.");
        return RedirectToAction(nameof(Staff));
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ------------------------------------------------------ staff, salary and bonus
    [HttpGet]
    public async Task<IActionResult> Staff()
    {
        var rows = await Db.QueryAsync(
            @"SELECT U.NAME, U.EMAIL, U.USER_TYPE, U.PHONE_NUMBER, U.DEPARTMENT, U.POSITION, U.JOIN_DATE,
                     S.SALARY_AMOUNT, ISNULL(S.ALLOWANCES, 0) AS ALLOWANCES, ISNULL(S.DEDUCTIONS, 0) AS DEDUCTIONS,
                     ISNULL(S.BONUS_AMOUNT, 0) AS BONUS_AMOUNT,
                     (ISNULL(S.SALARY_AMOUNT, 0) + ISNULL(S.ALLOWANCES, 0) + ISNULL(S.BONUS_AMOUNT, 0) - ISNULL(S.DEDUCTIONS, 0)) AS NET_SALARY
              FROM USER_TABLE U
              OUTER APPLY (SELECT TOP 1 E.SALARY_AMOUNT, E.ALLOWANCES, E.DEDUCTIONS, E.BONUS_AMOUNT
                           FROM EMPLOYEE_SALARY E WHERE E.EMPLOYEE_EMAIL = U.EMAIL
                           ORDER BY E.PAYMENT_DATE DESC, E.SALARY_ID DESC) S
              WHERE U.USER_TYPE IN ('STAFF', 'RECEPTIONIST')
              ORDER BY U.USER_TYPE, U.NAME");
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateSalary(string email, decimal salary)
    {
        if (salary <= 0 || salary > 99_999_999) { FlashError("Please enter a valid salary."); return RedirectToAction(nameof(Staff)); }

        var exists = await Db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM USER_TABLE WHERE EMAIL = @email AND USER_TYPE IN ('STAFF', 'RECEPTIONIST')", new { email });
        if (exists == 0) { FlashError("Employee not found."); return RedirectToAction(nameof(Staff)); }

        var n = await Db.ExecAsync(
            @"UPDATE EMPLOYEE_SALARY SET SALARY_AMOUNT = @salary, PAYMENT_DATE = @today
              WHERE SALARY_ID = (SELECT TOP 1 SALARY_ID FROM EMPLOYEE_SALARY WHERE EMPLOYEE_EMAIL = @email
                                 ORDER BY PAYMENT_DATE DESC, SALARY_ID DESC)",
            new { salary, today = Clock.Today, email });
        if (n == 0)
        {
            // employee had no salary row yet
            await Db.ExecAsync(
                @"INSERT INTO EMPLOYEE_SALARY (EMPLOYEE_EMAIL, SALARY_AMOUNT, PAYMENT_DATE, PAYMENT_METHOD, ALLOWANCES, DEDUCTIONS, BONUS_AMOUNT, BONUS_REASON)
                  VALUES (@email, @salary, @today, 'Bank Transfer', 0, 0, 0, NULL)",
                new { email, salary, today = Clock.Today });
        }
        FlashOk("Salary updated.");
        return RedirectToAction(nameof(Staff));
    }

    [HttpPost]
    public async Task<IActionResult> AddBonus(string email, decimal bonus, string? reason)
    {
        if (bonus <= 0 || bonus > 99_999_999) { FlashError("Please enter a valid bonus amount."); return RedirectToAction(nameof(Staff)); }
        reason = (reason ?? "").Trim();
        if (reason.Length == 0 || reason.Length > 100) { FlashError("Please give a reason (up to 100 characters)."); return RedirectToAction(nameof(Staff)); }

        var n = await Db.ExecAsync(
            @"UPDATE EMPLOYEE_SALARY SET BONUS_AMOUNT = ISNULL(BONUS_AMOUNT, 0) + @bonus, BONUS_REASON = @reason, PAYMENT_DATE = @today
              WHERE SALARY_ID = (SELECT TOP 1 SALARY_ID FROM EMPLOYEE_SALARY WHERE EMPLOYEE_EMAIL = @email
                                 ORDER BY PAYMENT_DATE DESC, SALARY_ID DESC)",
            new { bonus, reason, today = Clock.Today, email });
        if (n > 0) FlashOk("Bonus added.");
        else FlashError("This employee has no salary record yet. Set a salary first.");
        return RedirectToAction(nameof(Staff));
    }

    [HttpGet]
    public async Task<IActionResult> BonusHistory(string email)
    {
        var who = await Db.QueryOneAsync("SELECT NAME, EMAIL FROM USER_TABLE WHERE EMAIL = @email", new { email });
        if (who == null) return NotFound();
        ViewData["Who"] = who.Str("NAME");
        var rows = await Db.QueryAsync(
            @"SELECT PAYMENT_DATE, BONUS_AMOUNT, BONUS_REASON FROM EMPLOYEE_SALARY
              WHERE EMPLOYEE_EMAIL = @email AND BONUS_AMOUNT > 0 ORDER BY PAYMENT_DATE DESC", new { email });
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> RemoveEmployee(string email)
    {
        await using var tx = await Db.BeginAsync();
        var isEmployee = await tx.ScalarAsync<int>(
            "SELECT COUNT(*) FROM USER_TABLE WHERE EMAIL = @email AND USER_TYPE IN ('STAFF', 'RECEPTIONIST')", new { email });
        if (isEmployee == 0)
        {
            await tx.RollbackAsync();
            FlashError("Employee not found.");
            return RedirectToAction(nameof(Staff));
        }

        await tx.ExecAsync("DELETE FROM EMPLOYEE_SALARY WHERE EMPLOYEE_EMAIL = @email", new { email });
        await tx.ExecAsync("DELETE FROM LEAVE_REQUEST WHERE EMAIL = @email", new { email });
        await tx.ExecAsync("DELETE FROM ROOM_ASSIGNMENTS WHERE EMAIL = @email", new { email });
        await tx.ExecAsync("DELETE FROM MaintenanceRequests WHERE EMAIL = @email", new { email });
        await tx.ExecAsync("DELETE FROM USER_TABLE WHERE EMAIL = @email AND USER_TYPE IN ('STAFF', 'RECEPTIONIST')", new { email });
        await tx.CommitAsync();

        FlashOk("Employee removed.");
        return RedirectToAction(nameof(Staff));
    }

    // =================================================================== reviews
    [HttpGet]
    public async Task<IActionResult> Reviews()
    {
        var rows = await Db.QueryAsync(
            @"SELECT V.REVIEW_ID, V.BOOKING_ID, U.NAME, V.RATING, V.COMMENTS, V.REVIEW_DATE
              FROM REVIEWS_TABLE V JOIN USER_TABLE U ON U.EMAIL = V.EMAIL
              ORDER BY V.REVIEW_DATE DESC, V.REVIEW_ID DESC");
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteReview(int id)
    {
        var n = await Db.ExecAsync("DELETE FROM REVIEWS_TABLE WHERE REVIEW_ID = @id", new { id });
        if (n > 0) FlashOk("Review deleted.");
        else FlashError("Review not found.");
        return RedirectToAction(nameof(Reviews));
    }

    // ======================================================== facility bookings
    [HttpGet]
    public async Task<IActionResult> FacilityBookings()
    {
        var rows = await Db.QueryAsync(
            @"SELECT F.FACILITY_BOOKING_ID, U.NAME, F.FACILITY_NAME, F.BOOKING_DATE, F.GUESTS_COUNT, F.TOTAL_PRICE, F.BOOKING_STATUS
              FROM FACILITIES_BOOKING F JOIN USER_TABLE U ON U.EMAIL = F.EMAIL
              ORDER BY F.FACILITY_BOOKING_ID DESC");
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> FacilityStatus(int id, string? status)
    {
        status = Lists.FacilityStatuses.FirstOrDefault(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));
        if (status == null) { FlashError("Invalid status."); return RedirectToAction(nameof(FacilityBookings)); }

        var n = await Db.ExecAsync("UPDATE FACILITIES_BOOKING SET BOOKING_STATUS = @status WHERE FACILITY_BOOKING_ID = @id", new { status, id });
        if (n > 0) FlashOk($"Facility booking #{id} is now {status}.");
        else FlashError("Facility booking not found.");
        return RedirectToAction(nameof(FacilityBookings));
    }

    // ================================================================== revenue
    [HttpGet]
    public async Task<IActionResult> Revenue(string? type, string? fromDate, string? toDate)
    {
        var today = Clock.Today;
        var fromD = Check.TryDate(fromDate, out var f) ? f : today.AddDays(-30);
        var toD = Check.TryDate(toDate, out var t) ? t : today;
        if (toD < fromD) (fromD, toD) = (toD, fromD);

        // the period expression is chosen from a fixed list - never from user text
        var kind = type switch { "Monthly" => "Monthly", "Yearly" => "Yearly", _ => "Daily" };
        var day = "CONVERT(VARCHAR(10), TRY_CONVERT(datetime, B.BOOKING_DATE, 120), 120)";
        var period = kind switch
        {
            "Monthly" => $"LEFT({day}, 7)",
            "Yearly" => $"LEFT({day}, 4)",
            _ => day
        };

        var rows = await Db.QueryAsync(
            $@"SELECT {period} AS PERIOD_NAME, COUNT(*) AS BOOKINGS, SUM({Nights} * R.PPN) AS REVENUE
               FROM BOOKINGS_TABLE B JOIN ROOM_TABLE R ON R.ROOM_ID = B.ROOM_ID
               WHERE {BookedOn} BETWEEN @fromD AND @toD AND {RevenueStatuses}
               GROUP BY {period}
               ORDER BY {period} DESC", new { fromD, toD });

        var vm = new DateRangeVm
        {
            From = fromD,
            To = toD,
            Type = kind,
            Rows = rows,
            TotalBookings = rows.Sum(r => r.Int("BOOKINGS")),
            TotalRevenue = rows.Sum(r => r.Dec("REVENUE"))
        };
        return View(vm);
    }

    // ================================================================== reports
    [HttpGet]
    public async Task<IActionResult> Reports(string? fromDate, string? toDate)
    {
        var today = Clock.Today;
        var fromD = Check.TryDate(fromDate, out var f) ? f : today.AddDays(-30);
        var toD = Check.TryDate(toDate, out var t) ? t : today;
        if (toD < fromD) (fromD, toD) = (toD, fromD);

        var rows = await Db.QueryAsync(
            $@"SELECT B.BOOKING_ID, U.NAME, B.ROOM_ID, B.CHECK_IN, B.CHECK_OUT, B.BOOKING_DATE, B.BOOKING_STATUS,
                      {Nights} AS NIGHTS, ({Nights} * R.PPN) AS AMOUNT
               FROM BOOKINGS_TABLE B
               JOIN USER_TABLE U ON U.EMAIL = B.EMAIL
               JOIN ROOM_TABLE R ON R.ROOM_ID = B.ROOM_ID
               WHERE {BookedOn} BETWEEN @fromD AND @toD
               ORDER BY TRY_CONVERT(datetime, B.BOOKING_DATE, 120) DESC, B.BOOKING_ID DESC", new { fromD, toD });

        var counted = rows.Where(r =>
        {
            var s = r.Str("BOOKING_STATUS").ToUpperInvariant();
            return s != "PENDING" && s != "CANCELLED";
        }).ToList();

        var vm = new DateRangeVm
        {
            From = fromD,
            To = toD,
            Rows = rows,
            TotalBookings = rows.Count,
            TotalRevenue = counted.Sum(r => r.Dec("AMOUNT"))
        };
        return View(vm);
    }
}
