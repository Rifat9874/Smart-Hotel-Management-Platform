namespace HotelManagement.Web.Services;

/// <summary>
/// ROOM_TABLE.PICTURE holds a Windows path from the developer's PC (E:\9th semester\...\1.jpg).
/// On the website only the file name matters: the pictures live in wwwroot/img/rooms.
/// </summary>
public static class RoomImages
{
    public const string Fallback = "/img/site/lobby.jpg";

    public static string FileName(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return "";
        var p = storedPath.Replace('\\', '/').Trim();
        var i = p.LastIndexOf('/');
        return i >= 0 ? p[(i + 1)..] : p;
    }

    public static string Full(string? storedPath)
    {
        var f = FileName(storedPath);
        return f.Length == 0 ? Fallback : "/img/rooms/" + Uri.EscapeDataString(f);
    }

    public static string Thumb(string? storedPath)
    {
        var f = FileName(storedPath);
        return f.Length == 0 ? Fallback : "/img/rooms/thumb/" + Uri.EscapeDataString(f);
    }
}
