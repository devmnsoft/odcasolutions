using Microsoft.Extensions.DependencyInjection;
using Odca.Application.Operations;

namespace Odca.Infrastructure.Operations;

public static class OperationsServiceCollectionExtensions
{
    public static IServiceCollection AddOdcaOperationalInbox(this IServiceCollection services)
    {
        services.AddScoped<OperationalInboxRepository>();
        services.AddScoped<IOperationalInboxRepository>(provider => provider.GetRequiredService<OperationalInboxRepository>());
        services.AddScoped<IMonthlyAgendaRepository>(provider => provider.GetRequiredService<OperationalInboxRepository>());
        services.AddScoped<IContractSheetRepository, ContractSheetRepository>();
        services.AddScoped<OperationalInboxService>();
        services.AddScoped<MonthlyAgendaService>();
        services.AddScoped<ContractSheetService>();
        return services;
    }
}
