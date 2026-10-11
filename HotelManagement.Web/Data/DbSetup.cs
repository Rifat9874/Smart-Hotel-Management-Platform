using System.Reflection;
using HotelManagement.Web.Services;

namespace HotelManagement.Web.Data;

public sealed class SetupStatus
{
    public bool Connected { get; set; }
    public string Message { get; set; } = "";
    public List<string> MissingTables { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new();
}

/// <summary>
/// One-click database preparation used by the /Setup page.
/// It never deletes or overwrites anything: tables are created only if missing, and demo rows are
/// added only when the table is empty. If you restored your own HMSDB1 backup, nothing is touched.
/// </summary>
public sealed class DbSetup
{
    public static readonly string[] AllTables =
    {
        "USER_TABLE", "ROOM_TABLE", "ADDITIONAL_PICTURES", "BOOKINGS_TABLE", "PAYMENT", "REVIEWS_TABLE",
        "ROOM_ASSIGNMENTS", "MaintenanceRequests", "LEAVE_REQUEST", "FACILITIES_BOOKING", "EMPLOYEE_SALARY"
    };

    public const string DemoAdminEmail = "admin@hotel.com";
    public const string DemoAdminPassword = "Admin@12345";
    public const string DemoReceptionEmail = "reception@hotel.com";
    public const string DemoReceptionPassword = "Reception@123";
    public const string DemoStaffEmail = "staff@hotel.com";
    public const string DemoStaffPassword = "Staff@12345";
    public const string DemoGuestEmail = "guest@hotel.com";
    public const string DemoGuestPassword = "Guest@12345";

    private readonly Db _db;
    private readonly Clock _clock;

