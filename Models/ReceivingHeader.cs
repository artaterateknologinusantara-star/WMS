using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("ReceivingHeader")]
    public class ReceivingHeader
    {
        public int Id { get; set; }

        public string ReceivingNumber { get; set; } = string.Empty;

        public int? SupplierId { get; set; }

        public string? SupplierName { get; set; }

        public string? DriverName { get; set; }

        public string? VehicleNumber { get; set; }

        public string? PONumber { get; set; }

        public string? WarehouseLocation { get; set; }

        public string? ReferenceNumber { get; set; }

        public string? Notes { get; set; }

        public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;

        public int? ReceivedBy { get; set; }

        public string Status { get; set; } = "Draft";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ReceivingDetail> Details { get; set; } = new List<ReceivingDetail>();
    }
}
