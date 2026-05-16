using Microsoft.AspNetCore.Mvc;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PutawayController(PutawayService putawayService) : ControllerBase
    {
        private readonly PutawayService _putawayService = putawayService;

        /// <summary>
        /// Confirm putaway — scan pallet barcode then scan bin/rack barcode.
        /// This is THE ONLY operation that creates InventoryStock records.
        /// </summary>
        [HttpPost("confirm")]
        public async Task<IActionResult> ConfirmPutaway([FromBody] PutawayRequest request)
        {
            if (request == null)
                return BadRequest(new { success = false, message = "Request body cannot be null." });

            if (string.IsNullOrWhiteSpace(request.PalletId))
                return BadRequest(new { success = false, message = "PalletId is required." });

            if (string.IsNullOrWhiteSpace(request.BinCode))
                return BadRequest(new { success = false, message = "BinCode is required." });

            try
            {
                var result = await _putawayService.ConfirmPutawayAsync(request);
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

        /// <summary>
        /// Get all pending putaway tasks (ReceivingDetails without a matching InventoryStock).
        /// </summary>
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingTasks([FromQuery] int? skuId = null)
        {
            try
            {
                var tasks = await _putawayService.GetPendingPutawayTasksAsync(skuId);
                return Ok(new { success = true, data = tasks, count = tasks.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get current stock position for a specific pallet (post-putaway).
        /// </summary>
        [HttpGet("stock/{palletId}")]
        public async Task<IActionResult> GetStockPosition(string palletId)
        {
            if (string.IsNullOrWhiteSpace(palletId))
                return BadRequest(new { success = false, message = "PalletId is required." });

            try
            {
                var stock = await _putawayService.GetStockByPalletAsync(palletId);

                if (stock == null)
                    return NotFound(new { success = false, message = $"No stock found for pallet {palletId}." });

                return Ok(new { success = true, data = stock });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
