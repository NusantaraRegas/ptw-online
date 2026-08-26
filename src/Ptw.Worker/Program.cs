using Ptw.Infrastructure;
using Ptw.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPtwInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OutboxWorker>();

await builder.Build().RunAsync();
