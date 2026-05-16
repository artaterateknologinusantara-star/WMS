using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    public class ReceivingService
    {
        private readonly ApplicationDbContext _context;

        public ReceivingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<ReceivingHistoryDto>> GetReceivingHistoryAsync()
        {
            var headers = await _context.ReceivingHeaders
                .Include(h => h.Details)
                    .ThenInclude(d => d.SKU)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return headers.Select(h => new ReceivingHistoryDto
            {
                Id = h.Id,
                ReceivingNumber = h.ReceivingNumber,
                SupplierName = h.SupplierName ?? string.Empty,
                PONumber = h.PONumber ?? string.Empty,
                DriverName = h.DriverName ?? string.Empty,
                VehicleNumber = h.VehicleNumber ?? string.Empty,
                WarehouseLocation = h.WarehouseLocation ?? string.Empty,
                ReferenceNumber = h.ReferenceNumber ?? string.Empty,
                ReceivedBy = h.ReceivedBy,
                ReceivedDate = h.ReceivedDate,
                Status = h.Status,
                CreatedAt = h.CreatedAt,
                Pallets = h.Details.Select(d => d.PalletId).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList()!,
                SKUs = h.Details.Select(d => d.SKU?.SKUCode ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList(),
                TotalQty = h.Details.Sum(d => d.Qty)
            }).ToList();
        }

        /// <summary>
        /// RECEIVING (DRAFT ONLY) - Auto-palletization with max 50 qty per pallet
        /// NO STOCK UPDATES - This is for palletization only
        /// Stock updates happen ONLY in PUTAWAY via PutawayService
        /// </summary>
        public async Task<ReceivingResult> SubmitReceivingAsync(ReceivingSubmitRequest request)
        {
            var receivingNumber = $"RCV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";
            var pallets = new List<PalletInfo>();

            var header = new ReceivingHeader
            {
                ReceivingNumber = receivingNumber,
                SupplierName = request.SupplierName,
                DriverName = request.DriverName,
                VehicleNumber = request.VehicleNumber,
                PONumber = request.PONumber,
                WarehouseLocation = request.WarehouseLocation,
                ReferenceNumber = request.ReferenceNumber,
                Notes = request.Notes,
                ReceivedDate = DateTime.UtcNow,
                ReceivedBy = await GetValidUserIdAsync(request.ReceivedBy),
                Status = "Draft",
                CreatedAt = DateTime.UtcNow,
            };

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.ReceivingHeaders.Add(header);
                await _context.SaveChangesAsync();

                foreach (var detail in request.Details)
                {
                    var sku = detail.SKUId > 0
                        ? await _context.MasterSKUs.FindAsync(detail.SKUId)
                        : await _context.MasterSKUs.FirstOrDefaultAsync(s => s.SKUCode == detail.SKUCode);
                    if (sku == null) continue;

                    var remainingQty = detail.Qty;
                    while (remainingQty > 0)
                    {
                        var qtyForPallet = Math.Min(remainingQty, 50);
                        var palletId = $"PLT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";

                        var receivingDetail = new ReceivingDetail
                        {
                            ReceivingHeaderId = header.Id,
                            SKUId = sku.Id,
                            Qty = qtyForPallet,
                            UOMId = detail.UOMId,
                            PalletId = palletId
                        };

                        _context.ReceivingDetails.Add(receivingDetail);

                        // NOTE: DO NOT update InventoryStock here!
                        // Stock update only happens in PUTAWAY (PutawayService)
                        // This transaction is DRAFT ONLY for palletization

                        pallets.Add(new PalletInfo
                        {
                            PalletId = palletId,
                            SkuNumber = sku.SKUCode ?? string.Empty,
                            Qty = qtyForPallet,
                            ItemName = sku.SKUName ?? string.Empty
                        });

                        remainingQty -= qtyForPallet;
                    }
                }

                if (pallets.Count == 0)
                    throw new InvalidOperationException(
                        "Tidak ada SKU yang valid. Pastikan SKU Number yang dimasukkan terdaftar di Master SKU.");

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }

            return new ReceivingResult
            {
                ReceivingNumber = receivingNumber,
                Pallets = pallets
            };
        }

        private static Task<int?> GetValidUserIdAsync(int? userId) =>
            Task.FromResult(userId is > 0 ? userId : (int?)null);

        public class ReceivingResult
        {
            public string ReceivingNumber { get; set; } = string.Empty;
            public List<PalletInfo> Pallets { get; set; } = new();
        }

        public class PalletInfo
        {
            public string PalletId { get; set; } = string.Empty;
            public string SkuNumber { get; set; } = string.Empty;
            public int Qty { get; set; }
            public string ItemName { get; set; } = string.Empty;
        }
    }

    public class ReceivingDetailRequest
    {
        public int SKUId { get; set; }
        public string? SKUCode { get; set; }
        public int Qty { get; set; }
        public int? UOMId { get; set; }
    }

    public class ReceivingSubmitRequest
    {
        public string SupplierName { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string VehicleNumber { get; set; } = string.Empty;
        public string PONumber { get; set; } = string.Empty;
        public string WarehouseLocation { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int? ReceivedBy { get; set; }
        public List<ReceivingDetailRequest> Details { get; set; } = new();
    }

    public class ReceivingHistoryDto
    {
        public int Id { get; set; }
        public string ReceivingNumber { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string PONumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string VehicleNumber { get; set; } = string.Empty;
        public string WarehouseLocation { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public int? ReceivedBy { get; set; }
        public DateTime ReceivedDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<string> Pallets { get; set; } = new();
        public List<string> SKUs { get; set; } = new();
        public int TotalQty { get; set; }
    }
}