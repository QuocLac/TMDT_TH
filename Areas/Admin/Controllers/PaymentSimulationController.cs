using Microsoft.AspNetCore.Mvc;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/PaymentSimulation")]
public sealed class PaymentSimulationController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(
        "Index",
        "Returns",
        new
        {
            area = "Admin",
            status = ReturnRequestStatus.RefundPending
        });

    [HttpPost("{*path}")]
    public IActionResult LegacyAction() => RedirectToAction(
        "Index",
        "Returns",
        new
        {
            area = "Admin",
            status = ReturnRequestStatus.RefundPending
        });
}
