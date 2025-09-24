namespace ExamBookingSystem.Models
{
    public enum ActionType
    {
        BookingCreated,
        PaymentInitiated,
        PaymentConfirmed,
        ExaminersContacted,
        ExaminerResponded,
        ExaminerAssigned,
        EmailSent,
        SMSSent,
        SlackNotificationSent,
        ScheduleConfirmed,
        BookingCancelled,
        RefundProcessed,
        Error
    }

    public class ActionLog
    {
        public int Id { get; set; }

        public int? BookingRequestId { get; set; }
        public BookingRequest? BookingRequest { get; set; }

        public int? ExaminerId { get; set; }
        public Examiner? Examiner { get; set; }

        public ActionType ActionType { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? Details { get; set; } // JSON for extra data

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? UserId { get; set; } // Для майбутньої аутентифікації
        public string? IpAddress { get; set; }
    }
}