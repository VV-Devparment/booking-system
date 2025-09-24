using System.ComponentModel.DataAnnotations;

namespace ExamBookingSystem.Models
{
    public enum BookingStatus
    {
        Created,
        PaymentPending,
        PaymentConfirmed,
        ExaminersContacted,
        ExaminerAssigned,
        Scheduled,
        Completed,
        Cancelled,
        RefundRequested,
        Refunded
    }

    public class BookingRequest
    {
        public int Id { get; set; }

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

        [Required]
        public string StudentAddress { get; set; } = string.Empty;

        // Geolocation для пошуку екзаменаторів
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [Required]
        public string ExamType { get; set; } = string.Empty;

        public DateTime PreferredDate { get; set; }
        public string? PreferredTime { get; set; }

        public string? SpecialRequirements { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Created;

        // Payment інформація для майбутньої інтеграції Stripe
        public string? PaymentIntentId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public bool IsPaid { get; set; } = false;

        // Призначений екзаменатор
        public int? AssignedExaminerId { get; set; }
        public Examiner? AssignedExaminer { get; set; }

        // Фінальні деталі іспиту
        public DateTime? ScheduledDate { get; set; }
        public string? ScheduledTime { get; set; }
        public string? MeetingLocation { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<ExaminerResponse> ExaminerResponses { get; set; } = new List<ExaminerResponse>();
        public ICollection<ActionLog> ActionLogs { get; set; } = new List<ActionLog>();
    }
}