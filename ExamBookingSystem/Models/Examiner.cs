using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamBookingSystem.Models
{
    [Table("Examiners")]
    public class Examiner
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("Name")]
        [Required]
        public string Name { get; set; } = string.Empty;

        [Column("Email")]
        [Required]
        public string Email { get; set; } = string.Empty;

        [Column("Phone")]
        public string? Phone { get; set; }

        [Column("Address")]
        [Required]
        public string Address { get; set; } = string.Empty;

        [Column("Website / Research")]
        public string? Website { get; set; }

        [Column("About")]
        public string? About { get; set; }

        [Column("Spoken With?")]
        public string? SpokenWith { get; set; }

        [Column("Qualifications")]
        public string? Qualification { get; set; }

        [Column("FSDO")]
        public string? FSDO { get; set; }

        [Column("Aircraft")]
        public string? Aircraft { get; set; }

        [Column("AdditionalInfo")]
        public string? AdditionalInfo { get; set; }

        [Column("Notes")]
        public string? Notes { get; set; }

        // Calculated properties для сумісності з існуючим кодом
        [NotMapped]
        public string FirstName
        {
            get
            {
                var parts = Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts?.FirstOrDefault() ?? "";
            }
        }

        [NotMapped]
        public string LastName
        {
            get
            {
                var parts = Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts != null && parts.Length > 1
                    ? string.Join(" ", parts.Skip(1))
                    : "";
            }
        }

        [NotMapped]
        public string PhoneNumber
        {
            get => Phone ?? string.Empty;
            set => Phone = value;
        }

        // Розпарсована кваліфікація в список спеціалізацій
        [NotMapped]
        public List<string> Specializations
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Qualification))
                    return new List<string>();

                var specializations = new List<string>();
                var qual = Qualification.ToUpper();

                // Парсимо на основі стандартних авіаційних сертифікацій
                if (qual.Contains("DPE-PE")) specializations.Add("Private");
                if (qual.Contains("DPE-CIRE")) specializations.Add("Instrument");
                if (qual.Contains("DPE-CE")) specializations.Add("Commercial");
                if (qual.Contains("DPE-FIE")) specializations.Add("CFI");
                if (qual.Contains("DPE-CFII")) specializations.Add("CFII");
                if (qual.Contains("DPE-MEI")) specializations.Add("MEI");
                if (qual.Contains("DPE-ATP")) specializations.Add("ATP");
                if (qual.Contains("DPE-PE-A") || qual.Contains("DPE-FIE-A")) specializations.Add("MultiEngine");

                return specializations.Distinct().ToList();
            }
        }

        // Географічні координати (будуть обчислюватися динамічно через геокодування)
        [NotMapped]
        public double? Latitude { get; set; }

        [NotMapped]
        public double? Longitude { get; set; }

        // Відстань від студента (обчислюється динамічно)
        [NotMapped]
        public double DistanceKm { get; set; }

        // Navigation properties тимчасово відключені для спрощення
        [NotMapped]
        public ICollection<ExaminerResponse> Responses { get; set; } = new List<ExaminerResponse>();

        // Helper методи
        public bool HasSpecialization(string examType)
        {
            return Specializations.Any(s => s.Equals(examType, StringComparison.OrdinalIgnoreCase));
        }

        public string GetDisplayName()
        {
            return !string.IsNullOrWhiteSpace(Name) ? Name : $"{FirstName} {LastName}".Trim();
        }

        public bool HasValidContactInfo()
        {
            return !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Address);
        }
    }
}