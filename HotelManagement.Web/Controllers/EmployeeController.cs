using System.Globalization;
using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>Employee_Profile.cs + Reciptionist_Leave.cs + Staff_LeaveApplication.cs + Edit_Info.cs (employee part).</summary>
[Authorize(Roles = "RECEPTIONIST,STAFF")]
public class EmployeeController : AppController
{
    public EmployeeController(Db db, Clock clock) : base(db, clock) { }

    private const string LeaveDaysSql =
        "DATEDIFF(DAY, TRY_CONVERT(date, FROM_DATE, 23), TRY_CONVERT(date, TO_DATE, 23)) + 1";

    // -------------------------------------------------------------- profile
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var person = await Db.QueryOneAsync(
            @"SELECT NAME, PHONE_NUMBER, EMAIL, USER_TYPE, ADDRESS, DEPARTMENT, POSITION, JOIN_DATE
              FROM USER_TABLE WHERE EMAIL = @email", new { email = Email });
        if (person == null) return NotFound();

        var vm = new EmployeeProfileVm { Person = person };

        vm.Salary = await Db.QueryOneAsync(
            @"SELECT TOP 1 SALARY_AMOUNT, PAYMENT_DATE, PAYMENT_METHOD,
                     ISNULL(ALLOWANCES, 0) AS ALLOWANCES, ISNULL(DEDUCTIONS, 0) AS DEDUCTIONS,
                     ISNULL(BONUS_AMOUNT, 0) AS BONUS_AMOUNT, BONUS_REASON,
                     (SALARY_AMOUNT + ISNULL(ALLOWANCES, 0) + ISNULL(BONUS_AMOUNT, 0) - ISNULL(DEDUCTIONS, 0)) AS NET_SALARY
              FROM EMPLOYEE_SALARY WHERE EMPLOYEE_EMAIL = @email
              ORDER BY PAYMENT_DATE DESC, SALARY_ID DESC", new { email = Email });

        vm.Leaves = await Db.QueryAsync(
            @"SELECT LEAVE_ID, FROM_DATE, TO_DATE, REASON, STATUS FROM LEAVE_REQUEST
              WHERE EMAIL = @email ORDER BY LEAVE_ID DESC", new { email = Email });

        var stats = await Db.QueryOneAsync(
            $@"SELECT
                 SUM(CASE WHEN STATUS = 'APPROVED' THEN 1 ELSE 0 END) AS APPROVED,
                 SUM(CASE WHEN STATUS = 'PENDING' THEN 1 ELSE 0 END) AS PENDING,
                 SUM(CASE WHEN STATUS IN ('REJECTED', 'DISMISSED') THEN 1 ELSE 0 END) AS REJECTED,
                 SUM(CASE WHEN STATUS = 'APPROVED' THEN {LeaveDaysSql} ELSE 0 END) AS LEAVE_DAYS
               FROM LEAVE_REQUEST WHERE EMAIL = @email", new { email = Email });
        if (stats != null)
        {
            vm.Approved = stats.Int("APPROVED");
            vm.Pending = stats.Int("PENDING");
            vm.Rejected = stats.Int("REJECTED");
            vm.UsedLeave = stats.Int("LEAVE_DAYS");
        }
        return View(vm);
    }

    // ----------------------------------------------------------------- edit
    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var me = await Db.QueryOneAsync("SELECT NAME, PHONE_NUMBER, EMAIL, ADDRESS FROM USER_TABLE WHERE EMAIL = @email", new { email = Email });
        return me == null ? NotFound() : View(me);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string? name, string? phone, string? address, string? password, string? confirm)
    {
        name = (name ?? "").Trim();
        phone = (phone ?? "").Trim();
        address = (address ?? "").Trim();

        string? error = null;
        if (name.Length == 0 || name.Length > 50) error = "Invalid Name";
        else if (!Check.Phone(phone)) error = "Invalid Number (11 digits)";
        else if (address.Length > 255) error = "Address is too long.";
        else if (!string.IsNullOrEmpty(password) && password != confirm) error = "Passwords do not match!";
        else if (!string.IsNullOrEmpty(password) && !Check.Password(password)) error = "Password must be at least 8 characters!";

        if (error != null)
        {
            ViewData["Error"] = error;
            return View(await Db.QueryOneAsync("SELECT NAME, PHONE_NUMBER, EMAIL, ADDRESS FROM USER_TABLE WHERE EMAIL = @email", new { email = Email }));
        }

        await Db.ExecAsync(
            "UPDATE USER_TABLE SET NAME = @name, PHONE_NUMBER = @phone, ADDRESS = @address WHERE EMAIL = @email",
            new { name, phone, address = address.Length == 0 ? null : address, email = Email });
        if (!string.IsNullOrEmpty(password))
            await Db.ExecAsync("UPDATE USER_TABLE SET PASSWORD = @pw WHERE EMAIL = @email",
                new { pw = PasswordService.Hash(password), email = Email });

        await RefreshNameAsync(name);
        FlashOk("Your information has been updated.");
        return RedirectToAction(nameof(Profile));
    }

    // ---------------------------------------------------------------- leave
    [HttpGet]
    public async Task<IActionResult> Leave()
    {
        ViewData["Today"] = Clock.Today.ToString("yyyy-MM-dd");
        var rows = await Db.QueryAsync(
            @"SELECT LEAVE_ID, FROM_DATE, TO_DATE, REASON, STATUS FROM LEAVE_REQUEST
              WHERE EMAIL = @email ORDER BY LEAVE_ID DESC", new { email = Email });
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Leave(string? fromDate, string? toDate, string? reason)
    {
        if (!Check.TryDate(fromDate, out var fromD) || !Check.TryDate(toDate, out var toD))
        { FlashError("Please choose both dates."); return RedirectToAction(nameof(Leave)); }
        if (fromD < Clock.Today) { FlashError("Start date cannot be in the past."); return RedirectToAction(nameof(Leave)); }
        if (toD < fromD) { FlashError("End date must be on or after the start date."); return RedirectToAction(nameof(Leave)); }
        if (!Lists.LeaveReasons.Contains(reason)) { FlashError("Please select a reason."); return RedirectToAction(nameof(Leave)); }

        await using (var tx = await Db.BeginAsync())
        {
            var overlap = await tx.ScalarAsync<int>(
                @"SELECT COUNT(*) FROM LEAVE_REQUEST
                  WHERE EMAIL = @email AND STATUS IN ('Pending', 'APPROVED')
                    AND TRY_CONVERT(date, FROM_DATE, 23) <= @toD AND TRY_CONVERT(date, TO_DATE, 23) >= @fromD",
                new { email = Email, fromD, toD });
            if (overlap > 0)
            {
                await tx.RollbackAsync();
                FlashError("You already have a pending or approved leave in this period.");
                return RedirectToAction(nameof(Leave));
            }

            // ids look like LV001 (older rows may hold plain numbers)
            var next = await tx.ScalarAsync<int>(
                @"SELECT ISNULL(MAX(CASE WHEN LEAVE_ID LIKE 'LV%' THEN TRY_CAST(SUBSTRING(LEAVE_ID, 3, 20) AS INT)
                                         ELSE TRY_CAST(LEAVE_ID AS INT) END), 0) + 1
                  FROM LEAVE_REQUEST WITH (UPDLOCK, HOLDLOCK)");
            var leaveId = "LV" + next.ToString("D3", CultureInfo.InvariantCulture);

            await tx.ExecAsync(
                @"INSERT INTO LEAVE_REQUEST (LEAVE_ID, EMAIL, FROM_DATE, TO_DATE, REASON, STATUS)
                  VALUES (@leaveId, @email, @f, @t, @reason, 'Pending')",
                new { leaveId, email = Email, f = fromD.ToString("yyyy-MM-dd"), t = toD.ToString("yyyy-MM-dd"), reason });
            await tx.CommitAsync();
        }

        FlashOk("Leave request submitted successfully!");
        return RedirectToAction(nameof(Leave));
    }
}
