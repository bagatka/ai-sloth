using Aspire.Hosting;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Bagatka_AiSloth_WebApi>("webapi")
    .WithHttpHealthCheck("/health");

using DistributedApplication app = builder.Build();
app.Run();
