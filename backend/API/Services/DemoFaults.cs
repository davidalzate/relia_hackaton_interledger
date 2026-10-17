using Relia.Api.Domain;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Fallos forzados del Modo demo, en memoria (singleton). "El próximo pago falla" es de un solo uso: el primer
/// depósito, retiro, carga o liquidación que lo encuentra lo consume y queda fallido sin llamar al riel.
/// </summary>
public class DemoFaults
{
    /// <summary>failureDetail de la operación o voidReason de la liquidación que falló por el interruptor.</summary>
    public const string ForcedFailureDetail = "Fallo forzado desde el Modo demo";

    private int _failNextPayment;

    public bool FailNextPayment
    {
        get => Volatile.Read(ref _failNextPayment) == 1;
        set => Volatile.Write(ref _failNextPayment, value ? 1 : 0);
    }

    /// <summary>true si estaba encendido; lo apaga en el mismo paso, así dos peticiones no lo usan las dos.</summary>
    private bool TryConsumeFailNextPayment() => Interlocked.Exchange(ref _failNextPayment, 0) == 1;

    /// <summary>
    /// Si el interruptor estaba encendido, lo apaga y marca el pago fallido por el Modo demo (payment_failed y su
    /// evento simulate); quien llama cierra la operación o la liquidación sin llamar al riel.
    /// </summary>
    public bool TryFailPayment(IPaymentRepository payments, Payment payment, DateTimeOffset now)
    {
        if (!TryConsumeFailNextPayment()) return false;
        payment.ErrorCode = "payment_failed";
        payment.ErrorMessage = ForcedFailureDetail;
        payments.AddEvent(new PaymentEvent
        {
            PaymentId = payment.Id, Action = "simulate", PreviousState = payment.State, NewState = OperationState.Failed,
            Origin = EventOrigin.Simulator, Detail = ForcedFailureDetail, CreatedAt = now,
        });
        return true;
    }
}
