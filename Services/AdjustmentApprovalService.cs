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
        /// Get all pending adjustments for approval
        /// </summary>
        public async Task<List<AdjustmentForApprovalDto>> GetPendingAdjustmentsAsync(int? skuId = null)
        {
            var query = _context.InventoryAdjustments
                .Include(x => x.SKU)
                .Where(x => x.ApprovalStatus == "Pending" && !x.IsProcessed);

            if (skuId.HasValue)
                query = query.Where(x => x.SKUId == skuId.Value);

            var results = await query
                .OrderByDescending(x => x.RequestedAt)
                .Select(x => new AdjustmentForApprovalDto
                {
                    AdjustmentId = x.Id,
                    AdjustmentNo = x.AdjustmentNo,
                    SKUId = x.SKUId,
                    SKUCode = x.SKU!.SKUCode ?? string.Empty,
                    SKUName = x.SKU!.SKUName ?? string.Empty,
                    PalletId = x.PalletId,
                    PrevQty = x.PrevQty,
                    NewQty = x.NewQty,
                    AdjustmentType = x.AdjustmentType,
                    Reason = x.Reason,
                    Remarks = x.Remarks,
                    RequestedBy = x.RequestedBy,
                    RequestedAt = x.RequestedAt,
                    Status = x.ApprovalStatus
                })
                .ToListAsync();

            return results;
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

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Calculate quantity change
                int qtyChange = adjustment.NewQty - adjustment.PrevQty;

                // Find or create inventory stock for the pallet
                var stock = await _context.InventoryStocks
                    .FirstOrDefaultAsync(x => 
                        x.SKUId == adjustment.SKUId && 
                        x.PalletId == adjustment.PalletId);

                int qtyBefore;
                int? rackId = null;

                if (stock != null)
                {
                    // Normal path: pallet has been put away — update InventoryStock
                    qtyBefore = stock.Qty;
                    rackId = stock.RackId;

                    stock.Qty = adjustment.NewQty;
                    stock.AvailableQty = Math.Max(0, stock.AvailableQty + qtyChange);
                    stock.LastMovementDate = DateTime.UtcNow;
                    stock.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    // Fallback: no pallet-level stock exists — update MasterSKU directly
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

        /// <summary>
        /// Get adjustment history (approved/rejected)
        /// </summary>
        public async Task<List<AdjustmentHistoryDto>> GetAdjustmentHistoryAsync(int days = 30)
        {
            var fromDate = DateTime.UtcNow.AddDays(-days);

            var results = await _context.InventoryAdjustments
                .Include(x => x.SKU)
                .Where(x => x.IsProcessed && x.ApprovedAt >= fromDate)
                .OrderByDescending(x => x.ApprovedAt)
                .Select(x => new AdjustmentHistoryDto
                {
                    AdjustmentNo = x.AdjustmentNo,
                    SKUCode = x.SKU!.SKUCode ?? string.Empty,
                    PalletId = x.PalletId,
                    PrevQty = x.PrevQty,
                    NewQty = x.NewQty,
                    ApprovalStatus = x.ApprovalStatus,
                    Reason = x.Reason,
                    ApprovedAt = x.ApprovedAt
                })
                .ToListAsync();

            return results;
        }
    }

    // ============ DTOs ============

    public class AdjustmentForApprovalDto
    {
        public int AdjustmentId { get; set; }
        public string AdjustmentNo { get; set; } = string.Empty;
        public int SKUId { get; set; }
        public string SKUCode { get; set; } = string.Empty;
        public string SKUName { get; set; } = string.Empty;
        public string? PalletId { get; set; }
        public int PrevQty { get; set; }
        public int NewQty { get; set; }
        public string? AdjustmentType { get; set; }
        public string? Reason { get; set; }
        public string? Remarks { get; set; }
        public int? RequestedBy { get; set; }
        public DateTime? RequestedAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }

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

    public class AdjustmentHistoryDto
    {
        public string AdjustmentNo { get; set; } = string.Empty;
        public string SKUCode { get; set; } = string.Empty;
        public string? PalletId { get; set; }
        public int PrevQty { get; set; }
        public int NewQty { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }
}
