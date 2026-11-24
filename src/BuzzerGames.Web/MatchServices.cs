using BuzzerGames.Core.Match;

namespace BuzzerGames.Web;

public static class MatchServices
{
    /// <summary>
    /// Registers Match Game. Its questions, family names and Super Match blanks live in <c>match/bank.json</c> inside the same data folder
    /// The Floor, Wheel, Pyramid and Family Feud use, so on Railway they sit on the same Volume. Set MATCH_DATA_DIR only to put them elsewhere.
    /// </summary>
    public static IServiceCollection AddMatchGame(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var folder = Environment.GetEnvironmentVariable("MATCH_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetEnvironmentVariable("FLOOR_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(env.ContentRootPath, "floor-data");
            return new FolderMatchContent(folder);
        });
        services.AddSingleton<IMatchContent>(sp => sp.GetRequiredService<FolderMatchContent>());
        services.AddSingleton<MatchRoomManager>();
        return services;
    }
}
