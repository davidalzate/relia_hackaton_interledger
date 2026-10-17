using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

// Los stubs aún no usan sus dependencias; se quita al implementar el último servicio.
#pragma warning disable CS9113

// Implementaciones en desarrollo: reciben ya sus repositorios y responden 501 "En desarrollo".
// Cada una se implementa en el ticket indicado de docs/issues/backend-relia-api/.

/// <summary>Ticket 08.</summary>
public class ReceiptService(
    IOperationRepository operations, IReceiptRepository receipts, ISimulatedMessageRepository messages,
    IUnitOfWork unitOfWork, ICurrentUser currentUser) : IReceiptService
{
    public Task<ReceiptDto> GetAsync(Guid operationId, ReceiptViewChannel channel, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getReceipt");

    public Task<ReceiptDeliveryDto> SendAsync(Guid operationId, SendReceiptRequest request,
        CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("sendReceipt");
}

/// <summary>Ticket 13.</summary>
public class SettlementService(ISettlementRepository settlements, ICurrentUser currentUser) : ISettlementService
{
    public Task<SettlementPageDto> ListAsync(SettlementQuery query, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("listSettlements");

    public Task<SettlementDto> GetAsync(Guid settlementId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getSettlement");
}
