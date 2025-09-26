using System.ComponentModel.DataAnnotations;

namespace ExamBookingSystem.DTOs
{
    public class ExaminerResponseDto
    {
        [Required]
        public string BookingId { get; set; } = string.Empty;

        [Required]
        public string ExaminerEmail { get; set; } = string.Empty;

        [Required]
        public string ExaminerName { get; set; } = string.Empty;

        [Required]
        public string Response { get; set; } = string.Empty; // "Accepted" or "Declined"

        [Required]
        public string StudentName { get; set; } = string.Empty;

        [Required]
        public string StudentEmail { get; set; } = string.Empty;

        public DateTime? ProposedDateTime { get; set; }
        public string? ResponseMessage { get; set; }

        // Нові поля для venue та ціни
        public string? VenueDetails { get; set; }
        public decimal? ExaminerPrice { get; set; }
        public string? ExaminerPhone { get; set; }
    }
}