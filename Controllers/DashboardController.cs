using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController(ApplicationDbContext context) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;

        // GET /api/dashboard/summary
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            try
            {
                var today = DateTime.UtcNow.Date;

                // ── Inbound ───────────────────────────────────────────────────
                var pendingPutaway = await _context.ReceivingDetails
                    .CountAsync(d => !_context.InventoryStocks.Any(s => s.PalletId == d.PalletId));

                var draftReceivings = await _context.ReceivingHeaders
                    .CountAsync(h => h.Status == "Draft");

                var totalReceivings = await _context.ReceivingHeaders.CountAsync();

                // ── Inventory ─────────────────────────────────────────────────
                var activeStocks = await _context.InventoryStocks
                    .Where(s => s.Status == "Active")
                    .ToListAsync();

                var stagingStocks = await _context.InventoryStocks
                    .Where(s => s.Status == "Outbound Staging")
                    .ToListAsync();

                // ── Outbound ──────────────────────────────────────────────────
                var pendingPicks = await _context.PickingDetails
                    .CountAsync(d => d.Status == "pending" || d.Status == "in-progress");

                var completedPicksToday = await _context.PickingDetails
                    .CountAsync(d => d.Status == "picked" && d.UpdatedAt.HasValue && d.UpdatedAt.Value.Date == today);

                var pendingDispatches = await _context.DispatchHeaders
                    .CountAsync(h => h.Status == "pending");

                var dispatchedToday = await _context.DispatchHeaders
                    .CountAsync(h => h.Status == "dispatched" && h.UpdatedAt.HasValue && h.UpdatedAt.Value.Date == today);

                // ── Adjustments ───────────────────────────────────────────────
                var pendingAdjustments = await _context.InventoryAdjustments
                    .CountAsync(a => a.ApprovalStatus == "Pending");

                // ── Stock by Zone ─────────────────────────────────────────────
                var stockByZone = await _context.InventoryStocks
                    .Include(s => s.Rack)
                    .Where(s => s.Status == "Active" && s.Qty > 0)
                    .GroupBy(s => s.Rack != null ? s.Rack.Zone : "Unknown")
                    .Select(g => new
                    {
                        zone    = g.Key,
                        pallets = g.Count(),
                        qty     = g.Sum(s => s.Qty)
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        inbound = new
                        {
                            pendingPutaway,
                            draftReceivings,
                            totalReceivings,
                        },
                        inventory = new
                        {
                            activePallets    = activeStocks.Count,
                            activeQty        = activeStocks.Sum(s => s.Qty),
                            stagingPallets   = stagingStocks.Count,
                            stagingQty       = stagingStocks.Sum(s => s.Qty),
                        },
                        outbound = new
                        {
                            pendingPicks,
                            completedPicksToday,
                            pendingDispatches,
                            dispatchedToday,
                        },
                        adjustments = new
                        {
                            pending = pendingAdjustments,
                        },
                        stockByZone,
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // GET /api/dashboard/activity
        [HttpGet("activity")]
        public async Task<IActionResult> GetActivity()
        {
            try
            {
                var movements = await _context.StockMovements
                    .Include(m => m.SKU)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(15)
                    .Select(m => new
                    {
                        id             = m.Id,
                        movementType   = m.MovementType,
                        skuCode        = m.SKU != null ? m.SKU.SKUCode : string.Empty,
                        skuName        = m.SKU != null ? m.SKU.SKUName : string.Empty,
                        qty            = m.Qty,
                        qtyBefore      = m.QtyBefore,
                        qtyAfter       = m.QtyAfter,
                        referenceNo    = m.ReferenceNo,
                        fromBin        = _context.BinLocations
                                            .Where(b => b.Id == m.FromRackId)
                                            .Select(b => b.BinCode)
                                            .FirstOrDefault() ?? string.Empty,
                        toBin          = _context.BinLocations
                                            .Where(b => b.Id == m.ToRackId)
                                            .Select(b => b.BinCode)
                                            .FirstOrDefault() ?? string.Empty,
                        remarks        = m.MovementRemarks,
                        createdAt      = m.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = movements });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
