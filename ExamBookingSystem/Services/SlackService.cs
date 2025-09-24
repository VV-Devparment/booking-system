using Newtonsoft.Json;

namespace ExamBookingSystem.Services
{
    public class SlackService : ISlackService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SlackService> _logger;
        private readonly string? _webhookUrl;
        private readonly bool _isDemoMode;

        public SlackService(HttpClient httpClient, IConfiguration configuration, ILogger<SlackService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _webhookUrl = _configuration["Slack:WebhookUrl"];

            // Видаліть demo mode логіку
            _isDemoMode = false;
        }

        public async Task<bool> SendNotificationAsync(string message, string? channel = null)
        {
            if (_isDemoMode)
            {
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("💬 SLACK NOTIFICATION SIMULATION (Demo Mode)");
                _logger.LogInformation($"📢 Channel: {channel ?? "#exam-bookings"}");
                _logger.LogInformation($"💬 Message: {message}");
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("✅ Slack notification sent successfully! (simulated)");
                return await Task.FromResult(true);
            }

            if (string.IsNullOrEmpty(_webhookUrl))
            {
                _logger.LogWarning("Slack webhook URL not configured");
                return false;
            }

            try
            {
                var payload = new
                {
                    text = message,
                    channel = channel ?? "#exam-bookings",
                    username = "Exam Booking Bot",
                    icon_emoji = ":calendar:"
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_webhookUrl, content);

                _logger.LogInformation($"Slack notification sent, Status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Slack notification");
                return false;
            }
        }

        public async Task<bool> NotifyNewBookingAsync(string studentName, string examType, DateTime preferredDate)
        {
            if (_isDemoMode)
            {
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("💬 SLACK NEW BOOKING NOTIFICATION SIMULATION (Demo Mode)");
                _logger.LogInformation($"🆕 NEW BOOKING ALERT");
                _logger.LogInformation($"👤 Student: {studentName}");
                _logger.LogInformation($"📚 Exam Type: {examType}");
                _logger.LogInformation($"📅 Preferred Date: {preferredDate:yyyy-MM-dd}");
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("✅ Slack notification sent successfully! (simulated)");
                return await Task.FromResult(true);
            }

            var message = $"🆕 *New Exam Booking*\n" +
                         $"Student: {studentName}\n" +
                         $"Exam Type: {examType}\n" +
                         $"Preferred Date: {preferredDate:dddd, MMMM dd, yyyy}";

            return await SendNotificationAsync(message);
        }

        public async Task<bool> NotifyExaminerResponseAsync(string examinerName, string response, string studentName)
        {
            if (_isDemoMode)
            {
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("💬 SLACK EXAMINER RESPONSE NOTIFICATION SIMULATION (Demo Mode)");
                _logger.LogInformation($"👨‍🏫 EXAMINER RESPONSE");
                _logger.LogInformation($"Examiner: {examinerName}");
                _logger.LogInformation($"Response: {response}");
                _logger.LogInformation($"Student: {studentName}");
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("✅ Slack notification sent successfully! (simulated)");
                return await Task.FromResult(true);
            }

            var emoji = response.ToLower().Contains("accepted") ? "✅" : "❌";
            var message = $"{emoji} *Examiner Response*\n" +
                         $"Examiner: {examinerName}\n" +
                         $"Response: {response}\n" +
                         $"Student: {studentName}";

            return await SendNotificationAsync(message);
        }

        public async Task<bool> NotifyErrorAsync(string error, string? details = null)
        {
            if (_isDemoMode)
            {
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("💬 SLACK ERROR NOTIFICATION SIMULATION (Demo Mode)");
                _logger.LogInformation($"🚨 ERROR: {error}");
                if (!string.IsNullOrEmpty(details))
                {
                    _logger.LogInformation($"Details: {details}");
                }
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("✅ Slack error notification sent successfully! (simulated)");
                return await Task.FromResult(true);
            }

            var message = $"🚨 *System Error*\n{error}";
            if (!string.IsNullOrEmpty(details))
            {
                message += $"\nDetails: {details}";
            }

            return await SendNotificationAsync(message, "#alerts");
        }
    }
}