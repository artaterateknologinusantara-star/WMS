using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;
using Syntera.WMS.API.Models.Dtos;

namespace Syntera.WMS.API.Services
{
    public class InventoryService
    {
        private readonly ApplicationDbContext _context;

        public InventoryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<InventoryLookupDto?> GetByCodeAsync(string skuCode)
        {
            if (string.IsNullOrWhiteSpace(skuCode))
            {
                return null;
            }

            var normalizedCode = skuCode.Trim();

            var sku = await _context.MasterSKUs
                .Include(x => x.UOM)
                .Include(x => x.BinLocation)
                .FirstOrDefaultAsync(x => x.SKUCode == normalizedCode);

            if (sku == null)
            {
                return null;
            }

            // Aggregate from InventoryStock if exists, else fallback to MasterSKU
            var stockRecords = await _context.InventoryStocks
                .Include(x => x.Rack)
                .Where(x => x.SKUId == sku.Id)
                .ToListAsync();

            int totalQty = stockRecords.Any() ? stockRecords.Sum(x => x.Qty) : sku.Qty;
            int availableQty = stockRecords.Any() ? stockRecords.Sum(x => x.AvailableQty) : sku.Qty;
            string palletId = stockRecords.FirstOrDefault()?.PalletId ?? sku.PalletId ?? string.Empty;
            string binLocation = stockRecords.FirstOrDefault()?.Rack?.BinCode ?? sku.BinLocation?.BinCode ?? string.Empty;

            return new InventoryLookupDto
            {
                SKUId = sku.Id,
                SKUCode = sku.SKUCode ?? string.Empty,
                SKUName = sku.SKUName ?? string.Empty,
                Qty = totalQty,
                BinLocation = binLocation,
                PalletId = palletId,
                UOM = sku.UOM?.UOMName ?? string.Empty,
                Status = sku.Status ?? string.Empty,
                LastMovementDate = sku.LastMovement.HasValue ? sku.LastMovement.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty
            };
        }

        public async Task<List<InventoryLookupDto>> GetAllAsync()
        {
            var stocks = await _context.InventoryStocks
                .Include(x => x.SKU)
                    .ThenInclude(s => s!.UOM)
                .Include(x => x.Rack)
                .Where(x => x.Qty > 0)
                .OrderByDescending(x => x.LastMovementDate)
                .ToListAsync();

            return stocks.Select(x => new InventoryLookupDto
            {
                StockId = x.Id,
                SKUId = x.SKUId,
                SKUCode = x.SKU?.SKUCode ?? string.Empty,
                SKUName = x.SKU?.SKUName ?? string.Empty,
                Qty = x.Qty,
                BinLocation = x.Rack?.BinCode ?? string.Empty,
                PalletId = x.PalletId ?? string.Empty,
                UOM = x.SKU?.UOM?.UOMName ?? string.Empty,
                Status = x.Status ?? "Active",
                LastMovementDate = x.LastMovementDate.HasValue
                    ? x.LastMovementDate.Value.ToString("yyyy-MM-dd HH:mm")
                    : string.Empty
            }).ToList();
        }

    }
}
