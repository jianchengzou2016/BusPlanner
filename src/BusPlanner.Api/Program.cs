using BusPlanner.Api.Data;
using BusPlanner.Api.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Text.Json.Serialization;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((context, configuration) =>
    {
        configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<BusPlannerOptions>(context.Configuration.GetSection("BusPlanner"));
        services.PostConfigure<BusPlannerOptions>(options =>
        {
            // Azure Static Web Apps limits application-setting names to 64 characters.
            // Use the same structured JSON shape locally and in Azure. Locally, Journeys is
            // a JSON object; in Azure, its value is supplied as the BusPlanner__Journeys setting.
            var journeyProfilesJson = context.Configuration["BusPlanner:Journeys"];
            if (string.IsNullOrWhiteSpace(journeyProfilesJson)
                || !journeyProfilesJson.TrimStart().StartsWith('{'))
            {
                return;
            }

            var journeyProfiles = JsonSerializer.Deserialize<Dictionary<string, JourneyProfileOptions>>(
                journeyProfilesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (journeyProfiles is null)
            {
                throw new InvalidOperationException("BusPlanner:Journeys must contain a JSON object of journey profiles when configured as a setting value.");
            }

            options.Journeys = new Dictionary<string, JourneyProfileOptions>(
                journeyProfiles,
                StringComparer.OrdinalIgnoreCase);
        });
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(json =>
            json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        services.AddHttpClient();
        services.AddSingleton<TransitDataService>();
        services.AddHostedService<ScheduleWarmupService>();
    })
    .Build();

host.Run();
