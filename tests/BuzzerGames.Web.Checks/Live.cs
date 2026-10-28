using BuzzerGames.Core.Password;
using BuzzerGames.Web.Components.Password;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

/// <summary>
/// One screen that stays alive while the game moves on, the way a browser circuit does: the same component instance is
/// re-rendered with each new snapshot, so Blazor has to diff the old screen against the new one. Drawing every phase from
/// scratch (Render.cs) can't catch a problem that only shows up in that diff.
/// </summary>
sealed class LiveView : IDisposable
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddSingleton<IJSRuntime, Render.NoJs>()
        .BuildServiceProvider();

    private sealed class Host : ComponentBase
    {
        public static Host? Last;
        [Parameter] public Type Type { get; set; } = default!;
        [Parameter] public Dictionary<string, object?> Parameters { get; set; } = new();

        public Host() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent(0, Type);
            var seq = 1;
            foreach (var (name, value) in Parameters) builder.AddComponentParameter(seq++, name, value);
            builder.CloseComponent();
        }

        public void Refresh() => StateHasChanged();
    }

    private readonly HtmlRenderer _renderer = new(Services, Services.GetRequiredService<ILoggerFactory>());
    private readonly Host _host;
    private readonly string _name;

    public LiveView(string name, Type type, Dictionary<string, object?> parameters)
    {
        _name = name;
        _renderer.Dispatcher.InvokeAsync(async () =>
            await _renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Type"] = type,
                ["Parameters"] = parameters
            }))).GetAwaiter().GetResult();
        _host = Host.Last!;
    }

    public void Update(Dictionary<string, object?> parameters)
    {
        _host.Parameters = parameters;
        try
        {
            _renderer.Dispatcher.InvokeAsync(() => _host.Refresh()).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"{_name} failed to redraw: {e}", e);
        }
    }

    public void Dispose() => _renderer.Dispose();
}
