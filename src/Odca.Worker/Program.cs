using Odca.AI;
using Odca.Worker;
using Npgsql;
using Odca.Configuration;
using Odca.Configuration.Localization;
using Odca.Infrastructure.Distributed;
using Odca.Infrastructure.Signatures;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMinutes(2));
var localConfiguration = LocalRuntimeConfiguration.Add(builder.Configuration, builder.Environment, args);
Console.WriteLine($"Configuração: ambiente={localConfiguration.Environment}; caminho={localConfiguration.Path ?? "não utilizado"}.");
// D-OC31 A.4: seleção condicional das chaves (dev: filesystem; produção: postgres v054).
builder.Services.AddOdcaDataProtection(builder.Configuration);
// D-OC53 (Sprint 19/F-E): provedor de IA para a analise automatica pos-extracao;
// sem configuracao valida cai em "none" (no-op honesto). A chave nunca e commitada.
builder.Services.AddOdcaAi(builder.Configuration);
// D-OC46 (Sprint 12/F-1): catálogos embarcados para o idioma preferido dos lembretes
// e do corpo SMTP (fallback pt-BR, camada canônica).
builder.Services.AddSingleton<LocalizationService>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<DocumentWorker>();
builder.Services.AddHostedService<ObligationReminderWorker>();
builder.Services.AddHostedService<RenewalReminderWorker>();
builder.Services.AddHostedService<RenewalApplicationWorker>();
builder.Services.AddHostedService<SlaViolationScanWorker>();
// D-OC37 (Sprint 3/A-1): provedor de envelope externo de assinatura + reconciliador periódico.
builder.Services.AddHostedService<SignatureEnvelopeWorker>();
builder.Services.AddOdcaSignatureEnvelopeProvider();
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Database")??throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.")));

var pickup = builder.Configuration["Notifications:DevelopmentPickupDirectory"];
var smtpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Notifications:Smtp:Host"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Notifications:Smtp:SenderAddress"]);
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(pickup))
{
    builder.Services.AddSingleton<Odca.Worker.Transports.INotificationTransport>(new Odca.Worker.Transports.FilePickupNotificationTransport(pickup));
}
else if (smtpConfigured)
{
    // B.1 (D-OC31): transporte real quando há configuração SMTP válida; sem ela o
    // stub continua falhando honestamente com notification_provider_missing.
    builder.Services.AddSingleton<Odca.Worker.Transports.INotificationTransport, Odca.Worker.Transports.SmtpNotificationTransport>();
}
else
{
    builder.Services.AddSingleton<Odca.Worker.Transports.INotificationTransport, Odca.Worker.Transports.StubProductionNotificationTransport>();
}

var host = builder.Build();
host.Run();
