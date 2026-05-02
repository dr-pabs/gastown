using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        // HTTP client for calling marketing-service internal API
        services.AddHttpClient("marketing-service", client =>
        {
            client.BaseAddress = new Uri(
                ctx.Configuration["Services:MarketingServiceBaseUrl"]
                ?? "http://marketing-service");
        });
    })
    .Build();

await host.RunAsync();
