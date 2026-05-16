using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("Categories")]
    public class Category
    {
        public int Id { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryName { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