    public DbSetup(Db db, Clock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SetupStatus> GetStatusAsync()
    {
        var status = new SetupStatus();
        var ping = await _db.PingAsync();
        status.Connected = ping.ok;
        status.Message = ping.message;
        if (!ping.ok) return status;

        var existing = (await _db.QueryAsync("SELECT name AS TABLE_NAME FROM sys.tables"))
            .Select(r => r.Str("TABLE_NAME"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var t in AllTables)
        {
            if (!existing.Contains(t)) { status.MissingTables.Add(t); continue; }
            status.Counts[t] = await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{t}]");
        }
        return status;
    }

    /// <summary>Creates missing tables and demo data. Returns a human readable log.</summary>
    public async Task<List<string>> InitializeAsync()
    {
        var log = new List<string>();

        // schema.sql = one "IF OBJECT_ID(...) IS NULL BEGIN CREATE TABLE ... END;" block per table; run them one by one
        var schema = ReadEmbedded("schema.sql");
        foreach (var block in System.Text.RegularExpressions.Regex.Split(schema, @"(?m)^(?=IF OBJECT_ID)"))
        {
            if (block.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                await _db.ExecScriptAsync(block);
        }
        log.Add("Tables checked / created.");

        if (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM ROOM_TABLE") == 0)
        {
            await SeedRoomsAsync();
            log.Add($"Added {SeedData.Rooms.Length} rooms with pictures.");
        }
        else log.Add("Rooms already exist: left untouched.");

        if (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM USER_TABLE") == 0)
        {
            await SeedDemoUsersAndActivityAsync();
            log.Add("Added 4 demo accounts (admin, receptionist, staff, customer) and a little sample activity.");
        }
        else log.Add("Users already exist: no demo accounts added.");

        return log;
    }

    private static string ReadEmbedded(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private async Task SeedRoomsAsync()
    {
        await using var tx = await _db.BeginAsync();
        foreach (var r in SeedData.Rooms)
        {
            await tx.ExecAsync(
                @"INSERT INTO ROOM_TABLE (ROOM_ID, [TYPE], PPN, [DESCRIPTION], PICTURE, COMPLIMENTARY, FEATURES)
                  VALUES (@id, @type, @ppn, @descr, @pic, @comp, @feat)",
                new { id = r.Id, type = r.Type, ppn = r.Price, descr = r.Description, pic = r.Picture, comp = r.Complimentary, feat = r.Features });

            var n = r.Picture.Replace(".jpg", "");
            await tx.ExecAsync(
                @"INSERT INTO ADDITIONAL_PICTURES (PICTURE_ID, ROOM_ID, IMAGE_1, IMAGE_2, IMAGE_3)
                  VALUES (@pid, @id, @i1, @i2, @i3)",
                new { pid = "p" + r.Id.Substring(1), id = r.Id, i1 = n + ".1.jpg", i2 = n + ".2.jpg", i3 = n + ".3.jpg" });
        }
        await tx.CommitAsync();
    }

    private async Task SeedDemoUsersAndActivityAsync()
    {
        var today = _clock.Today;
        var now = _clock.Now;
        string stamp(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        await using var tx = await _db.BeginAsync();

        async Task AddUser(string name, string phone, string email, string password, string type,
                           string? address, string? dept, string? position)
        {
            await tx.ExecAsync(
                @"INSERT INTO USER_TABLE (NAME, PHONE_NUMBER, EMAIL, PASSWORD, USER_TYPE, ADDRESS, DEPARTMENT, POSITION, JOIN_DATE)
                  VALUES (@name, @phone, @email, @pw, @type, @address, @dept, @position, @join)",
                new { name, phone, email, pw = PasswordService.Hash(password), type, address, dept, position, join = today.AddYears(-1) });
        }

        await AddUser("Hotel Admin", "01700000001", DemoAdminEmail, DemoAdminPassword, "ADMIN", "Cox's Bazar, Bangladesh", "Management", "General Manager");
        await AddUser("Rima Reception", "01700000002", DemoReceptionEmail, DemoReceptionPassword, "RECEPTIONIST", "Cox's Bazar, Bangladesh", "Front Desk", "Front Desk Executive");
        await AddUser("Sabbir Staff", "01700000003", DemoStaffEmail, DemoStaffPassword, "STAFF", "Cox's Bazar, Bangladesh", "Operations", "Operations Staff");
        await AddUser("Demo Guest", "01700000004", DemoGuestEmail, DemoGuestPassword, "CUSTOMER", null, null, null);

        async Task AddSalary(string email, decimal salary, decimal allowances, decimal deductions)
        {
            await tx.ExecAsync(
                @"INSERT INTO EMPLOYEE_SALARY (EMPLOYEE_EMAIL, SALARY_AMOUNT, PAYMENT_DATE, PAYMENT_METHOD, ALLOWANCES, DEDUCTIONS, BONUS_AMOUNT, BONUS_REASON)
                  VALUES (@email, @salary, @pd, 'Bank Transfer', @allowances, @deductions, 0, NULL)",
                new { email, salary, pd = today.AddDays(-5), allowances, deductions });
        }
        await AddSalary(DemoReceptionEmail, 30000m, 3000m, 500m);
        await AddSalary(DemoStaffEmail, 25000m, 2000m, 300m);

        // ---- a few bookings for the demo guest so every dashboard has something to show
        async Task AddBooking(string id, string room, int fromDay, int toDay, string status, int ppn, bool paid)
        {
            var ci = today.AddDays(fromDay);
            var co = today.AddDays(toDay);
            var nights = Math.Max(1, (co - ci).Days);
            await tx.ExecAsync(
                @"INSERT INTO BOOKINGS_TABLE (BOOKING_ID, EMAIL, ROOM_ID, CHECK_IN, CHECK_OUT, BOOKING_DATE, BOOKING_STATUS, TOTAL_AMOUNT)
                  VALUES (@id, @email, @room, @ci, @co, @bd, @status, @total)",
                new { id, email = DemoGuestEmail, room, ci, co, bd = stamp(now.AddDays(Math.Min(fromDay, 0) - 2)), status, total = (decimal)(ppn * nights) });
            if (paid)
            {
                await tx.ExecAsync(
                    @"INSERT INTO PAYMENT (P_ID, BOOKING_ID, CARD_NUMBER, EXP_DATE, CODE, BILL)
                      VALUES (@pid, @id, @card, @exp, @code, @bill)",
                    new { pid = id, id, card = "**** **** **** 4242", exp = today.AddYears(2), code = "***", bill = ppn * nights });
            }
        }
        await AddBooking("1", "r106", -10, -7, "CHECK-OUT", 9000, true);
        await AddBooking("2", "r112", -1, 2, "CHECK-IN", 12005, true);
        await AddBooking("3", "r103", 6, 9, "PAID", 11000, true);
        await AddBooking("4", "r109", 20, 22, "PENDING", 8500, false);

        await tx.ExecAsync(
            @"INSERT INTO REVIEWS_TABLE (BOOKING_ID, EMAIL, RATING, COMMENTS, REVIEW_DATE)
              VALUES ('1', @email, 5, 'Lovely stay, spotless room and very friendly staff!', @now)",
            new { email = DemoGuestEmail, now = now.AddDays(-6) });

        // ---- housekeeping task, leave request, maintenance request, facility booking
        await tx.ExecAsync(
            @"INSERT INTO ROOM_ASSIGNMENTS (ASSIGNMENT_ID, ROOM_ID, ASSIGNED_DATE, EMAIL, ROOM_STATUS)
              VALUES ('0001', 'r106', @d, @staff, 'DIRTY'), ('0002', 'r108', @d, @staff, 'CLEANED')",
            new { d = today, staff = DemoStaffEmail });

        await tx.ExecAsync(
            @"INSERT INTO LEAVE_REQUEST (LEAVE_ID, EMAIL, FROM_DATE, TO_DATE, REASON, STATUS)
              VALUES ('LV001', @staff, @f, @t, 'Family Emergency', 'Pending')",
            new { staff = DemoStaffEmail, f = today.AddDays(10).ToString("yyyy-MM-dd"), t = today.AddDays(12).ToString("yyyy-MM-dd") });

        await tx.ExecAsync(
            @"INSERT INTO MaintenanceRequests (M_ID, ROOM_ID, PROBLEM, STATUS, SUBMIT_DATE, EMAIL)
              VALUES ('MR001', 'r106', 'AC not working', 'Pending', @sd, @staff)",
            new { sd = stamp(now), staff = DemoStaffEmail });

        await tx.ExecAsync(
            @"INSERT INTO FACILITIES_BOOKING (EMAIL, FACILITY_NAME, BOOKING_DATE, GUESTS_COUNT, TOTAL_PRICE, BOOKING_STATUS, BOOKING_TIME)
              VALUES (@email, 'Spa Services', @d, 2, 5000, 'PENDING', @now)",
            new { email = DemoGuestEmail, d = today.AddDays(1), now });

        await tx.CommitAsync();
    }
}
