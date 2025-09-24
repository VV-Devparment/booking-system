using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamBookingSystem.Models
{
    [Table("Administrators")]
    public class Administrator
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        [Column("Email")]
        public string Email { get; set; } = string.Empty;

        [Column("Phone")]
        public string? Phone { get; set; }

        [Required]
        [Column("PasswordHash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("FullName")]
        public string FullName { get; set; } = string.Empty;

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("LastLoginAt")]
        public DateTime? LastLoginAt { get; set; }
    }
}