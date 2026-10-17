using Relia.Api.Contracts;
using Relia.Api.Persistence;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>Panel del Modo demo: fallos forzados y reinicio. "Vencer ya" vive en <see cref="IOperationService"/>.</summary>
public interface IDemoService
{
    DemoFaultsDto GetFaults();

    DemoFaultsDto SetFaults(SetDemoFaultsRequest request);

    /// <summary>
    /// Cancela en el riel los cobros pendientes, vacía los datos, vuelve a sembrar y apaga los fallos forzados.
    /// El dinero ya movido en las billeteras reales no se devuelve.
    /// </summary>
    Task ResetAsync(CancellationToken ct = default);
}

public class DemoService(
    IPaymentRepository payments, IRailClient rail, DemoSeeder seeder, DemoFaults faults, ILogger<DemoService> logger)
    : IDemoService
{
    public DemoFaultsDto GetFaults() => new(faults.FailNextPayment);

    public DemoFaultsDto SetFaults(SetDemoFaultsRequest request)
    {
        // [Required] en el DTO garantiza el valor.
        faults.FailNextPayment = request.FailNextPayment!.Value;
        return GetFaults();
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        foreach (var charge in await payments.ListPendingRailChargesAsync(ct))
        {
            try
            {
                await rail.CancelPaymentAsync(charge.RailPaymentId!.Value, ct);
            }
            catch (RailException e)
            {
                // Ya aprobado (payment_not_cancellable) o riel caído: el reinicio sigue; ese cobro queda en el
                // riel sin operación que lo espere.
                logger.LogWarning(e, "El reinicio no pudo cancelar en el riel el pago {PaymentId} ({Code})",
                    charge.Id, e.Code);
            }
        }

        await seeder.ResetAsync(ct);
        faults.FailNextPayment = false;
        logger.LogInformation("Demo reiniciada");
    }
}
