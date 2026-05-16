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
        public async Task<IActionResult> GetAll()
        {
            var bins = await _context.BinLocations
                .Where(b => b.IsActive)
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
