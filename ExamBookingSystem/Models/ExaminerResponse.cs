namespace ExamBookingSystem.Models
{
    public enum ResponseType
    {
        Accepted,
        Declined,
        NoResponse
    }

    public class ExaminerResponse
    {
        public int Id { get; set; }

        public int BookingRequestId { get; set; }
        public BookingRequest BookingRequest { get; set; } = null!;

        public int ExaminerId { get; set; }
        public Examiner Examiner { get; set; } = null!;

        public ResponseType Response { get; set; } = ResponseType.NoResponse;

        public DateTime ContactedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }

        public string? ResponseMessage { get; set; }

        // Додаткова інформація від екзаменатора
        public DateTime? ProposedDate { get; set; }
        public string? ProposedTime { get; set; }
        public string? ProposedLocation { get; set; }

        public bool IsWinner { get; set; } = false; // "перший ТАК виграє"
    }
}