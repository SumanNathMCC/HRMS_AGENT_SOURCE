using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;
using HRMS_CHATBOT_SOURCE.Domain.Extensions;
using HRMS_CHATBOT_SOURCE.Domain.Interfaces;
using HRMS_CHATBOT_SOURCE.Domain.Json;
using HRMS_CHATBOT_SOURCE.Infrastructure.Core;
using HRMS_CHATBOT_SOURCE.Infrastructure.Interfaces;
using HRMS_CHATBOT_SOURCE.Infrastructure.Security;
using HRMS_CHATBOT_SOURCE.Infrastructure.Services;
using MCC.Foundation.Chunker.Abstractions;
using MCC.Foundation.Chunker;
using MCC.Foundation.CosmosHelper;
using MCC.Foundation.MSSQLHelper.Extension;
using MCC.Foundation.QdrantHelper;
using MCC.Foundation.QdrantHelper.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
namespace HRMS_CHATBOT_SOURCE.Infrastructure.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHrmsCoreServices(
        this IServiceCollection services,
        IConfiguration configuration,
        ApplicationSecrets applicationSecrets)
    {
        services.Configure<AzureKeyVaultSettings>(configuration.GetSection(AzureKeyVaultSettings.SectionName));
        services.Configure<AppSettings>(configuration.GetSection(AppSettings.SectionName));
        services.Configure<AzureSpeechSettings>(configuration.GetSection(AzureSpeechSettings.SectionName));

        services.AddHttpContextAccessor();
        services.AddMemoryCache();        services.AddScoped<IServiceContext, ServiceContext>();

        services.AddSingleton(applicationSecrets);
        services.AddSingleton<IAzureKeyVaultService, AzureKeyVaultService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ISpeechTranslationService, AzureSpeechTranslationService>();
        services.AddSingleton<JwtTokenValidator>();

        return services;
    }

    public static IServiceCollection AddHrmsMvc(this IServiceCollection services)    {
        services.AddControllersWithViews()
            .AddNewtonsoftJson(options =>
            {
                var jsonSettings = DomainJsonSerializerSettings.Default;
                options.SerializerSettings.ContractResolver = jsonSettings.ContractResolver;
                options.SerializerSettings.NullValueHandling = jsonSettings.NullValueHandling;
                options.SerializerSettings.ReferenceLoopHandling = jsonSettings.ReferenceLoopHandling;
            })
            .AddRazorOptions(options =>
            {
                options.ViewLocationFormats.Clear();
                options.ViewLocationFormats.Add("/web/Views/{1}/{0}.cshtml");
                options.ViewLocationFormats.Add("/web/Views/Shared/{0}.cshtml");
                options.AreaViewLocationFormats.Clear();
                options.AreaViewLocationFormats.Add("/web/Areas/{2}/Views/{1}/{0}.cshtml");
                options.AreaViewLocationFormats.Add("/web/Areas/{2}/Views/Shared/{0}.cshtml");
                options.AreaViewLocationFormats.Add("/web/Views/Shared/{0}.cshtml");
            });

        return services;
    }

    public static IServiceCollection AddHrmsCors(this IServiceCollection services, IConfiguration configuration)
    {
        var corsSettings = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>()
            ?? new CorsSettings();

        services.AddCors(options =>
        {
            options.AddPolicy("HrmsCorsPolicy", policy =>
            {
                if (corsSettings.AllowedOrigins.Length > 0)
                {
                    policy.WithOrigins(corsSettings.AllowedOrigins);
                }
                else
                {
                    policy.SetIsOriginAllowed(_ => true);
                }

                policy.WithMethods(corsSettings.AllowedMethods);
                policy.WithHeaders(corsSettings.AllowedHeaders);

                if (corsSettings.AllowCredentials)
                {
                    policy.AllowCredentials();
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddHrmsSession(this IServiceCollection services, IConfiguration configuration)
    {
        var sessionSettings = configuration.GetSection(SessionSettings.SectionName).Get<SessionSettings>()
            ?? new SessionSettings();

        services.AddDistributedMemoryCache();
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(sessionSettings.IdleTimeoutMinutes);
            options.Cookie.Name = sessionSettings.CookieName;
            options.Cookie.HttpOnly = sessionSettings.CookieHttpOnly;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        return services;
    }

    public static IServiceCollection AddHrmsFoundationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        ApplicationSecrets applicationSecrets)
    {
        services.AddMSSQLHelperService();

        if (!string.IsNullOrWhiteSpace(applicationSecrets.CosmosSecret))
        {
            services.AddAzureCosmosService(applicationSecrets.CosmosSecret);
        }

        services.AddQdrantService(new QdrantConnectionModel
        {
            Host = applicationSecrets.QdrantEndpoint ?? string.Empty,
            ApiKey = applicationSecrets.QdrantApiKey ?? string.Empty
        });

        AddHrmsDocumentChunker(services, configuration);
        services.AddHrmsStorageServices(configuration);

        return services;
    }

    private static void AddHrmsDocumentChunker(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDocumentChunker(configuration);

        var primaryRegistrationIndex = services
            .Select((descriptor, index) => (descriptor, index))
            .LastOrDefault(entry => entry.descriptor.ServiceType == typeof(IDocumentChunkerService))
            .index;

        if (primaryRegistrationIndex < 0)
        {
            return;
        }

        var primaryRegistration = services[primaryRegistrationIndex];
        services.RemoveAt(primaryRegistrationIndex);

        services.Insert(primaryRegistrationIndex, new ServiceDescriptor(
            typeof(IDocumentChunkerService),
            sp => new ResilientDocumentChunkerService(
                CreateDocumentChunkerService(sp, primaryRegistration),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<ResilientDocumentChunkerService>>()),
            primaryRegistration.Lifetime));
    }

    private static IDocumentChunkerService CreateDocumentChunkerService(
        IServiceProvider serviceProvider,
        ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IDocumentChunkerService instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (IDocumentChunkerService)factory(serviceProvider);
        }

        if (descriptor.ImplementationType is { } implementationType)
        {
            return (IDocumentChunkerService)ActivatorUtilities.CreateInstance(serviceProvider, implementationType);
        }

        throw new InvalidOperationException("Unable to create the primary document chunker service.");
    }

    public static IServiceCollection AddHrmsAuthentication(this IServiceCollection services)
    {
        // Admin auth is handled by JwtValidationMiddleware + AdminAuthorizeAttribute.
        // MCC.Foundation.Authentication uses embedded PEM keys and the same cookie name,
        // which rejects tokens signed with AppSettings:AdminPrivate ("Invalid token").
        return services;
    }
    public static IServiceCollection AddHrmsSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "HRMS Chatbot API",
                Version = "v1",
                Description = "Development APIs. Use Chat to send a turn through the Supervisor handoff workflow."
            });
            options.DocInclusionPredicate((_, apiDescription) =>
                apiDescription.ActionDescriptor is ControllerActionDescriptor descriptor
                && descriptor.ControllerTypeInfo.IsDefined(typeof(ApiControllerAttribute), inherit: true));
        });
        services.AddSwaggerGenNewtonsoftSupport();
        return services;
    }
}
