using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BinLocationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BinLocationController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool available = false)
        {
            var query = _context.BinLocations
                .Where(b => b.IsActive)
                .AsQueryable();

            if (available)
            {
                var occupiedRackIds = _context.InventoryStocks
                    .Where(s => s.Status == "Active" || s.Status == "Outbound Staging")
                    .Select(s => s.RackId)
                    .Distinct();

                query = query.Where(b => !occupiedRackIds.Contains(b.Id));
            }

            var bins = await query
                .OrderBy(b => b.Zone)
                .ThenBy(b => b.BinCode)
                .Select(b => new
                {
                    id = b.Id,
                    binCode = b.BinCode,
                    zone = b.Zone,
                    rack = b.Rack
                })
                .ToListAsync();

            return Ok(new { success = true, data = bins });
        }
    }
}
