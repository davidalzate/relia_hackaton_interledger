using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Relia.Api.Persistence;
using Relia.Api.Rail;

namespace Relia.Api.Tests.Support;

/// <summary>
/// La Relia API completa en memoria. Costuras: el riel (<see cref="FakeRailClient"/>), el reloj
/// (<see cref="FakeTimeProvider"/>) y la base (EF InMemory: no hay Docker para Postgres en Testcontainers,
/// así que los triggers y CHECK del esquema no se prueban aquí).
/// </summary>
public class ReliaApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Start = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    private readonly string _database = $"relia-{Guid.NewGuid()}";

    public FakeRailClient Rail { get; } = new();
    public FakeTimeProvider Clock { get; } = new(Start);

    public bool DemoMode { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DEMO_MODE", DemoMode ? "true" : "false");
        builder.UseSetting("Sweep:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Relia", "Host=unused");
        builder.ConfigureAppConfiguration(config =>
            config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "demo-seed.json"), optional: false));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ReliaDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ReliaDbContext>>();
            services.AddDbContext<ReliaDbContext>(o => o
                .UseInMemoryDatabase(_database)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            services.RemoveAll<IRailClient>();
            services.AddSingleton<IRailClient>(Rail);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Cliente HTTP de la API cuyas respuestas se validan contra el contrato (ticket 17).</summary>
    public new HttpClient CreateClient() => CreateDefaultClient(new ContractCheckingHandler(ContractValidator.Relia));

    /// <summary>Datos de la demo: 4 corresponsales (Rosa en ceros), 3000000001 con llave, 3000000002 sin llave.</summary>
    public async Task<ReliaApiFactory> ResetAsync()
    {
        var response = await CreateClient().PostAsync("/demo/reset", null);
        response.EnsureSuccessStatusCode();
        return this;
    }

    public async Task<HttpClient> LoginAsync(string email, string password = Seed.Password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    public Task<HttpClient> RosaAsync() => LoginAsync(Seed.RosaEmail);

    public Task<HttpClient> AdminAsync() => LoginAsync(Seed.AdminEmail);

    public T Service<T>() where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private record LoginBody(string AccessToken);
}

/// <summary>Datos que siembra POST /demo/reset con demo-seed.json.</summary>
public static class Seed
{
    public const string Password = "demo1234";
    public const string RosaEmail = "rosa@relia.dev";
    public const string AdminEmail = "admin@relia.dev";
    public const string LinkedPhone = "3000000001";
    public const string UnlinkedPhone = "3000000002";
}
