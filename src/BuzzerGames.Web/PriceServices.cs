using BuzzerGames.Core.Price;

namespace BuzzerGames.Web;

public static class PriceServices
{
    /// <summary>
    /// Registers The Price Is Right. The sources are built with factories on purpose: when the container picks a constructor
    /// itself it can choose the one that takes a list and hand it an empty list (this already bit Password Plus).
    /// </summary>
    public static IServiceCollection AddPriceIsRight(this IServiceCollection services)
    {
        services.AddSingleton<IPriceItemSource>(_ => new JsonPriceItemSource());
        services.AddSingleton<IPartingGiftSource>(_ => new TextPartingGiftSource());
        services.AddSingleton<IPlinkoItemSource>(_ => new JsonPlinkoItemSource());
        services.AddSingleton<PriceRoomManager>();
        return services;
    }
}
