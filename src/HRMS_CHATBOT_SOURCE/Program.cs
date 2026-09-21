using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Infrastructure.Bootstrap;
using HRMS_CHATBOT_SOURCE.Infrastructure.Extensions;
using HRMS_CHATBOT_SOURCE.Agent;
using HRMS_CHATBOT_SOURCE.Logic;
using HRMS_CHATBOT_SOURCE.RAG;
using HRMS_CHATBOT_SOURCE.Repo.Admin;
using HRMS_CHATBOT_SOURCE.Repo.Agent;
using HRMS_CHATBOT_SOURCE.Repo.Document;
using HRMS_CHATBOT_SOURCE.Repo.Leave;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine("web", "wwwroot")
});

var applicationSecrets = await KeyVaultBootstrap.LoadSecretsAsync(
    builder.Configuration,
    builder.Environment);

builder.Configuration.AddInMemoryCollection(applicationSecrets.ToConfigurationDictionary());

builder.Services
    .AddHrmsCoreServices(builder.Configuration, applicationSecrets)
    .AddHrmsMvc()
    .AddHrmsCors(builder.Configuration)
    .AddHrmsSession(builder.Configuration)
    .AddHrmsFoundationServices(builder.Configuration, applicationSecrets)
    .AddHrmsAuthentication()
    .AddHrmsSwagger();

builder.Services.AddScoped<IUserProfileRepo, UserProfileRepo>();
builder.Services.AddScoped<IDocumentRepo, DocumentRepo>();
builder.Services.AddScoped<IAgentRepo, AgentRepo>();
builder.Services.AddScoped<ILeaveRepo, LeaveRepo>();
builder.Services.AddLogic();
builder.Services.AddRag(builder.Configuration);
builder.Services.AddHrmsAgentFramework(builder.Configuration);

var app = builder.Build();
app.UseHrmsPipeline();
app.Run();
