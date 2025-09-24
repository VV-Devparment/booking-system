using System.ComponentModel.DataAnnotations;

namespace ExamBookingSystem.DTOs
{
    public class CreateBookingDto
    {
        [Required]
        [StringLength(100)]
        public string StudentFirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string StudentLastName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string StudentEmail { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string StudentPhone { get; set; } = string.Empty;

        // Aircraft information
        [Required]
        public string AircraftType { get; set; } = "Cessna 172"; // Додайте Required атрибут

        [Required]
        public string CheckRideType { get; set; } = string.Empty;

        [Required]
        public string PreferredAirport { get; set; } = string.Empty;

        public int SearchRadius { get; set; } = 50;

        public bool WillingToFly { get; set; }

        // New availability window fields
        public string DateOption { get; set; } = "ASAP"; // "ASAP" or "DATE_RANGE"

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        // New additional fields
        public string? FtnNumber { get; set; }

        public string? ExamId { get; set; }

        public string? AdditionalNotes { get; set; }

        // Legacy fields for compatibility
        public string StudentAddress { get; set; } = string.Empty;

        public string ExamType { get; set; } = string.Empty;

        public DateTime PreferredDate { get; set; }

        public string? PreferredTime { get; set; }

        public string? SpecialRequirements { get; set; }

        // Additional optional fields
        public string? PreferredExaminer { get; set; }

        public bool AdditionalRating { get; set; }

        public bool IsRecheck { get; set; }

        // Fields for after examiner match
        public string? CertificateNumber { get; set; }

        public string? WrittenLast4 { get; set; }
    }
}