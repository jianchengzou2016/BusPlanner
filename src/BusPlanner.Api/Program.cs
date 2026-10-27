using BusPlanner.Api.Data;
using BusPlanner.Api.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Text.Json.Serialization;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((_, configuration) => configuration.AddEnvironmentVariables())
    .ConfigureServices((context, services) =>
    {
        services.Configure<BusPlannerOptions>(context.Configuration.GetSection("BusPlanner"));
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(json =>
            json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        services.AddHttpClient();
        services.AddSingleton<TransitDataService>();
    })
    .Build();

host.Run();
