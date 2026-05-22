using Microsoft.AspNetCore.Mvc;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PickingController(PickingService pickingService) : ControllerBase
    {
        private readonly PickingService _pickingService = pickingService;

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
