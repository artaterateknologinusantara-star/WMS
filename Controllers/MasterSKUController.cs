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
    public class MasterSKUController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MasterSKUController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /api/mastersku
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var skus = await _context.MasterSKUs
                .OrderBy(s => s.SKUCode)
                .Select(s => new
                {
                    id           = s.Id,
                    skuCode      = s.SKUCode ?? string.Empty,
                    skuName      = s.SKUName ?? string.Empty,
                    categoryId   = s.CategoryId,
                    categoryName = s.Category != null ? s.Category.CategoryName ?? string.Empty : string.Empty,
                    uomId        = s.UOMId,
                    uomCode      = s.UOM != null ? s.UOM.UOMCode ?? string.Empty : string.Empty,
                    isActive     = s.Status == "Active",
                    qty          = s.Qty,
                })
                .ToListAsync();

            return Ok(new { success = true, data = skus });
        }

        // GET /api/mastersku/categories
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .OrderBy(c => c.CategoryName)
                .Select(c => new { id = c.Id, categoryCode = c.CategoryCode, categoryName = c.CategoryName })
                .ToListAsync();

            return Ok(new { success = true, data = categories });
        }

        // POST /api/mastersku
        [HttpPost]
        [Authorize(Policy = "ManagerOnly")]
        public async Task<IActionResult> Create([FromBody] SaveSKURequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SKUCode))
                return BadRequest(new { success = false, message = "SKU Code is required." });
            if (string.IsNullOrWhiteSpace(request.SKUName))
                return BadRequest(new { success = false, message = "SKU Name is required." });

            var exists = await _context.MasterSKUs
                .AnyAsync(s => s.SKUCode == request.SKUCode.Trim());
            if (exists)
                return BadRequest(new { success = false, message = $"SKU Code '{request.SKUCode}' already exists." });

            var sku = new MasterSKU
            {
                SKUCode    = request.SKUCode.Trim(),
                SKUName    = request.SKUName.Trim(),
                CategoryId = request.CategoryId,
                UOMId      = request.UOMId,
                Status     = "Active",
                CreatedAt  = DateTime.UtcNow,
                UpdatedAt  = DateTime.UtcNow,
            };

            _context.MasterSKUs.Add(sku);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    id       = sku.Id,
                    skuCode  = sku.SKUCode,
                    skuName  = sku.SKUName,
                    isActive = true,
                }
            });
        }

        // PUT /api/mastersku/{id}
        [HttpPut("{id}")]
        [Authorize(Policy = "ManagerOnly")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveSKURequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SKUCode))
                return BadRequest(new { success = false, message = "SKU Code is required." });
            if (string.IsNullOrWhiteSpace(request.SKUName))
                return BadRequest(new { success = false, message = "SKU Name is required." });

            var sku = await _context.MasterSKUs.FindAsync(id);
            if (sku == null)
                return NotFound(new { success = false, message = "SKU not found." });

            var codeConflict = await _context.MasterSKUs
                .AnyAsync(s => s.SKUCode == request.SKUCode.Trim() && s.Id != id);
            if (codeConflict)
                return BadRequest(new { success = false, message = $"SKU Code '{request.SKUCode}' is already used by another SKU." });

            sku.SKUCode    = request.SKUCode.Trim();
            sku.SKUName    = request.SKUName.Trim();
            sku.CategoryId = request.CategoryId;
            sku.UOMId      = request.UOMId;
            sku.UpdatedAt  = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "SKU updated." });
        }

        // PATCH /api/mastersku/{id}/deactivate
        [HttpPatch("{id}/deactivate")]
        [Authorize(Policy = "ManagerOnly")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var sku = await _context.MasterSKUs.FindAsync(id);
            if (sku == null)
                return NotFound(new { success = false, message = "SKU not found." });

            if (sku.Status != "Active")
                return BadRequest(new { success = false, message = $"SKU '{sku.SKUCode}' is already inactive." });

            sku.Status    = "Inactive";
            sku.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"SKU '{sku.SKUCode}' deactivated." });
        }
    }

    public class SaveSKURequest
    {
        public string  SKUCode    { get; set; } = string.Empty;
        public string  SKUName    { get; set; } = string.Empty;
        public int?    CategoryId { get; set; }
        public int?    UOMId      { get; set; }
    }
}
