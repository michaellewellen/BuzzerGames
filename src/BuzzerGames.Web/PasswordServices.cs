using BuzzerGames.Core.Password;

namespace BuzzerGames.Web;

public static class PasswordServices
{
    /// <summary>
    /// Registers Password Plus. The word sources are built with factories on purpose: both have a second constructor that
    /// takes a list of words, and when the container picks the constructor itself it prefers that one and hands it an
    /// EMPTY list, which leaves the game with no bonus words at all.
    /// </summary>
    public static IServiceCollection AddPasswordPlus(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordPuzzleSource>(_ => new JsonPasswordPuzzleSource());
        services.AddSingleton<IBonusWordSource>(_ => new TextBonusWordSource());
        services.AddSingleton<PasswordRoomManager>();
        return services;
    }
}
