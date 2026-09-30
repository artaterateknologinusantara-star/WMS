using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    /// <summary>
    /// PUTAWAY SERVICE — sole source of InventoryStock creation.
    /// Business rules:
    ///   - Scan palletId → scan BinCode → confirm
    ///   - Creates InventoryStock record (first stock activation)
    ///   - Inserts StockMovement for full audit trail
    ///   - Max 50 qty per pallet enforced
    /// </summary>
    public class PutawayService(ApplicationDbContext context)
    {
        private readonly ApplicationDbContext _context = context;

        /// <summary>
        /// Scan and confirm putaway for a pallet.
        /// THE ONLY operation that creates InventoryStock records.
        /// </summary>
        public async Task<PutawayResult> ConfirmPutawayAsync(PutawayRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PalletId))
                throw new ArgumentException("PalletId is required");

            if (string.IsNullOrWhiteSpace(request.BinCode))
                throw new ArgumentException("BinCode is required");

            // Resolve pallet from receiving details
            var receivingDetail = await _context.ReceivingDetails
                .Include(x => x.SKU)
                .FirstOrDefaultAsync(x => x.PalletId == request.PalletId);

            if (receivingDetail == null)
                throw new InvalidOperationException($"Pallet {request.PalletId} not found in receiving records.");

            // QC gate — putaway is blocked until this pallet's ReceivingDetail has passed QC.
            // Enforced here (not just filtered out of GET /pending) so the gate holds even if
            // this endpoint is called directly, e.g. via a barcode scan that skips the task list.
            if (receivingDetail.QCStatus != "Passed")
                throw new InvalidOperationException(
                    $"Cannot putaway: QC check belum lolos untuk pallet {request.PalletId}, status saat ini: {receivingDetail.QCStatus}.");

            // Resolve bin location by code
            var binLocation = await _context.BinLocations
                .FirstOrDefaultAsync(x => x.BinCode == request.BinCode);

            if (binLocation == null)
                throw new InvalidOperationException($"Bin location '{request.BinCode}' not found. Please verify the bin barcode.");

            if (receivingDetail.Qty > 50)
                throw new InvalidOperationException($"Pallet quantity {receivingDetail.Qty} exceeds the maximum of 50 units per pallet.");

            // Guard: pallet already put away
            var alreadyExists = await _context.InventoryStocks
                .AnyAsync(x => x.PalletId == request.PalletId);

            if (alreadyExists)
                throw new InvalidOperationException($"Pallet {request.PalletId} has already been put away.");

            // Guard: bin already occupied by another pallet
            var binOccupied = await _context.InventoryStocks
                .AnyAsync(x => x.RackId == binLocation.Id && x.Status == "Active");

            if (binOccupied)
                throw new InvalidOperationException($"Bin '{request.BinCode}' already has an active pallet. Each bin can only hold one pallet at a time.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var stock = new InventoryStock
                {
                    SKUId = receivingDetail.SKUId,
                    PalletId = request.PalletId,
                    RackId = binLocation.Id,
                    Qty = receivingDetail.Qty,
                    ReservedQty = 0,
                    AvailableQty = receivingDetail.Qty,
                    Status = "Active",
                    BatchNumber = receivingDetail.BatchNumber,
                    ExpiredDate = receivingDetail.ExpiredDate,
                    LastMovementDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.InventoryStocks.Add(stock);
                await _context.SaveChangesAsync();

                _context.StockMovements.Add(new StockMovement
                {
                    SKUId = receivingDetail.SKUId,
                    MovementType = "Putaway",
                    Qty = receivingDetail.Qty,
                    ReferenceNo = request.PalletId,
                    QtyBefore = 0,
                    QtyAfter = stock.Qty,
                    FromRackId = null,
                    ToRackId = binLocation.Id,
                    MovementRemarks = $"Putaway: Pallet {request.PalletId} → Bin {binLocation.BinCode}",
                    MovementReferenceType = "Putaway",
                    CreatedBy = request.ConfirmedBy,
                    CreatedAt = DateTime.UtcNow
                });

                // Flip ReceivingHeader → "Putaway" once every detail has a matching InventoryStock.
                // Single correlated query avoids materialising the pallet-ID list.
                var stillPending = await _context.ReceivingDetails
                    .Where(d => d.ReceivingHeaderId == receivingDetail.ReceivingHeaderId &&
                                !_context.InventoryStocks.Any(s => s.PalletId == d.PalletId))
                    .AnyAsync();

                if (!stillPending)
                {
                    var header = await _context.ReceivingHeaders.FindAsync(receivingDetail.ReceivingHeaderId);
                    if (header != null && header.Status != "Putaway")
                        header.Status = "Putaway";
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new PutawayResult
                {
                    Success = true,
                    PalletId = request.PalletId,
                    SKUId = receivingDetail.SKUId,
                    SKUCode = receivingDetail.SKU?.SKUCode ?? string.Empty,
                    Qty = receivingDetail.Qty,
                    BinLocationId = binLocation.Id,
                    BinCode = binLocation.BinCode,
                    Message = $"Putaway confirmed: {receivingDetail.Qty} units of {receivingDetail.SKU?.SKUCode} → bin {binLocation.BinCode}"
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Pending putaway tasks = ReceivingDetails that have no corresponding InventoryStock yet.
        /// </summary>
        public async Task<List<PutawayTaskDto>> GetPendingPutawayTasksAsync(int? skuId = null)
        {
            var query = _context.ReceivingDetails
                .Include(x => x.SKU)
                .Include(x => x.Header)
                .Where(x => !_context.InventoryStocks.Any(s => s.PalletId == x.PalletId));

            if (skuId.HasValue)
                query = query.Where(x => x.SKUId == skuId.Value);

            return await query
                .Select(x => new PutawayTaskDto
                {
                    PalletId = x.PalletId ?? string.Empty,
                    SKUId = x.SKUId,
                    SKUCode = x.SKU!.SKUCode ?? string.Empty,
                    SKUName = x.SKU!.SKUName ?? string.Empty,
                    Qty = x.Qty,
                    ReceivingNumber = x.Header != null ? x.Header.ReceivingNumber : string.Empty,
                    SupplierName = x.Header != null ? x.Header.SupplierName ?? string.Empty : string.Empty,
                    Status = "Pending",
                    CreatedAt = x.Header != null ? x.Header.CreatedAt : DateTime.UtcNow,
                    QCStatus = x.QCStatus,
                    QCRemarks = x.QCRemarks
                })
                .ToListAsync();
        }

        /// <summary>
        /// Submit a QC check result for a pallet's ReceivingDetail — the gate that must be
        /// "Passed" before ConfirmPutawayAsync will allow the pallet into a bin.
        /// Same actor as putaway (InboundAccess) performs this — no separate QC Inspector role.
        /// </summary>
        public async Task<PutawayTaskDto> SubmitQCCheckAsync(QCCheckRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PalletId))
                throw new ArgumentException("PalletId is required");

            if (request.Result != "Passed" && request.Result != "Failed")
                throw new ArgumentException("Result must be 'Passed' or 'Failed'.");

            var receivingDetail = await _context.ReceivingDetails
                .Include(x => x.SKU)
                .Include(x => x.Header)
                .FirstOrDefaultAsync(x => x.PalletId == request.PalletId)
                ?? throw new InvalidOperationException($"Pallet {request.PalletId} not found in receiving records.");

            receivingDetail.QCStatus = request.Result;
            receivingDetail.QCCheckedBy = request.CheckedBy;
            receivingDetail.QCCheckedAt = DateTime.UtcNow;
            receivingDetail.QCRemarks = request.Remarks;

            await _context.SaveChangesAsync();

            return new PutawayTaskDto
            {
                PalletId = receivingDetail.PalletId ?? string.Empty,
                SKUId = receivingDetail.SKUId,
                SKUCode = receivingDetail.SKU?.SKUCode ?? string.Empty,
                SKUName = receivingDetail.SKU?.SKUName ?? string.Empty,
                Qty = receivingDetail.Qty,
                ReceivingNumber = receivingDetail.Header?.ReceivingNumber ?? string.Empty,
                SupplierName = receivingDetail.Header?.SupplierName ?? string.Empty,
                Status = "Pending",
                CreatedAt = receivingDetail.Header?.CreatedAt ?? DateTime.UtcNow,
                QCStatus = receivingDetail.QCStatus,
                QCRemarks = receivingDetail.QCRemarks
            };
        }

        /// <summary>
        /// Current stock position for a pallet (post-putaway lookup).
        /// </summary>
        public async Task<StockPositionDto?> GetStockByPalletAsync(string palletId)
        {
            var stock = await _context.InventoryStocks
                .Include(x => x.SKU)
                .Include(x => x.Rack)
                .FirstOrDefaultAsync(x => x.PalletId == palletId);

            if (stock == null) return null;

            return new StockPositionDto
            {
                PalletId = palletId,
                SKUId = stock.SKUId,
                SKUCode = stock.SKU?.SKUCode ?? string.Empty,
                SKUName = stock.SKU?.SKUName ?? string.Empty,
                Qty = stock.Qty,
                AvailableQty = stock.AvailableQty,
                ReservedQty = stock.ReservedQty,
                BinCode = stock.Rack?.BinCode ?? string.Empty,
                Status = stock.Status ?? "Unknown",
                BatchNumber = stock.BatchNumber,
                ExpiredDate = stock.ExpiredDate
            };
        }
    }

    // ============ DTOs ============

    public class PutawayRequest
    {
        public string PalletId { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
        public int? ConfirmedBy { get; set; }
    }

    public class QCCheckRequest
    {
        public string PalletId { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty; // "Passed" | "Failed"
        public string? Remarks { get; set; }
        public int? CheckedBy { get; set; }
    }

    public class PutawayResult
    {
        public bool Success { get; set; }
        public string PalletId { get; set; } = string.Empty;
        public int SKUId { get; set; }
        public string SKUCode { get; set; } = string.Empty;
        public int Qty { get; set; }
        public int BinLocationId { get; set; }
        public string BinCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class PutawayTaskDto
    {
        public string PalletId { get; set; } = string.Empty;
        public int SKUId { get; set; }
        public string SKUCode { get; set; } = string.Empty;
        public string SKUName { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string ReceivingNumber { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public string QCStatus { get; set; } = "Pending";
        public string? QCRemarks { get; set; }
    }

    public class StockPositionDto
    {
        public string PalletId { get; set; } = string.Empty;
        public int SKUId { get; set; }
        public string SKUCode { get; set; } = string.Empty;
        public string SKUName { get; set; } = string.Empty;
        public int Qty { get; set; }
        public int AvailableQty { get; set; }
        public int ReservedQty { get; set; }
        public string BinCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? BatchNumber { get; set; }
        public DateTime? ExpiredDate { get; set; }
    }
}
