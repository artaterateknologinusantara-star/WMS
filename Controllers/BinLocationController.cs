using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AnyStaff")]
    public class BinLocationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BinLocationController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /api/binlocation
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var occupiedRackIds = await _context.InventoryStocks
                .Where(s => s.Status == "Active" || s.Status == "Outbound Staging")
                .Select(s => s.RackId)
                .Distinct()
                .ToListAsync();

            var bins = await _context.BinLocations
                .OrderBy(b => b.Zone)
                .ThenBy(b => b.BinCode)
                .Select(b => new
                {
                    id          = b.Id,
                    binCode     = b.BinCode,
                    zone        = b.Zone,
                    rack        = b.Rack,
                    capacityQty = b.CapacityQty,
                    isActive    = b.IsActive,
                })
                .ToListAsync();

            var result = bins.Select(b => new
            {
                b.id,
                b.binCode,
                b.zone,
                b.rack,
                b.capacityQty,
                b.isActive,
                isOccupied = occupiedRackIds.Contains(b.id),
            });

            return Ok(new { success = true, data = result });
        }

        // POST /api/binlocation
        [HttpPost]
        [Authorize(Policy = "ManagerOnly")]
        public async Task<IActionResult> Create([FromBody] CreateBinRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BinCode))
                return BadRequest(new { success = false, message = "BinCode is required." });
            if (string.IsNullOrWhiteSpace(request.Zone))
                return BadRequest(new { success = false, message = "Zone is required." });

            var exists = await _context.BinLocations
                .AnyAsync(b => b.BinCode == request.BinCode.Trim());
            if (exists)
                return BadRequest(new { success = false, message = $"BinCode '{request.BinCode}' already exists." });

            var bin = new BinLocation
            {
                BinCode     = request.BinCode.Trim(),
                Zone        = request.Zone.Trim(),
                Rack        = request.Rack?.Trim(),
                CapacityQty = request.CapacityQty,
                IsActive    = true,
                WarehouseId = 1,
                CreatedAt   = DateTime.UtcNow,
            };

            _context.BinLocations.Add(bin);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    id          = bin.Id,
                    binCode     = bin.BinCode,
                    zone        = bin.Zone,
                    rack        = bin.Rack,
                    capacityQty = bin.CapacityQty,
                    isActive    = bin.IsActive,
                    isOccupied  = false,
                }
            });
        }

        // PATCH /api/binlocation/{id}/toggle-active
        [HttpPatch("{id}/toggle-active")]
        [Authorize(Policy = "ManagerOnly")]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var bin = await _context.BinLocations.FindAsync(id);
            if (bin == null)
                return NotFound(new { success = false, message = "Bin location not found." });

            // Guard: don't deactivate an occupied bin
            if (bin.IsActive)
            {
                var occupied = await _context.InventoryStocks
                    .AnyAsync(s => s.RackId == id &&
                                   (s.Status == "Active" || s.Status == "Outbound Staging"));
                if (occupied)
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Cannot deactivate '{bin.BinCode}': bin is currently occupied."
                    });
            }

            bin.IsActive = !bin.IsActive;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                data = new { id = bin.Id, binCode = bin.BinCode, isActive = bin.IsActive }
            });
        }
    }

    public class CreateBinRequest
    {
        public string  BinCode     { get; set; } = string.Empty;
        public string  Zone        { get; set; } = string.Empty;
        public string? Rack        { get; set; }
        public int     CapacityQty { get; set; }
    }
}
