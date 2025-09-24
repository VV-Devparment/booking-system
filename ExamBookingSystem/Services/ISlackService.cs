namespace ExamBookingSystem.Services
{
    public interface ISlackService
    {
        Task<bool> SendNotificationAsync(string message, string? channel = null);
        Task<bool> NotifyNewBookingAsync(string studentName, string examType, DateTime preferredDate);
        Task<bool> NotifyExaminerResponseAsync(string examinerName, string response, string studentName);
        Task<bool> NotifyErrorAsync(string error, string? details = null);
    }
}