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
builder.Services.AddSingleton<ServerContext>();
builder.Services.AddSingleton<ProbeHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProbeHost>());
builder.Services.AddHostedService<SchedulerHost>();
builder.Services.AddHostedService<BackupHost>();
builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "DoveLoFanno";
    doc.Info.Version = Api.Version;
    doc.Info.Description = "API del server domestico DoveLoFanno: sorgenti, canali, registrazioni, backup.";
    doc.Servers?.Clear();
    return Task.CompletedTask;
}));
// installazione su PC: gira come servizio Windows; altrimenti (Docker, prova) come normale processo
builder.Host.UseWindowsService(o => o.ServiceName = "DoveLoFanno");

var app = builder.Build();
app.MapOpenApi("/api/{documentName}/openapi.json");
Api.Map(app);
await app.RunAsync();
return 0;

/// <summary>Per i test con WebApplicationFactory.</summary>
public partial class Program;
