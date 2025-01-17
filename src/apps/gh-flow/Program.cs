using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI.TextEmbedding;
using Microsoft.SemanticKernel.Connectors.Memory.Qdrant;
using Microsoft.SemanticKernel.Memory;
using MS.AI.DevTeam;
using Octokit.Webhooks;
using Octokit.Webhooks.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<WebhookEventProcessor, GithubWebHookProcessor>();
builder.Services.AddTransient(CreateKernel);

builder.Services.AddSingleton(s =>
{
    var ghOptions = s.GetService<IOptions<GithubOptions>>();
    var ghService = new GithubAuthService(ghOptions);
    var client = ghService.GetGitHubClient().Result;
    return client;
});
builder.Services.AddSingleton<GithubAuthService>();

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddOptions<GithubOptions>()
    .Configure<IConfiguration>((settings, configuration) =>
    {
        configuration.GetSection("GithubOptions").Bind(settings);
    });
builder.Services.AddOptions<AzureOptions>()
    .Configure<IConfiguration>((settings, configuration) =>
    {
        configuration.GetSection("AzureOptions").Bind(settings);
    });
builder.Services.AddOptions<OpenAIOptions>()
    .Configure<IConfiguration>((settings, configuration) =>
    {
        configuration.GetSection("OpenAIOptions").Bind(settings);
    });
builder.Services.AddOptions<QdrantOptions>()
    .Configure<IConfiguration>((settings, configuration) =>
    {
        configuration.GetSection("QdrantOptions").Bind(settings);
    });

builder.Services.AddSingleton<IManageAzure, AzureService>();
builder.Services.AddSingleton<IManageGithub, GithubService>();


builder.Host.UseOrleans(siloBuilder =>
{
    var connectionString = builder.Configuration.GetValue<string>("AzureOptions:CosmosConnectionString");
    siloBuilder.UseCosmosReminderService( o => 
    {
            o.ConfigureCosmosClient(connectionString);
            o.ContainerName = "devteam";
            o.DatabaseName = "reminders";
            o.IsResourceCreationEnabled = true;
    });
    siloBuilder.AddCosmosGrainStorage(
        name: "messages",
        configureOptions: o =>
        {
            o.ConfigureCosmosClient(connectionString);
            o.ContainerName = "devteam";
            o.DatabaseName = "persistence";
            o.IsResourceCreationEnabled = true;
        });
    siloBuilder.UseLocalhostClustering();
});




builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
var app = builder.Build();

app.UseRouting();

app.UseEndpoints(endpoints =>
{
    endpoints.MapGitHubWebhooks();
});

app.Run();

static IKernel CreateKernel(IServiceProvider provider)
{
    var openAiConfig = provider.GetService<IOptions<OpenAIOptions>>().Value;
    var qdrantConfig = provider.GetService<IOptions<QdrantOptions>>().Value;

    var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddConsole()
            .AddDebug();
    });

    var memoryStore = new QdrantMemoryStore(new QdrantVectorDbClient(qdrantConfig.Endpoint, qdrantConfig.VectorSize));
    var embedingGeneration = new AzureTextEmbeddingGeneration(openAiConfig.EmbeddingDeploymentOrModelId, openAiConfig.Endpoint, openAiConfig.ApiKey);
    var semanticTextMemory = new SemanticTextMemory(memoryStore, embedingGeneration);

    return new KernelBuilder()
                        .WithLoggerFactory(loggerFactory)
                        .WithAzureChatCompletionService(openAiConfig.DeploymentOrModelId, openAiConfig.Endpoint, openAiConfig.ApiKey, true, openAiConfig.ServiceId, true)
                        .WithMemory(semanticTextMemory).Build();
}