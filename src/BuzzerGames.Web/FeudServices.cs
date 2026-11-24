using BuzzerGames.Core.Feud;

namespace BuzzerGames.Web;

public static class FeudServices
{
    /// <summary>
    /// Registers Family Feud. Its survey questions live in <c>feud/boards.json</c> inside the same data folder The Floor, Wheel and Pyramid use,
    /// so on Railway they sit on the same Volume with no new setup. Set FEUD_DATA_DIR only to put them somewhere else.
    /// </summary>
    public static IServiceCollection AddFeud(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var folder = Environment.GetEnvironmentVariable("FEUD_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetEnvironmentVariable("FLOOR_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(env.ContentRootPath, "floor-data");
            return new FolderFeudContent(folder);
        });
        services.AddSingleton<IFeudContent>(sp => sp.GetRequiredService<FolderFeudContent>());
        services.AddSingleton<FeudRoomManager>();
        return services;
    }
}
