using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "OutboundAccess")]
    public class DispatchController(DispatchService dispatchService) : ControllerBase
    {
        private readonly DispatchService _dispatchService = dispatchService;

        // GET /api/dispatch
        [HttpGet]
        public async Task<IActionResult> GetDispatchList()
        {
            try
            {
                var items = await _dispatchService.GetDispatchListAsync();
                return Ok(new { success = true, data = items, count = items.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // GET /api/dispatch/staging-items
        [HttpGet("staging-items")]
        public async Task<IActionResult> GetStagingItems()
        {
            try
            {
                var items = await _dispatchService.GetStagingItemsAsync();
                return Ok(new { success = true, data = items, count = items.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // POST /api/dispatch
        [HttpPost]
        public async Task<IActionResult> CreateDispatch([FromBody] CreateDispatchRequest request)
        {
            try
            {
                var result = await _dispatchService.CreateDispatchAsync(request);
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

        // POST /api/dispatch/{id}/cancel
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelDispatch(int id)
        {
            try
            {
                var result = await _dispatchService.CancelDispatchAsync(id);
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

        // POST /api/dispatch/{id}/confirm
        [HttpPost("{id}/confirm")]
        public async Task<IActionResult> ConfirmDispatch(int id, [FromBody] ConfirmDispatchRequest? request)
        {
            try
            {
                var result = await _dispatchService.ConfirmDispatchAsync(id, request ?? new ConfirmDispatchRequest());
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
