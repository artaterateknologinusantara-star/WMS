using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MasterSKUController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MasterSKUController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var skus = await _context.MasterSKUs
                .OrderBy(s => s.SKUCode)
                .Select(s => new
                {
                    id = s.Id,
                    skuCode = s.SKUCode,
                    skuName = s.SKUName,
                    qty = s.Qty
                })
                .ToListAsync();

            return Ok(new { success = true, data = skus });
        }
    }
}
