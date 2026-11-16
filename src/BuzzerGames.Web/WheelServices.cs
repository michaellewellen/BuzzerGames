using BuzzerGames.Core.Wheel;

namespace BuzzerGames.Web;

public static class WheelServices
{
    /// <summary>
    /// Registers Wheel of Fortune. Its puzzles live in <c>wheel/puzzles.json</c> inside the same data folder The Floor uses, so on Railway they sit on
    /// the same Volume and need no new setup. Set WHEEL_DATA_DIR only if you want them somewhere else. The puzzles page uses the same passcode as the Floor's topics page.
    /// </summary>
    public static IServiceCollection AddWheel(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var folder = Environment.GetEnvironmentVariable("WHEEL_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetEnvironmentVariable("FLOOR_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(env.ContentRootPath, "floor-data");
            return new FolderWheelContent(folder);
        });
        services.AddSingleton<IWheelContent>(sp => sp.GetRequiredService<FolderWheelContent>());
        services.AddSingleton<WheelRoomManager>();
        return services;
    }
}
