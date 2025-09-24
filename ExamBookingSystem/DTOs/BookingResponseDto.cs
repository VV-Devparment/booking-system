namespace ExamBookingSystem.DTOs
{
    public class BookingResponseDto
    {
        public string BookingId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string> ExaminersContacted { get; set; } = new();
        public string Status { get; set; } = string.Empty;
    }
}