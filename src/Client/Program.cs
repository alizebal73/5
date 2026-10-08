using GameNet.Agent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "GameNet 5 Agent");
builder.Services.AddHostedService<AgentWorker>();
await builder.Build().RunAsync();
