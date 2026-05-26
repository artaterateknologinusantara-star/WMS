using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("DispatchDetail")]
    public class DispatchDetail
    {
        public int Id { get; set; }

        public int DispatchHeaderId { get; set; }

        [ForeignKey("DispatchHeaderId")]
        public DispatchHeader? Header { get; set; }

        public int PickingDetailId { get; set; }

        [ForeignKey("PickingDetailId")]
        public PickingDetail? PickingDetail { get; set; }

        public int SKUId { get; set; }

        [ForeignKey("SKUId")]
        public MasterSKU? SKU { get; set; }

        public int Qty { get; set; }

        // Snapshot of location at dispatch creation time
        public string StagingBinCode { get; set; } = string.Empty;

        public string PalletId { get; set; } = string.Empty;

        // pending | dispatched
        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
