using Odca.Worker;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Odca.Configuration;

var builder = Host.CreateApplicationBuilder(args);
var localConfiguration = LocalRuntimeConfiguration.Add(builder.Configuration, builder.Environment, args);
Console.WriteLine($"Configuração: ambiente={localConfiguration.Environment}; caminho={localConfiguration.Path ?? "não utilizado"}.");
builder.Services.AddHostedService<Worker>();
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "ODCA Solutions");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new System.IO.DirectoryInfo(keysPath));
}
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Database")??throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.")));

var pickup = builder.Configuration["Notifications:DevelopmentPickupDirectory"];
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(pickup))
{
    builder.Services.AddSingleton<Odca.Worker.Transports.INotificationTransport>(new Odca.Worker.Transports.FilePickupNotificationTransport(pickup));
}
else
{
    builder.Services.AddSingleton<Odca.Worker.Transports.INotificationTransport, Odca.Worker.Transports.StubProductionNotificationTransport>();
}

var host = builder.Build();
host.Run();
