using Microsoft.AspNetCore.Mvc;
using Rose.Application.Tracker;

namespace Rose.Api.Controllers;

[Tags("Payments")]
public sealed class PaymentsController(PaymentTrackerService tracker) : TrackerControllerBase
{
    /// <summary>Record a payment. Date and time default to now; status defaults to Paid.</summary>
    [HttpPost("payments")]
    [ProducesResponseType<PaymentCreated>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Add(AddPaymentRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await tracker.AddPaymentAsync(request, ct));

    /// <summary>Mark a payment Paid or Unpaid.</summary>
    /// <response code="404">Payment not found.</response>
    [HttpPatch("payments/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaymentUpdated> Update(string id, UpdatePaymentRequest request, CancellationToken ct) =>
        tracker.SetPaymentStatusAsync(id, request, ct);

    /// <summary>Delete one payment.</summary>
    /// <response code="404">Payment not found.</response>
    [HttpDelete("payments/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OkResponse> Delete(string id, CancellationToken ct)
    {
        await tracker.DeletePaymentAsync(id, ct);
        return new OkResponse();
    }
}