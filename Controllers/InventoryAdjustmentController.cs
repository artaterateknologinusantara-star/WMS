using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryAdjustmentController(
        ApplicationDbContext context,
        AdjustmentApprovalService approvalService) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;
        private readonly AdjustmentApprovalService _approvalService = approvalService;

        // ----------------------------------------------------------------
        // GET  /api/inventoryadjustment
        // ----------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAdjustments()
        {
            var adjustments = await _context.InventoryAdjustments
                .Include(x => x.SKU)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    id = x.Id,
                    adjustmentNo = x.AdjustmentNo,
                    adjustmentType = x.AdjustmentType ?? string.Empty,
                    skuId = x.SKUId,
                    skuNumber = x.SKU != null ? x.SKU.SKUCode : string.Empty,
                    skuName = x.SKU != null ? x.SKU.SKUName : string.Empty,
                    palletId = x.PalletId ?? string.Empty,
                    prevQty = x.PrevQty,
                    newQty = x.NewQty,
                    diffQty = x.NewQty - x.PrevQty,
                    reason = x.Reason ?? string.Empty,
                    remarks = x.Remarks ?? string.Empty,
                    requestedBy = x.RequestedBy,
                    requestedAt = x.RequestedAt.HasValue ? x.RequestedAt.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty,
                    approvalStatus = x.ApprovalStatus ?? "Pending",
                    approvedBy = x.ApprovedBy,
                    approvedAt = x.ApprovedAt.HasValue ? x.ApprovedAt.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty,
                    rejectedReason = x.RejectedReason ?? string.Empty,
                    isProcessed = x.IsProcessed,
                    createdAt = x.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                })
                .ToListAsync();

            return Ok(new { success = true, data = adjustments });
        }

        // ----------------------------------------------------------------
        // POST  /api/inventoryadjustment
        // Creates a PENDING adjustment — no stock change at this point.
        // Stock is only updated upon approval (see /{id}/approve).
        // ----------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> PostAdjustment([FromBody] InventoryAdjustmentRequest request)
        {
            if (request == null)
                return BadRequest(new { success = false, message = "Request body is required." });

            if (string.IsNullOrWhiteSpace(request.SKUCode) && request.SKUId == null)
                return BadRequest(new { success = false, message = "SKUId or SKUCode is required." });

            if (request.NewQty < 0)
                return BadRequest(new { success = false, message = "NewQty must be zero or greater." });

            var sku = await _context.MasterSKUs
                .FirstOrDefaultAsync(x =>
                    (request.SKUId != null && x.Id == request.SKUId) ||
                    (!string.IsNullOrWhiteSpace(request.SKUCode) && x.SKUCode == request.SKUCode));

            if (sku == null)
                return NotFound(new { success = false, message = "SKU not found." });

            // Read current qty — pallet-specific if pallet provided, otherwise aggregate
            int prevQty;
            if (!string.IsNullOrWhiteSpace(request.PalletId))
            {
                var stock = await _context.InventoryStocks
                    .FirstOrDefaultAsync(x => x.SKUId == sku.Id && x.PalletId == request.PalletId);
                prevQty = stock?.Qty ?? sku.Qty;
            }
            else
            {
                prevQty = await _context.InventoryStocks
                    .Where(x => x.SKUId == sku.Id)
                    .SumAsync(x => (int?)x.Qty) ?? sku.Qty;
            }

            var adjustmentNo = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

            var adjustment = new InventoryAdjustment
            {
                AdjustmentNo = adjustmentNo,
                SKUId = sku.Id,
                PalletId = request.PalletId,
                PrevQty = prevQty,
                NewQty = request.NewQty,
                AdjustmentType = request.AdjustmentType,
                Reason = request.Reason,
                Remarks = request.Remarks,
                RequestedBy = request.RequestedBy,
                RequestedAt = DateTime.UtcNow,
                ApprovalStatus = "Pending",
                IsProcessed = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.InventoryAdjustments.Add(adjustment);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    adjustmentId = adjustment.Id,
                    adjustmentNo = adjustment.AdjustmentNo,
                    skuId = sku.Id,
                    skuCode = sku.SKUCode,
                    prevQty,
                    newQty = request.NewQty,
                    status = "Pending",
                    message = "Adjustment submitted. Awaiting approval."
                }
            });
        }

        // ----------------------------------------------------------------
        // POST  /api/inventoryadjustment/{id}/approve
        // Updates InventoryStock + creates StockMovement audit record.
        // ----------------------------------------------------------------
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveAdjustment(int id, [FromBody] ApproveRequest request)
        {
            if (request == null || request.ApprovedBy <= 0)
                return BadRequest(new { success = false, message = "ApprovedBy (user id) is required." });

            try
            {
                var result = await _approvalService.ApproveAdjustmentAsync(id, request.ApprovedBy);
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

        // ----------------------------------------------------------------
        // POST  /api/inventoryadjustment/{id}/reject
        // Status-only update; stock is NOT modified.
        // ----------------------------------------------------------------
        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectAdjustment(int id, [FromBody] RejectRequest request)
        {
            if (request == null)
                return BadRequest(new { success = false, message = "Request body is required." });

            if (request.RejectedBy <= 0)
                return BadRequest(new { success = false, message = "RejectedBy (user id) is required." });

            if (string.IsNullOrWhiteSpace(request.RejectionReason))
                return BadRequest(new { success = false, message = "RejectionReason is required." });

            try
            {
                var result = await _approvalService.RejectAdjustmentAsync(id, request.RejectedBy, request.RejectionReason);
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
    }

    // ---- Request DTOs ----

    public class InventoryAdjustmentRequest
    {
        public int? SKUId { get; set; }
        public string? SKUCode { get; set; }
        public string? PalletId { get; set; }
        public int NewQty { get; set; }
        public string? AdjustmentType { get; set; }
        public string? Reason { get; set; }
        public string? Remarks { get; set; }
        public int? RequestedBy { get; set; }
    }

    public class ApproveRequest
    {
        public int ApprovedBy { get; set; }
    }

    public class RejectRequest
    {
        public int RejectedBy { get; set; }
        public string RejectionReason { get; set; } = string.Empty;
    }
}
