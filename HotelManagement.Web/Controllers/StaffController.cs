using System.Globalization;
using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>StaffDashboard.cs + showMYTask.cs from the WinForms project.</summary>
[Authorize(Roles = "STAFF")]
public class StaffController : AppController
{
    public StaffController(Db db, Clock clock) : base(db, clock) { }

    /// <summary>Team view: every housekeeping assignment, searchable by staff name.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        q = (q ?? "").Trim();
        var like = "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

        var vm = new StaffVm
        {
            Search = q,
            StaffName = User.FullName(),
            Assignments = await Db.QueryAsync(
                @"SELECT A.ASSIGNMENT_ID, A.ROOM_ID, U.NAME, A.ASSIGNED_DATE, A.ROOM_STATUS
                  FROM ROOM_ASSIGNMENTS A
                  JOIN USER_TABLE U ON U.EMAIL = A.EMAIL
                  WHERE (@q = '' OR U.NAME LIKE @like)
                  ORDER BY A.ASSIGNED_DATE DESC, A.ASSIGNMENT_ID DESC", new { q, like })
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> MyTasks()
    {
        var vm = new MyTasksVm
        {
            Tasks = await Db.QueryAsync(
                @"SELECT ASSIGNMENT_ID, ROOM_ID, ASSIGNED_DATE, ROOM_STATUS FROM ROOM_ASSIGNMENTS
                  WHERE EMAIL = @email ORDER BY ASSIGNED_DATE DESC, ASSIGNMENT_ID DESC", new { email = Email }),
            MyRequests = await Db.QueryAsync(
                @"SELECT M_ID, ROOM_ID, PROBLEM, STATUS, SUBMIT_DATE FROM MaintenanceRequests
                  WHERE EMAIL = @email ORDER BY M_ID DESC", new { email = Email })
        };
        vm.MyRooms = vm.Tasks.Select(t => t.Str("ROOM_ID")).Distinct().OrderBy(x => x).ToList();
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Complete(string id)
    {
        var n = await Db.ExecAsync(
            @"UPDATE ROOM_ASSIGNMENTS SET ROOM_STATUS = 'CLEANED'
              WHERE ASSIGNMENT_ID = @id AND EMAIL = @email AND ROOM_STATUS <> 'CLEANED'", new { id, email = Email });
        if (n > 0) FlashOk("Task marked as complete.");
        else FlashError("Task not found or already completed.");
        return RedirectToAction(nameof(MyTasks));
    }

    [HttpPost]
    public async Task<IActionResult> ReportProblem(string? roomId, string? problem)
    {
        if (string.IsNullOrEmpty(roomId) || !Lists.MaintenanceProblems.Contains(problem))
        {
            FlashError("Please select a room and a problem.");
            return RedirectToAction(nameof(MyTasks));
        }

        // staff may only report problems for rooms assigned to them
        var mine = await Db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM ROOM_ASSIGNMENTS WHERE EMAIL = @email AND ROOM_ID = @roomId", new { email = Email, roomId });
        if (mine == 0)
        {
            FlashError("You can only report problems for rooms assigned to you.");
            return RedirectToAction(nameof(MyTasks));
        }

        await using (var tx = await Db.BeginAsync())
        {
            var next = await tx.ScalarAsync<int>(
                "SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(M_ID, 3, 20) AS INT)), 0) + 1 FROM MaintenanceRequests WITH (UPDLOCK, HOLDLOCK)");
            var mid = "MR" + next.ToString("D3", CultureInfo.InvariantCulture);

            await tx.ExecAsync(
                @"INSERT INTO MaintenanceRequests (M_ID, ROOM_ID, PROBLEM, STATUS, SUBMIT_DATE, EMAIL)
                  VALUES (@mid, @roomId, @problem, 'Pending', @sd, @email)",
                new { mid, roomId, problem, sd = Clock.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), email = Email });
            await tx.CommitAsync();
        }

        FlashOk("Maintenance request submitted to the admin.");
        return RedirectToAction(nameof(MyTasks));
    }
}
