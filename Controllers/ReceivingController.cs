using Microsoft.AspNetCore.Mvc;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReceivingController : ControllerBase
    {
        private readonly ReceivingService _receivingService;

        public ReceivingController(ReceivingService receivingService)
        {
            _receivingService = receivingService;
        }

        [HttpGet]
        public async Task<IActionResult> GetReceivingHistory()
        {
            try
            {
                var result = await _receivingService.GetReceivingHistoryAsync();
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SubmitReceiving([FromBody] ReceivingSubmitRequest request)
        {
            if (request == null || request.Details == null || !request.Details.Any())
                return BadRequest(new { success = false, message = "Request must include receiving details." });

            try
            {
                var result = await _receivingService.SubmitReceivingAsync(request);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}