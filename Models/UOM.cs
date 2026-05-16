using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("UOM")]
    public class UOM
    {
        public int Id { get; set; }
        public string? UOMCode { get; set; }
        public string? UOMName { get; set; }
    }
}
