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

            // Resolve bin location by code
            var binLocation = await _context.BinLocations
                .FirstOrDefaultAsync(x => x.BinCode == request.BinCode);

            if (binLocation == null)
                throw new InvalidOperationException($"Bin location '{request.BinCode}' not found. Please verify the bin barcode.");

            if (receivingDetail.Qty > 50)
                throw new InvalidOperationException($"Pallet quantity {receivingDetail.Qty} exceeds the maximum of 50 units per pallet.");

            // Guard: pallet already put away
            var alreadyExists = await _context.InventoryStocks
                .AnyAsync(x => x.SKUId == receivingDetail.SKUId && x.PalletId == request.PalletId);

            if (alreadyExists)
                throw new InvalidOperationException($"Pallet {request.PalletId} has already been put away.");

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
                    CreatedAt = x.Header != null ? x.Header.CreatedAt : DateTime.UtcNow
                })
                .ToListAsync();
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
                Status = stock.Status ?? "Unknown"
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
    }
}
