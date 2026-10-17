using Microsoft.Extensions.DependencyInjection;
using ReceiptReader.Application.Accounting;
using ReceiptReader.Application.Companies;
using ReceiptReader.Application.Inventory;
using ReceiptReader.Application.Learning;
using ReceiptReader.Application.Receipts;
using ReceiptReader.Application.Users;

namespace ReceiptReader.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ReceiptService>();
        services.AddScoped<ReceiptProcessingService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<AccountingService>();
        services.AddScoped<LearningService>();
        services.AddScoped<UserService>();
        services.AddScoped<CompanyAccess>();
        services.AddScoped<InventoryService>();
        services.AddScoped<CodeSchemeService>();
        return services;
    }
}
