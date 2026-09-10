using Odca.Worker;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddDataProtection().SetApplicationName("ODCA Solutions");
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Database")??throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.")));

var host = builder.Build();
host.Run();
