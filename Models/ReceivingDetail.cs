using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("ReceivingDetail")]
    public class ReceivingDetail
    {
        public int Id { get; set; }

        public int ReceivingHeaderId { get; set; }

        [ForeignKey("ReceivingHeaderId")]
        public ReceivingHeader? Header { get; set; }

        public int SKUId { get; set; }

        [ForeignKey("SKUId")]
        public MasterSKU? SKU { get; set; }

        public int Qty { get; set; }

        public int? UOMId { get; set; }

        [ForeignKey("UOMId")]
        public UOM? UOM { get; set; }

        public string? PalletId { get; set; }
    }
}