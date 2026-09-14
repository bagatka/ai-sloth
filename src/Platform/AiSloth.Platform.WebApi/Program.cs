using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
await using WebApplication app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
