using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BuzzerGames.Core.Floor;

namespace BuzzerGames.Web;

/// <summary>Who may use the topics page. Set FLOOR_ADMIN_PASSCODE on Railway; without one the page only opens when running on your own computer.</summary>
public sealed class FloorAdminAccess(string? passcode, bool isDevelopment)
{
    public bool Configured => !string.IsNullOrEmpty(passcode);

    /// <summary>True if the typed passcode is right (or none is needed because this is a development machine).</summary>
    public bool IsAllowed(string? typed)
    {
        if (!Configured) return isDevelopment;
        if (string.IsNullOrEmpty(typed)) return false;
        return CryptographicOperations.FixedTimeEquals(Hash(typed), Hash(passcode!));
    }

    public string? Problem => Configured || isDevelopment
        ? null
        : "The topics page is locked. Set the FLOOR_ADMIN_PASSCODE variable on Railway, then redeploy.";

    private static byte[] Hash(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));
}

public static class FloorServices
{
    public const string PassHeader = "X-Floor-Pass";

    /// <summary>
    /// Registers The Floor. Pictures and topics live in a folder: set FLOOR_DATA_DIR to the Railway Volume's mount path (/data).
    /// Without it they go in a "floor-data" folder beside the program, which is fine on your own computer but is wiped by every Railway deploy.
    /// </summary>
    public static IServiceCollection AddFloor(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var folder = Environment.GetEnvironmentVariable("FLOOR_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(env.ContentRootPath, "floor-data");
            var min = int.TryParse(Environment.GetEnvironmentVariable("FLOOR_MIN_PROMPTS"), out var m) && m > 0 ? m : 20;
            return new FolderFloorContent(folder, min);
        });
        services.AddSingleton<IFloorContent>(sp => sp.GetRequiredService<FolderFloorContent>());
        services.AddSingleton(sp => new FloorAdminAccess(
            Environment.GetEnvironmentVariable("FLOOR_ADMIN_PASSCODE"),
            sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));
        services.AddSingleton<FloorRoomManager>();
        return services;
    }

    /// <summary>The two web addresses that aren't pages: one serves a picture to the big screen, one takes uploads from the topics page.</summary>
    public static void MapFloor(this WebApplication app)
    {
        // Pictures are found by an opaque key, never by name, so the address of a picture can't give its answer away.
        app.MapGet("/the-floor/i/{key}", (string key, FolderFloorContent store, HttpContext http) =>
        {
            if (!Regex.IsMatch(key, "^[0-9a-f]{16}$") || !store.TryGetImage(key, out var path, out var type)) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(path, type);
        });

        app.MapPost("/the-floor/upload", async (HttpRequest request, FolderFloorContent store, FloorAdminAccess access) =>
        {
            if (!access.IsAllowed(request.Headers[PassHeader].ToString()))
                return Results.Json(new { ok = false, error = "Wrong passcode." }, statusCode: 401);
            if (!request.HasFormContentType)
                return Results.Json(new { ok = false, error = "Expected a form upload." }, statusCode: 400);

            var form = await request.ReadFormAsync();
            var file = form.Files["file"];
            if (file is null || file.Length == 0) return Results.Json(new { ok = false, error = "No picture came through." });
            if (file.Length > 8 * 1024 * 1024) return Results.Json(new { ok = false, error = "That picture is too big." });

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var result = store.SaveImage(form["topic"].ToString(), form["answer"].ToString(), form["alternates"].ToString(), memory.ToArray());
            return Results.Json(new { ok = result.Ok, error = result.Error, file = result.File });
        }).DisableAntiforgery();
    }
}

/// <summary>Small helpers the Floor screens share.</summary>
public static class FloorUi
{
    /// <summary>A color for each player, spread around the color wheel so neighbors look different.</summary>
    public static string Color(BuzzerGames.Core.Floor.FloorSnapshot s, Guid ownerId)
    {
        var index = s.Players.ToList().FindIndex(p => p.Id == ownerId);
        if (index < 0) index = 0;
        var hue = (int)(index * 137.508 % 360);
        var light = index % 2 == 0 ? 32 : 42;
        return $"hsl({hue}, 58%, {light}%)";
    }

    public static string ImageUrl(string key) => $"/the-floor/i/{key}";

    public static int Seconds(int ms) => (int)Math.Ceiling(ms / 1000.0);

    public static string Topics(IEnumerable<string> topics) => string.Join(" + ", topics);
}
