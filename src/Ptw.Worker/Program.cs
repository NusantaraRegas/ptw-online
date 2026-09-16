using Ptw.Infrastructure;
using Ptw.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPtwInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<PrintPackageRenderWorker>();

await builder.Build().RunAsync();
