using BuzzerGames.Core.Pyramid;

namespace BuzzerGames.Web;

public static class PyramidServices
{
    /// <summary>
    /// Registers $100,000 Pyramid. Its topics live in <c>pyramid/topics.json</c> inside the same data folder The Floor and Wheel use, so on Railway they
    /// sit on the same Volume with no new setup. Set PYRAMID_DATA_DIR only to put them somewhere else.
    /// </summary>
    public static IServiceCollection AddPyramid(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var folder = Environment.GetEnvironmentVariable("PYRAMID_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetEnvironmentVariable("FLOOR_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(env.ContentRootPath, "floor-data");
            return new FolderPyramidContent(folder);
        });
        services.AddSingleton<IPyramidContent>(sp => sp.GetRequiredService<FolderPyramidContent>());
        services.AddSingleton<PyramidRoomManager>();
        return services;
    }
}
