using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

// Los stubs aún no usan sus dependencias; se quita al implementar el último servicio.
#pragma warning disable CS9113

// Implementaciones en desarrollo: reciben ya sus repositorios y responden 501 "En desarrollo".
// Cada una se implementa en el ticket indicado de docs/issues/backend-relia-api/.

/// <summary>Ticket 13.</summary>
public class SettlementService(ISettlementRepository settlements, ICurrentUser currentUser) : ISettlementService
{
    public Task<SettlementPageDto> ListAsync(SettlementQuery query, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("listSettlements");

    public Task<SettlementDto> GetAsync(Guid settlementId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getSettlement");
}
