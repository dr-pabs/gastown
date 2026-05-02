using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddHttpClient("sfa-service", client =>
        {
            var baseUrl = ctx.Configuration["Services:SfaServiceBaseUrl"]
                ?? "http://localhost:5002/";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        });
    })
    .Build();

await host.RunAsync();
