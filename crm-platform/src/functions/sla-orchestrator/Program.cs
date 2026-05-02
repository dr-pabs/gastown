using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddHttpClient("css-service", client =>
        {
            var baseUrl = ctx.Configuration["Services:CssServiceBaseUrl"]
                ?? "http://localhost:5004/";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        });
    })
    .Build();

await host.RunAsync();
