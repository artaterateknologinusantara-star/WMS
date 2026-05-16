using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;
using Syntera.WMS.API.Models.Dtos;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly InventoryService _inventoryService;

        public InventoryController(ApplicationDbContext context, InventoryService inventoryService)
        {
            _context = context;
            _inventoryService = inventoryService;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                var data = await _inventoryService.GetAllAsync();

                var result = data
                    .Select(x => new
                    {
                        id = x.StockId,
                        skuNumber = x.SKUCode,
                        skuName = x.SKUName,
                        category = string.Empty, // Will be populated if needed
                        palletId = x.PalletId,
                        binLocation = x.BinLocation,
                        quantity = x.Qty,
                        uom = x.UOM,
                        status = x.Status,
                        lastMovement = x.LastMovementDate
                    })
                    .ToList();

                return Ok(new
                {
                    success = true,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("by-code/{skuCode}")]
        public async Task<IActionResult> GetByCode(string skuCode)
        {
            var inventory = await _inventoryService.GetByCodeAsync(skuCode);
            if (inventory == null)
                return NotFound(new { success = false, message = "SKU not found." });

            return Ok(new { success = true, data = inventory });
        }

        [HttpGet("uom")]
        public async Task<IActionResult> GetUOM()
        {
            var uoms = await _context.UOM
                .OrderBy(u => u.UOMCode)
                .Select(u => new { id = u.Id, uomCode = u.UOMCode, uomName = u.UOMName })
                .ToListAsync();

            return Ok(new { success = true, data = uoms });
        }
    }
}