using System.Text.Json.Serialization;
using DoveLoFanno.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<ServerContext>();
builder.Services.AddSingleton<ProbeHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProbeHost>());
builder.Services.AddHostedService<SchedulerHost>();
// installazione su PC: gira come servizio Windows (wave 3), altrimenti come normale processo
builder.Host.UseWindowsService(o => o.ServiceName = "DoveLoFanno");

var app = builder.Build();
Api.Map(app);
app.Run();

/// <summary>Per i test con WebApplicationFactory.</summary>
public partial class Program;
