using BuzzerGames.Core.CardSharks;

namespace BuzzerGames.Web;

public static class CardSharksServices
{
    /// <summary>Registers Card Sharks. The survey bank is built with a factory for the same reason as the other games: the JSON source has a second constructor that takes a string.</summary>
    public static IServiceCollection AddCardSharks(this IServiceCollection services)
    {
        services.AddSingleton<ICardSharksContent>(_ => new JsonCardSharksContent());
        services.AddSingleton<CardSharksRoomManager>();
        return services;
    }
}
