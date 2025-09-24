namespace ExamBookingSystem.Services
{
    public interface IEmailService
    {
        // Базовий метод для відправки email
        Task<bool> SendEmailAsync(string to, string subject, string body, string? fromName = null);

        // Новий метод для email з прикріпленням
        Task<bool> SendEmailWithAttachmentAsync(string to, string subject, string htmlContent, string attachmentContent, string attachmentFilename, string contentType, string fromName = "Exam Booking System");

        // Метод для підтвердження студента
        Task<bool> SendStudentConfirmationEmailAsync(string studentEmail, string studentName, string examinerName, DateTime scheduledDate);

        // Метод для контакту з екзаменатором
        Task<bool> SendExaminerContactEmailAsync(string examinerEmail, string examinerName, string studentName, string examType, DateTime preferredDate);
    }
}