using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Extraction;
using ReceiptReader.Application.Receipts;
using ReceiptReader.Infrastructure.Extraction;
using ReceiptReader.Infrastructure.Persistence;
using ReceiptReader.Infrastructure.Processing;
using ReceiptReader.Infrastructure.Security;
using ReceiptReader.Infrastructure.Storage;

namespace ReceiptReader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ExtractionOptions>(config.GetSection(ExtractionOptions.Section));
        services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
        services.Configure<ProcessingOptions>(config.GetSection(ProcessingOptions.Section));

        services.AddDbContext<AppDbContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Default")
                        ?? throw new InvalidOperationException("ConnectionStrings:Default is missing")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IImagePreprocessor, ImageSharpPreprocessor>();

        services.AddHttpClient<OllamaClient>((sp, http) =>
        {
            var opt = sp.GetRequiredService<IOptions<ExtractionOptions>>().Value;
            http.BaseAddress = new Uri(opt.OllamaBaseUrl);
            http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
        });

        services.AddScoped<ILanguageModel, OllamaLanguageModel>();
        services.AddScoped<VisionLlmExtractor>();
        services.AddScoped<OcrThenLlmExtractor>();
        services.AddScoped<IReceiptExtractor>(sp =>
            sp.GetRequiredService<IOptions<ExtractionOptions>>().Value.Mode switch
            {
                ExtractionMode.OcrThenLlm => sp.GetRequiredService<OcrThenLlmExtractor>(),
                _ => sp.GetRequiredService<VisionLlmExtractor>(),
            });

        services.AddSingleton<ChannelProcessingQueue>();
        services.AddSingleton<IProcessingQueue>(sp => sp.GetRequiredService<ChannelProcessingQueue>());
        services.AddHostedService<ReceiptProcessingWorker>();

        return services;
    }
}
