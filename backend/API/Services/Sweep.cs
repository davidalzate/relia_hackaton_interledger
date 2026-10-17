using Microsoft.Extensions.Options;
using Relia.Api.Configuration;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Un seguimiento que corre en cada ciclo del barrido de fondo: operaciones y liquidaciones. Se registran con
/// <c>AddScoped&lt;ISweepTask, ...&gt;()</c>.
/// </summary>
public interface ISweepTask
{
    /// <summary>Para los logs.</summary>
    string Name { get; }

    Task RunAsync(CancellationToken ct);
}

/// <summary>
/// Avanza las operaciones pendientes aunque nadie las consulte: refresca su pago y vence las pasadas de plazo, con
/// la misma lógica que la lectura. Cada operación va en su propio scope, para que un fallo no frene a las demás.
/// </summary>
public class OperationSweepTask(IOperationRepository operations, IServiceScopeFactory scopes,
    ILogger<OperationSweepTask> logger) : ISweepTask
{
    public string Name => "operaciones";

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var id in await operations.ListPendingIdsAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IOperationRepository>();
                var sync = scope.ServiceProvider.GetRequiredService<IOperationSync>();
                if (await repository.GetByIdAsync(id, ct) is { } operation) await sync.RefreshAsync(operation, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "El barrido no pudo avanzar la operación {OperationId}", id);
            }
        }
    }
}

/// <summary>
/// Cada Relia:SweepIntervalSeconds corre todos los <see cref="ISweepTask"/>. Un fallo se registra y se
/// reintenta en el siguiente ciclo: el servicio no se cae.
/// </summary>
public class SweepService(IServiceScopeFactory scopes, IOptions<ReliaOptions> options, TimeProvider time,
    ILogger<SweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Un valor ≤ 0 haría lanzar a PeriodicTimer y tumbaría el host: mínimo 1 s.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.SweepIntervalSeconds)), time);
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            foreach (var task in scope.ServiceProvider.GetServices<ISweepTask>())
            {
                try
                {
                    await task.RunAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Falló el barrido de {Task}; se reintenta en el siguiente ciclo", task.Name);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
