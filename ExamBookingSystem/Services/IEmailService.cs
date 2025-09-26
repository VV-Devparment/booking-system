namespace ExamBookingSystem.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body, string? fromName = null);

        Task<bool> SendEmailWithAttachmentAsync(
            string to,
            string subject,
            string htmlContent,
            string attachmentContent,
            string attachmentFilename,
            string contentType,
            string fromName = "Exam Booking System");

        Task<bool> SendStudentConfirmationEmailAsync(
            string studentEmail,
            string studentName,
            string examinerName,
            DateTime scheduledDate,
            string? examinerEmail = null,
            string? examinerPhone = null,
            string? venueDetails = null,
            string? examinerMessage = null,
            decimal? price = null);

        Task<bool> SendExaminerContactEmailAsync(
            string examinerEmail,
            string examinerName,
            string studentName,
            string examType,
            DateTime preferredDate,
            DateTime? endDate = null,
            string? ftnNumber = null,
            string? additionalNotes = null,
            bool willingToTravel = false);
    }
}