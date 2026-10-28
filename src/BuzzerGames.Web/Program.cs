using BuzzerGames.Web;
using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Password;
using BuzzerGames.Core.PressYourLuck;
using BuzzerGames.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Railway tells the app which port to listen on through the PORT environment variable.
// (On your own computer there is no PORT, so launchSettings.json decides instead.)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Shows the real error text in the browser's console (F12) instead of the generic "unhandled exception" line.
        // Fine for a private party game; the details are only ever internal error messages.
        options.DetailedErrors = true;
    });

// Where Jeopardy questions come from. To use a real dataset later, write a class that
// implements IGameDataSource and swap it in on this one line.
builder.Services.AddSingleton<IGameDataSource, JsonGameDataSource>();

// One shared set of running games. Every phone and the big screen talk to this same object.
builder.Services.AddSingleton<GameRoomManager>();

// Press Your Luck draws its questions from the same bank. Its own set of running games lives beside Jeopardy's.
builder.Services.AddSingleton<IPylQuestionSource, JsonPylQuestionSource>();
builder.Services.AddSingleton<PylRoomManager>();

// Password Plus has its own puzzle bank and its own set of running games.
builder.Services.AddPasswordPlus();

var app = builder.Build();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// No UseHttpsRedirection here on purpose: Railway ends the HTTPS connection at its edge
// and forwards plain HTTP to us, so redirecting would send phones into a loop.

app.UseAntiforgery();

app.MapStaticAssets();

// Railway (or you, in a browser) can hit this to check the app is alive.
app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
