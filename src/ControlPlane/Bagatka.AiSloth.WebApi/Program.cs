using System.Globalization;
using Bagatka.ServiceDefaults;
using Microsoft.AspNetCore.Builder;

// Anything that slips past the analyzers formats the same way on every server.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

await using WebApplication app = builder.Build();
app.MapDefaultEndpoints();

await app.RunAsync();
