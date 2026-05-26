using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("PickingDetail")]
    public class PickingDetail
    {
        public int Id { get; set; }

        public int PickingHeaderId { get; set; }

        [ForeignKey("PickingHeaderId")]
        public PickingHeader? Header { get; set; }

        public int SKUId { get; set; }

        [ForeignKey("SKUId")]
        public MasterSKU? SKU { get; set; }

        public int InventoryStockId { get; set; }

        [ForeignKey("InventoryStockId")]
        public InventoryStock? InventoryStock { get; set; }

        public int RequestedQty { get; set; }

        public int PickedQty { get; set; }

        // Stored at creation time so picking process can validate even after stock moves to staging
        public int? SuggestedRackId { get; set; }

        [ForeignKey("SuggestedRackId")]
        public BinLocation? SuggestedRack { get; set; }

        public string? SuggestedPalletId { get; set; }

        // pending | in-progress | picked | error
        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
