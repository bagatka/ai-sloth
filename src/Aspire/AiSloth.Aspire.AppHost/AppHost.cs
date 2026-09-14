using Aspire.Hosting;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

using DistributedApplication app = builder.Build();
app.Run();
