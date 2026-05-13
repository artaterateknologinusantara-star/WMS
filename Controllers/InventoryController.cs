
using Microsoft.AspNetCore.Mvc;

namespace Syntera.WMS.API.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    [HttpGet("stock-on-hand")]
    public IActionResult GetStock()
    {
        var stock = new[]
        {
            new { SKU = "CEMENT-001", Qty = 120 },
            new { SKU = "STEEL-002", Qty = 75 },
            new { SKU = "PAINT-003", Qty = 43 }
        };

        return Ok(new
        {
            success = true,
            data = stock
        });
    }
}
