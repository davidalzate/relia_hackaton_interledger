using Relia.Api.Repositories;

namespace Relia.Api.Services.Operations;

/// <summary>Algo pendiente de un pago de riel que el barrido debe hacer avanzar.</summary>
public interface ISweepable
{
    Task SweepAsync(CancellationToken ct = default);
}

/// <summary>
/// Ticket 13: avanza operaciones (y liquidaciones, que se registran como otro ISweepable) pendientes aunque
/// el frontend no las consulte: refresca su pago en el riel y vence las solicitudes pasadas de plazo.
/// </summary>
public class PaymentSweeper(IOperationRepository operations, OperationTracker tracker,
    IEnumerable<ISweepable> others, ILogger<PaymentSweeper> log)
{
    public async Task SweepAsync(CancellationToken ct = default)
    {
        foreach (var op in await operations.ListPendingAsync(ct))
        {
            try
            {
                await tracker.RefreshAsync(op, ct, force: true);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "El barrido no pudo avanzar la operación {OperationId}", op.Id);
            }
        }

        foreach (var other in others) await other.SweepAsync(ct);
    }
}

/// <summary>Corre el barrido cada 15 s. Se apaga con Sweep:Enabled=false (las pruebas lo llaman a mano).</summary>
public class PaymentSweepService(IServiceScopeFactory scopes, IConfiguration config, ILogger<PaymentSweepService> log)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Sweep:Enabled", true)) return;

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PaymentSweeper>().SweepAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Falló el barrido de pagos pendientes");
            }
        }
    }
}
