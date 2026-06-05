using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "OutboundAccess")]
    public class PickingController(PickingService pickingService, ApplicationDbContext context) : ControllerBase
    {
        private readonly PickingService _pickingService = pickingService;
        private readonly ApplicationDbContext _context = context;

        /// <summary>
        /// Live stock check for the Create Picking form.
        /// Returns available qty and FIFO-suggested bin/pallet for the given SKU.
        /// </summary>
        [HttpGet("check-stock")]
        public async Task<IActionResult> CheckStock([FromQuery] string skuCode)
        {
            if (string.IsNullOrWhiteSpace(skuCode))
                return BadRequest(new { success = false, message = "skuCode is required." });

            var sku = await _context.MasterSKUs
                .FirstOrDefaultAsync(s => s.SKUCode == skuCode.Trim());

            if (sku == null)
                return NotFound(new { success = false, message = $"SKU '{skuCode}' tidak ditemukan." });

            // Total available qty across all active pallets
            var totalAvailable = await _context.InventoryStocks
                .Where(s => s.SKUId == sku.Id && s.Status == "Active")
                .SumAsync(s => (int?)s.AvailableQty) ?? 0;

            // Best pallet via FIFO — same logic as CreatePickingAsync
            var best = await _context.InventoryStocks
                .Include(s => s.Rack)
                .Where(s =>
                    s.SKUId == sku.Id &&
                    s.Status == "Active" &&
                    s.AvailableQty > 0 &&
                    !_context.PickingDetails.Any(pd =>
                        pd.InventoryStockId == s.Id &&
                        (pd.Status == "pending" || pd.Status == "in-progress")))
                .OrderBy(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    skuCode      = sku.SKUCode,
                    skuName      = sku.SKUName,
                    availableQty = totalAvailable,
                    suggestedBin    = best?.Rack?.BinCode ?? string.Empty,
                    suggestedPallet = best?.PalletId     ?? string.Empty,
                    bestAvailableQty = best?.AvailableQty ?? 0,
                }
            });
        }

        [HttpGet("staging-locations")]
        public async Task<IActionResult> GetStagingLocations()
        {
            try
            {
                var locations = await _context.BinLocations
                    .Where(b => b.Zone == "Outbound Staging" && b.IsActive == true)
                    .OrderBy(b => b.BinCode)
                    .Select(b => new { id = b.Id, binCode = b.BinCode, rack = b.Rack })
                    .ToListAsync();

                return Ok(new { success = true, data = locations });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPickingList()
        {
            try
            {
                var items = await _pickingService.GetPickingListAsync();
                return Ok(new { success = true, data = items, count = items.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePicking([FromBody] CreatePickingRequest request)
        {
            try
            {
                var result = await _pickingService.CreatePickingAsync(request);
                return Ok(new { success = true, data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("{headerId}/force-complete")]
        public async Task<IActionResult> ForceComplete(int headerId)
        {
            try
            {
                var result = await _pickingService.ForceCompleteAsync(headerId);
                return Ok(new { success = true, data = result });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelPick(int id)
        {
            try
            {
                var result = await _pickingService.CancelPickAsync(id);
                return Ok(new { success = true, data = result });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("{id}/confirm")]
        public async Task<IActionResult> ConfirmPick(int id, [FromBody] ConfirmPickRequest? request)
        {
            try
            {
                var result = await _pickingService.ConfirmPickAsync(id, request ?? new ConfirmPickRequest());
                return Ok(new { success = true, data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
