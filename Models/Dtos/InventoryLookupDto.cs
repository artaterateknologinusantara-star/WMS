namespace Syntera.WMS.API.Models.Dtos
{
    public class InventoryLookupDto
    {
        public int StockId { get; set; }

        public int SKUId { get; set; }

        public string SKUCode { get; set; } = string.Empty;

        public string SKUName { get; set; } = string.Empty;

        public int Qty { get; set; }

        public string BinLocation { get; set; } = string.Empty;

        public string PalletId { get; set; } = string.Empty;

        public string UOM { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string LastMovementDate { get; set; } = string.Empty;
    }
}
