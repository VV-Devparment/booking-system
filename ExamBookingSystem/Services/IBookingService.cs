using ExamBookingSystem.DTOs;

namespace ExamBookingSystem.Services
{
    public enum BookingStatus
    {
        Created,
        ExaminersContacted,
        ExaminerAssigned,
        Scheduled,
        Completed,
        Cancelled
    }

    public class BookingInfo
    {
        public string BookingId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentEmail { get; set; } = string.Empty;
        public string ExamType { get; set; } = string.Empty;
        public DateTime PreferredDate { get; set; }
        public BookingStatus Status { get; set; }
        public string? AssignedExaminerEmail { get; set; }
        public string? AssignedExaminerName { get; set; }
        public DateTime? ScheduledDateTime { get; set; }
        public List<string> ContactedExaminers { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsPaid { get; set; } = false;
    }

    public interface IBookingService
    {
        Task<string> CreateBookingAsync(CreateBookingDto request);
        Task<BookingInfo?> GetBookingAsync(string bookingId);
        Task<bool> TryAssignExaminerAsync(string bookingId, string examinerEmail, string examinerName);
        Task<bool> IsBookingAvailableAsync(string bookingId);
        Task<List<BookingInfo>> GetActiveBookingsAsync();
        Task<bool> CancelBookingAsync(string bookingId, string reason);
        Task<bool> UpdateBookingStatusAsync(string bookingId, BookingStatus status); // Новий метод
    }
}