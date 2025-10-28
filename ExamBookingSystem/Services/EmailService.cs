using SendGrid;
using SendGrid.Helpers.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace ExamBookingSystem.Services
{
    public class EmailService : IEmailService
    {
        private readonly ISendGridClient _sendGridClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly bool _isDemoMode;

        public EmailService(ISendGridClient sendGridClient, IConfiguration configuration, ILogger<EmailService> logger)
        {
            _sendGridClient = sendGridClient;
            _configuration = configuration;
            _logger = logger;

            var apiKey = _configuration["SendGrid:ApiKey"];

          
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body, string? fromName = null)
        {
            if (_isDemoMode)
            {
                _logger.LogInformation($"📧 DEMO: Email to {to} - {subject}");
                return await Task.FromResult(true);
            }

            try
            {
                // ЗМІНІТЬ From адрес на верифікований
                var from = new EmailAddress("contact@mom-ai-agency.site", fromName ?? "JUMPSEAT");
                var toEmail = new EmailAddress(to);
                var msg = MailHelper.CreateSingleEmail(from, toEmail, subject, body, body);

                var response = await _sendGridClient.SendEmailAsync(msg);

                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    _logger.LogInformation($"✅ Email sent successfully to {to}");
                    return true;
                }
                else
                {
                    var responseBody = await response.Body.ReadAsStringAsync();
                    _logger.LogError($"❌ Failed to send email. Status: {response.StatusCode}, Body: {responseBody}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error sending email to {to}");
                return false;
            }
        }

        public async Task<bool> SendEmailWithAttachmentAsync(
            string to,
            string subject,
            string htmlContent,
            string attachmentContent,
            string attachmentFilename,
            string contentType,
            string fromName = "JUMPSEAT")
        {
            if (_isDemoMode)
            {
                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("📧 EMAIL WITH ATTACHMENT SIMULATION (Demo Mode)");
                _logger.LogInformation($"📨 To: {to}");
                _logger.LogInformation($"📝 Subject: {subject}");
                _logger.LogInformation($"👤 From: {fromName}");
                _logger.LogInformation($"📎 Attachment: {attachmentFilename} ({contentType})");
                _logger.LogInformation($"📄 Attachment size: {attachmentContent.Length} characters");

                var preview = CleanHtmlForPreview(htmlContent);
                var bodyPreview = preview.Length > 200 ? preview.Substring(0, 200) + "..." : preview;
                _logger.LogInformation($"📄 Body Preview: {bodyPreview}");

                _logger.LogInformation("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                _logger.LogInformation("✅ Email with attachment sent successfully! (simulated)");
                return await Task.FromResult(true);
            }

            try
            {
                var from = new EmailAddress("contact@mom-ai-agency.site", fromName ?? "JUMPSEAT");
                var toAddress = new EmailAddress(to);

                var msg = MailHelper.CreateSingleEmail(from, toAddress, subject, null, htmlContent);

                // Add attachment
                var attachmentBytes = Encoding.UTF8.GetBytes(attachmentContent);
                var base64Content = Convert.ToBase64String(attachmentBytes);

                msg.AddAttachment(attachmentFilename, base64Content, contentType);

                var response = await _sendGridClient.SendEmailAsync(msg);

                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    _logger.LogInformation($"✅ Email with attachment sent successfully to {to}");
                    return true;
                }
                else
                {
                    _logger.LogError($"❌ Failed to send email with attachment. Status: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception sending email with attachment to {to}");
                return false;
            }
        }

        public async Task<bool> SendExaminerContactEmailAsync(
    string examinerEmail,
    string examinerName,
    string studentName,
    string examType,
    DateTime preferredDate,
    DateTime? endDate = null,
    string? ftnNumber = null,
    string? additionalNotes = null,
    bool willingToTravel = false)
        {
            var subject = $"🎓 New Exam Request - {examType}";

            var dateRange = endDate.HasValue && endDate.Value != preferredDate
                ? $"{preferredDate:MMM dd} - {endDate.Value:MMM dd, yyyy}"
                : $"{preferredDate:dddd, MMMM dd, yyyy}";

            var body = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
            <div style='background: linear-gradient(135deg, #5CADD3 0%, #2c3e50 100%); padding: 20px; color: white; border-radius: 10px 10px 0 0; text-align: center;'>
                <img src='https://yourdomain.com/jumpseat-logo.png' alt='JUMPSEAT' style='height: 50px; margin-bottom: 10px;'>
                <h2 style='margin: 0;'>New Exam Request</h2>
            </div>
            
            <div style='padding: 20px; background: #f8f9fa; border: 1px solid #dee2e6; border-top: none;'>
                <p>Hello <strong>{examinerName}</strong>,</p>
                
                <p>You have received a new exam request:</p>
                
                <div style='background: white; padding: 15px; border-radius: 8px; margin: 20px 0;'>
                    <table style='width: 100%;'>
                        <tr>
                            <td style='padding: 8px 0;'><strong>👤 Student:</strong></td>
                            <td>{studentName}</td>
                        </tr>
                        <tr>
                            <td style='padding: 8px 0;'><strong>📚 Exam Type:</strong></td>
                            <td>{examType}</td>
                        </tr>
                        <tr>
                            <td style='padding: 8px 0;'><strong>📅 Availability:</strong></td>
                            <td>{dateRange}</td>
                        </tr>
                        {(!string.IsNullOrEmpty(ftnNumber) ? $@"
                        <tr>
                            <td style='padding: 8px 0;'><strong>📋 FTN Number:</strong></td>
                            <td>{ftnNumber}</td>
                        </tr>" : "")}
                        <tr>
                            <td style='padding: 8px 0;'><strong>✈️ Willing to Travel:</strong></td>
                            <td>{(willingToTravel ? "Yes" : "No")}</td>
                        </tr>
                        {(!string.IsNullOrEmpty(additionalNotes) ? $@"
                        <tr>
                            <td style='padding: 8px 0;' valign='top'><strong>📝 Additional Notes:</strong></td>
                            <td>{additionalNotes}</td>
                        </tr>" : "")}
                    </table>
                </div>
                
                <div style='background: #d1ecf1; border: 1px solid #bee5eb; padding: 15px; border-radius: 8px; margin: 20px 0;'>
                    <p style='margin: 0; color: #0c5460;'>
                        <strong>⚡ IMPORTANT:</strong> First examiner to accept wins! Please respond quickly if you're available.
                    </p>
                </div>
                
                <p>Please use the examiner portal to respond with your availability, venue details, and pricing.</p>

                <div style='margin-top: 30px; text-align: center;'>
                    <a href='https://booking-system-xpbx.onrender.com/' style='display: inline-block; padding: 14px 35px; background: linear-gradient(135deg, #5CADD3 0%, #2c3e50 100%); color: white; text-decoration: none; border-radius: 8px; font-weight: bold; font-size: 16px;'>
                        → Go to JUMPSEAT
                    </a>
                </div>                

                <div style='margin-top: 30px; padding-top: 20px; border-top: 1px solid #dee2e6; color: #6c757d; font-size: 12px;'>
                    <p>This is an automated message from JUMPSEAT Team.<br>
                    Do not reply to this email.</p>
                </div>
            </div>
        </div>";

            _logger.LogInformation($"📤 Sending examiner contact email to {examinerName} ({examinerEmail})");
            var result = await SendEmailAsync(examinerEmail, subject, body, "JUMPSEAT Team");

            if (result)
            {
                _logger.LogInformation($"✅ Examiner contact email sent successfully");
            }
            else
            {
                _logger.LogWarning($"❌ Failed to send examiner contact email");
            }

            return result;
        }

        public async Task<bool> SendStudentConfirmationEmailAsync(
    string studentEmail,
    string studentName,
    string examinerName,
    DateTime scheduledDate,
    string? examinerEmail = null,
    string? examinerPhone = null,
    string? venueDetails = null,
    string? examinerMessage = null,
    decimal? price = null)
        {
            var subject = "✅ Exam Scheduled - Confirmation";

            var body = $@"
    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
        <div style='background: linear-gradient(135deg, #5CADD3 0%, #2c3e50 100%); padding: 20px; color: white; border-radius: 10px 10px 0 0; text-align: center;'>
            <img src='https://yourdomain.com/jumpseat-logo.png' alt='JUMPSEAT' style='height: 50px; margin-bottom: 10px;'>
            <h2 style='margin: 0;'>✅ Exam Confirmed!</h2>
        </div>
        
        <div style='padding: 20px; background: #f8f9fa; border: 1px solid #dee2e6; border-top: none;'>
            <div style='background: #fff3cd; border: 1px solid #ffc107; padding: 10px; border-radius: 5px; margin-bottom: 20px;'>
                <strong>⚠️ IMPORTANT:</strong> Do not reply to this email. Contact your examiner directly using the information below.
            </div>
            
            <p>Hello <strong>{studentName}</strong>,</p>
            
            <p>Great news! Your exam has been successfully scheduled.</p>
            
            <div style='background: white; padding: 20px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #28a745;'>
                <h3 style='margin-top: 0; color: #28a745;'>📋 Exam Details</h3>
                <table style='width: 100%;'>
                    <tr>
                        <td style='padding: 8px 0;'><strong>👨‍🏫 Examiner:</strong></td>
                        <td>{examinerName}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0;'><strong>📧 Examiner Email:</strong></td>
                        <td><a href='mailto:{examinerEmail}' style='color: #5CADD3;'>{examinerEmail ?? "Not provided"}</a></td>
                    </tr>
                    {(!string.IsNullOrEmpty(examinerPhone) ? $@"
                    <tr>
                        <td style='padding: 8px 0;'><strong>📱 Examiner Phone:</strong></td>
                        <td><a href='tel:{examinerPhone}' style='color: #5CADD3;'>{examinerPhone}</a></td>
                    </tr>" : "")}
                    <tr>
                        <td style='padding: 8px 0;'><strong>📅 Date:</strong></td>
                        <td>{scheduledDate:dddd, MMMM dd, yyyy}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0;'><strong>⏰ Time:</strong></td>
                        <td>{scheduledDate:HH:mm}</td>
                    </tr>
                    {(!string.IsNullOrEmpty(venueDetails) ? $@"
                    <tr>
                        <td style='padding: 8px 0;'><strong>📍 Venue:</strong></td>
                        <td>{venueDetails}</td>
                    </tr>" : "")}
                    {(price.HasValue && price > 0 ? $@"
                    <tr>
                        <td style='padding: 8px 0;'><strong>💰 Exam Fee:</strong></td>
                        <td>${price:F2}</td>
                    </tr>" : "")}
                    {(!string.IsNullOrEmpty(examinerMessage) ? $@"
                    <tr>
                        <td style='padding: 8px 0;' valign='top'><strong>💬 Message from Examiner:</strong></td>
                        <td>{examinerMessage}</td>
                    </tr>" : "")}
                </table>
            </div>
            
            <div style='background: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 8px; margin: 20px 0;'>
                <p style='margin: 0; color: #155724;'>
                    📧 You will receive a calendar invitation shortly with all the details.
                </p>
            </div>
            
            <div style='background: #f0f0f0; padding: 15px; border-radius: 8px; margin: 20px 0;'>
                <h4 style='margin-top: 0;'>📝 Important Reminders:</h4>
                <ul style='margin: 0; padding-left: 20px;'>
                    <li>Bring logbook, aircraft documents, medical certificate and any other required paperwork</li>
                    <li>Complete your IACRA ASAP (within 72 hours of test) to avoid cancellation of check</li>
                    <li>Payment will be processed at the beginning of the test</li>
                    <li>Arrive at least 15 minutes early</li>
                    <li>The examiner will contact you with any questions related to case of events/flight planning</li>
                </ul>
            </div>
            
            <p>If you need to reschedule or have any questions, please contact us as soon as possible.</p>
            
            <p>Good luck with your exam!</p>
            
            <div style='margin-top: 30px; padding-top: 20px; border-top: 1px solid #dee2e6; color: #6c757d; font-size: 12px;'>
                <p>Best regards,<br>
                JUMPSEAT Team<br><br>
                This is an automated message. Do not reply to this email.</p>
            </div>
        </div>
    </div>";

            _logger.LogInformation($"📤 Sending confirmation email to {studentName} ({studentEmail})");
            var result = await SendEmailAsync(studentEmail, subject, body, "JUMPSEAT");

            if (result)
            {
                _logger.LogInformation($"✅ Student confirmation email sent successfully");
            }
            else
            {
                _logger.LogWarning($"❌ Failed to send student confirmation email");
            }

            return result;
        }

        private string CleanHtmlForPreview(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            // Remove HTML tags
            var cleanText = Regex.Replace(html, @"<[^>]*>", "");
            
            // Replace common HTML entities
            cleanText = cleanText.Replace("&nbsp;", " ")
                                .Replace("&amp;", "&")
                                .Replace("&lt;", "<")
                                .Replace("&gt;", ">")
                                .Replace("&quot;", "\"")
                                .Replace("&#39;", "'");

            // Clean up extra whitespace
            cleanText = Regex.Replace(cleanText, @"\s+", " ").Trim();

            return cleanText;
        }
    }
}
