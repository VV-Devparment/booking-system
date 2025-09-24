using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace ExamBookingSystem.Services
{
    public interface ISmsService
    {
        Task<bool> SendSmsAsync(string to, string message);
    }

    public class SmsService : ISmsService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmsService> _logger;
        private readonly bool _isDemoMode;

        public SmsService(IConfiguration configuration, ILogger<SmsService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            var accountSid = _configuration["Twilio:AccountSid"];
            var authToken = _configuration["Twilio:AuthToken"];

            
            _isDemoMode = false;

            TwilioClient.Init(accountSid, authToken);
            _logger.LogInformation("📱 SMS Service initialized with Twilio");
        }

        public async Task<bool> SendSmsAsync(string to, string message)
        {
            if (_isDemoMode)
            {
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("📱 SMS SIMULATION (Demo Mode)");
                _logger.LogInformation($"📞 To: {to}");
                _logger.LogInformation($"💬 Message: {message}");
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("✅ SMS sent successfully! (simulated)");
                
                return await Task.FromResult(true);
            }

            try
            {
                var fromNumber = _configuration["Twilio:FromNumber"];

                if (string.IsNullOrEmpty(fromNumber))
                {
                    _logger.LogWarning("SMS FROM number not configured, treating as demo mode");
                    return true;
                }

                var sms = await MessageResource.CreateAsync(
                    body: message,
                    from: new Twilio.Types.PhoneNumber(fromNumber),
                    to: new Twilio.Types.PhoneNumber(to)
                );

                _logger.LogInformation($"✅ SMS sent successfully: {sms.Sid}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send SMS");
                return false;
            }
        }
    }
}