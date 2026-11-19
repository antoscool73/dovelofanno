using DoveLoFanno.Server;

// controllo di salute per Docker (HEALTHCHECK) senza curl nell'immagine
if (args.Contains("--healthcheck"))
{
    string url = Environment.GetEnvironmentVariable("DLF_HEALTH_URL") ?? "http://127.0.0.1:8765/api/v1/health";
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return (await http.GetAsync(url)).IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return 1; }
}

var builder = WebApplication.CreateBuilder(args);
// i numeri sono solo numeri (le impostazioni web predefinite accettano anche "12" tra virgolette)
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
builder.Services.AddSingleton<ServerContext>();
builder.Services.AddSingleton<ProbeHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProbeHost>());
builder.Services.AddHostedService<SchedulerHost>();
builder.Services.AddHostedService<BackupHost>();
builder.Services.AddSingleton<RatingsHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RatingsHost>());
builder.Services.AddSingleton<GuideHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GuideHost>());
builder.Services.AddSingleton<LibraryHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryHost>());
builder.Services.AddSingleton<NotifyHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NotifyHost>());
builder.Services.AddSingleton<UpdateHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateHost>());
builder.Services.AddSingleton<PreviewService>();
builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "DoveLoFanno";
    doc.Info.Version = Api.Version;
    doc.Info.Description = "API del server domestico DoveLoFanno: sorgenti, canali, registrazioni, guida, libreria, liste, preferenze, proposte, avvisi, notifiche, aggiornamenti, backup.";
    doc.Servers?.Clear();
    return Task.CompletedTask;
}));
// installazione su PC: gira come servizio Windows; altrimenti (Docker, prova) come normale processo
builder.Host.UseWindowsService(o => o.ServiceName = "DoveLoFanno");

var app = builder.Build();
app.MapOpenApi("/api/{documentName}/openapi.json");
Api.Map(app);
// interfaccia web (wwwroot, prodotta da web/ con npm run build); gli indirizzi dell'app che non sono file
// tornano a index.html, quelli dell'API sconosciuti restano 404. index.html si ricontrolla sempre (dopo un
// aggiornamento il browser prende subito l'interfaccia nuova); i file in assets/ hanno l'hash nel nome e restano in cache.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
        ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);
app.MapFallback("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html", staticFiles);
await app.RunAsync();
return 0;

/// <summary>Per i test con WebApplicationFactory.</summary>
public partial class Program;
