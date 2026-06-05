using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    /// <summary>
    /// INVENTORY ADJUSTMENT APPROVAL SERVICE
    /// 
    /// Business Rules:
    /// - Adjustments start as PENDING (RequestedBy)
    /// - Approval/Rejection by Supervisor/Admin
    /// - APPROVED: Update InventoryStock + insert StockMovement
    /// - REJECTED: Status only, no stock update
    /// - All changes audit-trailed in StockMovement
    /// </summary>
    public class AdjustmentApprovalService
    {
        private readonly ApplicationDbContext _context;

        public AdjustmentApprovalService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Approve an adjustment - updates InventoryStock and creates StockMovement
        /// </summary>
        public async Task<ApprovalResultDto> ApproveAdjustmentAsync(int adjustmentId, int approvedBy)
        {
            var adjustment = await _context.InventoryAdjustments
                .Include(x => x.SKU)
                .FirstOrDefaultAsync(x => x.Id == adjustmentId);

            if (adjustment == null)
                throw new InvalidOperationException($"Adjustment {adjustmentId} not found");

            if (adjustment.ApprovalStatus != "Pending")
                throw new InvalidOperationException($"Adjustment status is {adjustment.ApprovalStatus}, cannot approve");

            // Guard: block if any pallet of this SKU has stock reserved by an active picking task
            var totalReserved = await _context.InventoryStocks
                .Where(s => s.SKUId == adjustment.SKUId && s.ReservedQty > 0)
                .SumAsync(s => (int?)s.ReservedQty) ?? 0;

            if (totalReserved > 0)
                throw new InvalidOperationException(
                    $"Cannot approve adjustment for SKU '{adjustment.SKU?.SKUCode}': " +
                    $"{totalReserved} unit(s) are currently reserved in active picking tasks. " +
                    $"Complete or cancel the related picking tasks before approving this adjustment.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Calculate quantity change
                int qtyChange = adjustment.NewQty - adjustment.PrevQty;

                // Find active inventory stock: match by PalletId if provided, else first Active pallet for SKU
                InventoryStock? stock;
                if (!string.IsNullOrWhiteSpace(adjustment.PalletId))
                {
                    stock = await _context.InventoryStocks
                        .FirstOrDefaultAsync(x =>
                            x.SKUId == adjustment.SKUId &&
                            x.PalletId == adjustment.PalletId &&
                            x.Status == "Active");
                }
                else
                {
                    stock = await _context.InventoryStocks
                        .Where(x => x.SKUId == adjustment.SKUId && x.Status == "Active")
                        .OrderBy(x => x.CreatedAt)
                        .FirstOrDefaultAsync();
                }

                int qtyBefore;
                int? rackId = null;

                if (stock != null)
                {
                    qtyBefore = stock.Qty;
                    rackId = stock.RackId;

                    stock.Qty = adjustment.NewQty;
                    // Recalculate from new Qty rather than adding delta (avoids drift)
                    stock.AvailableQty = Math.Max(0, adjustment.NewQty - stock.ReservedQty);
                    stock.LastMovementDate = DateTime.UtcNow;
                    stock.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    var sku = adjustment.SKU
                        ?? await _context.MasterSKUs.FindAsync(adjustment.SKUId)
                        ?? throw new InvalidOperationException($"SKU {adjustment.SKUId} not found.");

                    qtyBefore = sku.Qty;
                    sku.Qty = adjustment.NewQty;
                    sku.LastMovement = DateTime.UtcNow;
                    sku.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                // Create stock movement record (CRITICAL for audit trail)
                var movement = new StockMovement
                {
                    SKUId = adjustment.SKUId,
                    MovementType = "Adjustment",
                    Qty = qtyChange,
                    ReferenceNo = adjustment.AdjustmentNo,
                    QtyBefore = qtyBefore,
                    QtyAfter = adjustment.NewQty,
                    FromRackId = rackId,
                    ToRackId = rackId,
                    MovementRemarks = $"Adjustment Approved: {adjustment.Reason}",
                    MovementReferenceType = "Adjustment",
                    CreatedBy = approvedBy,
                    CreatedAt = DateTime.UtcNow
                };

                _context.StockMovements.Add(movement);

                // Update adjustment record
                adjustment.ApprovalStatus = "Approved";
                adjustment.ApprovedBy = approvedBy;
                adjustment.ApprovedAt = DateTime.UtcNow;
                adjustment.IsProcessed = true;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApprovalResultDto
                {
                    Success = true,
                    AdjustmentNo = adjustment.AdjustmentNo,
                    SKUCode = adjustment.SKU?.SKUCode ?? string.Empty,
                    PrevQty = adjustment.PrevQty,
                    NewQty = adjustment.NewQty,
                    QtyChange = qtyChange,
                    ApprovalStatus = "Approved",
                    Message = $"Adjustment {adjustment.AdjustmentNo} approved. Stock updated from {qtyBefore} to {adjustment.NewQty}"
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Reject an adjustment - status only, no stock update
        /// </summary>
        public async Task<ApprovalResultDto> RejectAdjustmentAsync(int adjustmentId, int rejectedBy, string rejectionReason)
        {
            var adjustment = await _context.InventoryAdjustments
                .Include(x => x.SKU)
                .FirstOrDefaultAsync(x => x.Id == adjustmentId);

            if (adjustment == null)
                throw new InvalidOperationException($"Adjustment {adjustmentId} not found");

            if (adjustment.ApprovalStatus != "Pending")
                throw new InvalidOperationException($"Adjustment status is {adjustment.ApprovalStatus}, cannot reject");

            // Update adjustment record only (NO stock changes)
            adjustment.ApprovalStatus = "Rejected";
            adjustment.ApprovedBy = rejectedBy;
            adjustment.ApprovedAt = DateTime.UtcNow;
            adjustment.RejectedReason = rejectionReason;
            adjustment.IsProcessed = true;

            await _context.SaveChangesAsync();

            return new ApprovalResultDto
            {
                Success = true,
                AdjustmentNo = adjustment.AdjustmentNo,
                SKUCode = adjustment.SKU?.SKUCode ?? string.Empty,
                PrevQty = adjustment.PrevQty,
                NewQty = adjustment.NewQty,
                ApprovalStatus = "Rejected",
                Message = $"Adjustment {adjustment.AdjustmentNo} rejected. Reason: {rejectionReason}"
            };
        }

    }

    // ============ DTOs ============

    public class ApprovalResultDto
    {
        public bool Success { get; set; }
        public string AdjustmentNo { get; set; } = string.Empty;
        public string SKUCode { get; set; } = string.Empty;
        public int PrevQty { get; set; }
        public int NewQty { get; set; }
        public int QtyChange { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

}
