using BuzzerGames.Core.Match;

namespace BuzzerGames.Web;

public static class MatchServices
{
    /// <summary>
    /// Registers Match Game. The content is built with a factory on purpose, for the same reason as Password Plus: the JSON source
    /// has a second constructor that takes a string, and a container left to choose would hand it an empty one.
    /// </summary>
    public static IServiceCollection AddMatchGame(this IServiceCollection services)
    {
        services.AddSingleton<IMatchContent>(_ => new JsonMatchContent());
        services.AddSingleton<MatchRoomManager>();
        return services;
    }
}
