using ExamBookingSystem.DTOs;
using ExamBookingSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ExamBookingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentController> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEmailService _emailService;
        private readonly ISlackService _slackService;
        private readonly ILocationService _locationService;
        private readonly IBookingService _bookingService;
        private readonly ExamBookingSystem.Services.ISettingsService _settingsService;

        // Тимчасове сховище для збереження даних booking між створенням session і webhook
        private static readonly ConcurrentDictionary<string, CreateBookingDto> _pendingBookings = new();

        public PaymentController(
            IConfiguration configuration,
            ILogger<PaymentController> logger,
            IServiceProvider serviceProvider,
            IEmailService emailService,
            ISlackService slackService,
            ILocationService locationService,
            IBookingService bookingService,
            ISettingsService settingsService)
        {
            _configuration = configuration;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _emailService = emailService;
            _slackService = slackService;
            _locationService = locationService;
            _bookingService = bookingService;
            _settingsService = settingsService;
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
        }

        [HttpPost("create-checkout-session")]
        public async Task<ActionResult> CreateCheckoutSession([FromBody] CreateBookingDto bookingData)
        {
            try
            {
                _logger.LogInformation("=== CREATING STRIPE CHECKOUT SESSION ===");
                _logger.LogInformation($"Student: {bookingData.StudentFirstName} {bookingData.StudentLastName}");
                _logger.LogInformation($"Email: {bookingData.StudentEmail}");
                _logger.LogInformation($"Exam Type: {bookingData.CheckRideType}");

                var domain = $"{Request.Scheme}://{Request.Host}";
                _logger.LogInformation($"Domain: {domain}");

                // Створюємо попередній booking ID
                var tempBookingId = $"TEMP_{Guid.NewGuid():N}";

                // Зберігаємо дані booking в пам'яті
                _pendingBookings[tempBookingId] = bookingData;

                // Видаляємо старі записи (більше 1 години)
                var oldKeys = _pendingBookings.Where(kvp => kvp.Key.StartsWith("TEMP_"))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in oldKeys)
                {
                    if (_pendingBookings.TryGetValue(key, out var oldBooking))
                    {
                        // Видаляємо якщо старіше 1 години (можна додати timestamp)
                        _pendingBookings.TryRemove(key, out _);
                    }
                }

                // Обмежуємо довжину значень для Stripe metadata (max 500 символів)
                var metadata = new Dictionary<string, string>
            {
                {"tempBookingId", tempBookingId}, // Ключове поле!
                {"studentFirstName", TruncateString(bookingData.StudentFirstName, 100)},
                {"studentLastName", TruncateString(bookingData.StudentLastName, 100)},
                {"studentEmail", TruncateString(bookingData.StudentEmail, 200)},
                {"studentPhone", TruncateString(bookingData.StudentPhone, 50)},
                {"checkRideType", TruncateString(bookingData.CheckRideType, 50)},
                {"preferredAirport", TruncateString(bookingData.PreferredAirport, 100)},
                {"aircraftType", TruncateString(bookingData.AircraftType, 100)}
            };

                _logger.LogInformation($"Created temp booking ID: {tempBookingId}");
                _logger.LogInformation($"Metadata items: {string.Join(", ", metadata.Keys)}");
                _logger.LogInformation($"Phone in metadata: '{bookingData.StudentPhone}'");

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "Aviation Checkride Booking",
                                Description = $"{bookingData.CheckRideType} checkride for {bookingData.StudentFirstName} {bookingData.StudentLastName}"
                            },
                            UnitAmount = _settingsService.GetBookingFee() * 100,
                        },
                        Quantity = 1,
                    }
                },
                    Mode = "payment",
                    SuccessUrl = $"{domain}/payment-success.html?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl = $"{domain}/index.html",
                    CustomerEmail = bookingData.StudentEmail, // Важливо!
                    Metadata = metadata
                };

                var service = new SessionService();
                var session = await service.CreateAsync(options);

                _logger.LogInformation($"Stripe session created: {session.Id}");
                _logger.LogInformation($"Session URL: {session.Url}");

                return Ok(new { sessionId = session.Id, url = session.Url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating checkout session");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            _logger.LogInformation("=== STRIPE WEBHOOK RECEIVED ===");
            _logger.LogInformation($"Headers: {string.Join(", ", Request.Headers.Keys)}");

            string json;
            using (var reader = new StreamReader(HttpContext.Request.Body))
            {
                json = await reader.ReadToEndAsync();
            }

            _logger.LogInformation($"Webhook payload length: {json.Length}");

            try
            {
                Event stripeEvent;
                _logger.LogInformation($"=== WEBHOOK BODY ===\n{json.Substring(0, Math.Min(json.Length, 500))}");
                var webhookSecret = _configuration["Stripe:WebhookSecret"];

                if (string.IsNullOrEmpty(webhookSecret) || webhookSecret == "whsec_wQpDRHxJ7lo3yps4JpprW3JvS8pjbva4")
                {
                    _logger.LogWarning("⚠️ Webhook signature validation SKIPPED (test mode)");
                    stripeEvent = EventUtility.ParseEvent(json);
                }
                else
                {
                    var signatureHeader = Request.Headers["Stripe-Signature"];
                    try
                    {
                        stripeEvent = EventUtility.ConstructEvent(
                            json,
                            signatureHeader,
                            webhookSecret
                        );
                        _logger.LogInformation("✅ Webhook signature validated");
                    }
                    catch (StripeException ex)
                    {
                        _logger.LogError($"❌ Webhook signature validation failed: {ex.Message}");
                        return BadRequest("Invalid signature");
                    }
                }

                _logger.LogInformation($"Event Type: {stripeEvent.Type}");
                _logger.LogInformation($"Event ID: {stripeEvent.Id}");

                if (stripeEvent.Type == "checkout.session.completed")
                {
                    _logger.LogInformation("Processing checkout.session.completed event");

                    var session = stripeEvent.Data.Object as Session;
                    if (session != null)
                    {
                        _logger.LogInformation($"Session ID: {session.Id}");
                        _logger.LogInformation($"Payment Status: {session.PaymentStatus}");

                        // Перевіряємо чи вже обробили цю сесію
                        var processedKey = $"PROCESSED_{session.Id}";
                        if (_pendingBookings.ContainsKey(processedKey))
                        {
                            _logger.LogWarning($"Session {session.Id} already processed, skipping");
                            return Ok(new { received = true, status = "already_processed" });
                        }

                        // Отримуємо повну сесію з Stripe API
                        var sessionService = new SessionService();
                        var fullSession = await sessionService.GetAsync(session.Id);

                        _logger.LogInformation($"Customer Email: {fullSession.CustomerEmail}");
                        _logger.LogInformation($"Amount Total: {fullSession.AmountTotal}");

                        // Спочатку пробуємо знайти booking за tempBookingId
                        CreateBookingDto? bookingData = null;

                        if (fullSession.Metadata != null && fullSession.Metadata.ContainsKey("tempBookingId"))
                        {
                            var tempBookingId = fullSession.Metadata["tempBookingId"];
                            _logger.LogInformation($"Found tempBookingId in metadata: {tempBookingId}");

                            if (_pendingBookings.TryRemove(tempBookingId, out bookingData))
                            {
                                _logger.LogInformation($"✅ Retrieved booking data from memory for {tempBookingId}");
                                _logger.LogInformation($"Phone from booking data: '{bookingData.StudentPhone}'");
                            }
                        }

                        // Якщо не знайшли в пам'яті, пробуємо відтворити з metadata
                        if (bookingData == null && fullSession.Metadata != null && fullSession.Metadata.Any())
                        {
                            _logger.LogWarning("Booking not found in memory, recreating from metadata");

                            bookingData = new CreateBookingDto
                            {
                                StudentFirstName = fullSession.Metadata.GetValueOrDefault("studentFirstName", ""),
                                StudentLastName = fullSession.Metadata.GetValueOrDefault("studentLastName", ""),
                                StudentEmail = fullSession.CustomerEmail ?? fullSession.Metadata.GetValueOrDefault("studentEmail", ""),
                                StudentPhone = fullSession.Metadata.GetValueOrDefault("studentPhone", ""),
                                CheckRideType = fullSession.Metadata.GetValueOrDefault("checkRideType", "Private"),
                                PreferredAirport = fullSession.Metadata.GetValueOrDefault("preferredAirport", ""),
                                AircraftType = fullSession.Metadata.GetValueOrDefault("aircraftType", "N/A"),
                                SearchRadius = 50,
                                WillingToFly = true,
                                DateOption = "ASAP",
                                StartDate = DateTime.UtcNow.AddDays(7),
                                AdditionalRating = false,
                                IsRecheck = false
                            };

                            // Логування для перевірки
                            _logger.LogInformation($"Phone from metadata: '{bookingData.StudentPhone}'");
                            _logger.LogInformation($"Recreated booking with Aircraft Type: '{bookingData.AircraftType}'");
                        }

                        if (bookingData != null)
                        {
                            // Позначаємо сесію як оброблену
                            _pendingBookings[processedKey] = bookingData;

                            // Обробляємо успішний платіж
                            await ProcessSuccessfulPayment(fullSession, bookingData);

                            _logger.LogInformation($"✅ Webhook processed successfully for session {session.Id}");
                        }
                        else
                        {
                            _logger.LogError($"❌ No booking data found for session {session.Id}");

                            // Спробуємо створити мінімальний booking з email
                            if (!string.IsNullOrEmpty(fullSession.CustomerEmail))
                            {
                                await CreateMinimalBooking(fullSession);
                            }
                        }
                    }
                }

                return Ok(new { received = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Webhook processing error");
                return Ok(new { received = true, error = ex.Message });
            }
        }

        private async Task ProcessSuccessfulPayment(Session session, CreateBookingDto bookingData)
        {
            try
            {
                _logger.LogInformation($"=== PROCESSING SUCCESSFUL PAYMENT ===");
                _logger.LogInformation($"Session ID: {session.Id}");
                _logger.LogInformation($"Payment Intent ID: {session.PaymentIntentId}");
                _logger.LogInformation($">>> Phone before creating booking: '{bookingData.StudentPhone}'");
                _logger.LogInformation($">>> Is phone null or empty: {string.IsNullOrEmpty(bookingData.StudentPhone)}");

                // Створюємо бронювання
                var bookingId = await _bookingService.CreateBookingAsync(bookingData);
                _logger.LogInformation($"Booking created with ID: {bookingId}");

                // ВАЖЛИВО: Оновлюємо статус оплати з PaymentIntentId
                if (_bookingService is EntityFrameworkBookingService efService)
                {
                    await efService.UpdatePaymentStatusAsync(bookingId, true, session.PaymentIntentId);
                    _logger.LogInformation($"Payment status updated with PaymentIntentId: {session.PaymentIntentId}");
                }

                // Відправляємо email підтвердження оплати
                try
                {
                    var emailBody = $@"
<html>
<body style='margin: 0; padding: 0; font-family: Arial, sans-serif; background-color: #f5f5f5;'>
    <div style='max-width: 600px; margin: 0 auto; background-color: white;'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #5CADD3 0%, #2c3e50 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;'>
            <img src='https://yourdomain.com/jumpseat-logo.png' alt='JUMPSEAT' style='height: 50px; margin-bottom: 10px;'>
            <h1 style='color: white; margin: 0; font-size: 32px; font-weight: 600;'>JUMPSEAT</h1>
            <p style='color: white; margin: 10px 0 0 0; font-size: 16px; opacity: 0.95;'>One Flight Away</p>
        </div>
        
        <!-- Success Banner -->
        <div style='background-color: #d4edda; border-bottom: 3px solid #28a745; padding: 20px; text-align: center;'>
            <div style='font-size: 48px; margin-bottom: 10px;'>✅</div>
            <h2 style='color: #155724; margin: 0; font-size: 24px;'>Payment Confirmed!</h2>
            <p style='color: #155724; margin: 5px 0 0 0;'>Your booking request has been received</p>
        </div>
        
        <!-- Main Content -->
        <div style='padding: 30px;'>
            <p style='font-size: 16px; color: #333; margin-bottom: 25px;'>
                Dear <strong>{bookingData.StudentFirstName} {bookingData.StudentLastName}</strong>,
            </p>
            
            <p style='font-size: 15px; color: #555; line-height: 1.6;'>
                Thank you for choosing JUMPSEAT! We've successfully received your payment and your booking request is now being processed.
            </p>
            
            <!-- Booking Details Card -->
            <div style='background: #f8f9fa; border-left: 4px solid #5CADD3; padding: 20px; margin: 25px 0; border-radius: 5px;'>
                <h3 style='margin: 0 0 15px 0; color: #2c3e50; font-size: 18px;'>📋 Booking Information</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 8px 0; color: #666; width: 40%;'>Booking Reference:</td>
                        <td style='padding: 8px 0; color: #333; font-weight: bold;'>{bookingId}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0; color: #666;'>Exam Type:</td>
                        <td style='padding: 8px 0; color: #333;'>{bookingData.CheckRideType}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0; color: #666;'>Preferred Location:</td>
                        <td style='padding: 8px 0; color: #333;'>{bookingData.PreferredAirport}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0; color: #666;'>Preferred Date:</td>
                        <td style='padding: 8px 0; color: #333;'>{(bookingData.StartDate?.ToString("MMMM dd, yyyy") ?? "ASAP")}</td>
                    </tr>
                    <tr style='border-top: 1px solid #dee2e6;'>
                        <td style='padding: 12px 0 0 0; color: #666; font-size: 16px;'>Amount Paid:</td>
                        <td style='padding: 12px 0 0 0; color: #28a745; font-size: 20px; font-weight: bold;'>${_settingsService.GetBookingFee()}.00</td>
                    </tr>
                </table>
            </div>
            
            <!-- Next Steps -->
            <div style='background: linear-gradient(135deg, #e7f3ff 0%, #f0f7ff 100%); padding: 20px; margin: 25px 0; border-radius: 8px;'>
                <h3 style='margin: 0 0 15px 0; color: #0066cc; font-size: 18px;'>🚀 What Happens Next?</h3>
                <ol style='margin: 0; padding-left: 20px; color: #333; line-height: 1.8;'>
                    <li style='margin-bottom: 8px;'>Our system is actively searching for qualified examiners in your area</li>
                    <li style='margin-bottom: 8px;'>We'll contact available examiners within your specified radius</li>
                    <li style='margin-bottom: 8px;'>Once an examiner accepts, you'll receive a confirmation email</li>
                    <li style='margin-bottom: 8px;'>The examiner will contact you directly to finalize all details</li>
                </ol>
                <div style='background: white; padding: 12px; margin-top: 15px; border-radius: 5px; border-left: 3px solid #0066cc;'>
                    <p style='margin: 0; color: #0066cc; font-size: 14px;'>
                        <strong>⏱️ Expected Timeline:</strong> Most students are matched within 48-72 hours
                    </p>
                </div>
            </div>
            
            <!-- Important Notice -->
            <div style='background: #fff3cd; border: 1px solid #ffc107; padding: 15px; margin: 25px 0; border-radius: 5px;'>
                <p style='margin: 0; color: #856404; font-size: 14px;'>
                    <strong>📌 Important:</strong> Please ensure your phone is available as examiners may contact you directly. 
                    Check your email regularly for updates on your booking status.
                </p>
            </div>
            
            <!-- Support Section -->
            <div style='text-align: center; padding: 20px; background: #f8f9fa; border-radius: 5px; margin: 25px 0;'>
                <p style='margin: 0 0 10px 0; color: #666; font-size: 14px;'>Need assistance?</p>
                <p style='margin: 0; color: #333; font-size: 16px;'>
                    Our support team is here to help<br>
                    <a href='mailto:main@jumpseat.us' style='color: #5CADD3; text-decoration: none; font-weight: bold;'>main@jumpseat.us</a>
                </p>
            </div>
        </div>
        
        <!-- Footer -->
        <div style='background: #2c3e50; padding: 25px; text-align: center; border-radius: 0 0 10px 10px;'>
            <p style='color: #95a5a6; margin: 0 0 10px 0; font-size: 14px;'>
                Thank you for choosing JUMPSEAT
            </p>
            <p style='color: #7f8c8d; margin: 0; font-size: 12px;'>
                This is an automated confirmation email. Please do not reply directly to this message.
            </p>
            <div style='margin-top: 20px; padding-top: 20px; border-top: 1px solid #34495e;'>
                <p style='color: #7f8c8d; margin: 0; font-size: 11px;'>
                    © 2025 JUMPSEAT. All rights reserved.
                </p>
            </div>
        </div>
    </div>
</body>
</html>";

                    await _emailService.SendEmailAsync(
                        bookingData.StudentEmail,
                        $"✅ Payment Confirmed - Booking {bookingId}",
                        emailBody
                    );

                    _logger.LogInformation($"Payment confirmation email sent to {bookingData.StudentEmail}");
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send payment confirmation email");
                    // Не блокуємо процес через помилку email
                }

                // Геокодуємо адресу
                var coordinates = await _locationService.GeocodeAddressAsync(bookingData.PreferredAirport);
                if (!coordinates.HasValue)
                {
                    _logger.LogWarning($"Unable to geocode airport: {bookingData.PreferredAirport}");
                    await _slackService.NotifyErrorAsync("Geocoding failed", $"Could not geocode {bookingData.PreferredAirport}");
                    return;
                }
                _logger.LogInformation($"Geocoded to ({coordinates.Value.Latitude}, {coordinates.Value.Longitude})");

                // Знаходимо екзаменаторів
                var radiusKm = bookingData.SearchRadius * 1.852;
                var nearbyExaminers = await _locationService.FindNearbyExaminersAsync(
                    coordinates.Value.Latitude,
                    coordinates.Value.Longitude,
                    radiusKm,
                    bookingData.CheckRideType);

                if (!nearbyExaminers.Any())
                {
                    _logger.LogWarning("No examiners found");
                    await _slackService.NotifyErrorAsync("No examiners found",
                        $"No qualified examiners for {bookingData.StudentFirstName} {bookingData.StudentLastName}");
                    return;
                }

                // Оновлюємо статус
                await _bookingService.UpdateBookingStatusAsync(bookingId, Services.BookingStatus.ExaminersContacted);

                // Slack повідомлення
                await _slackService.NotifyNewBookingAsync(
                    $"{bookingData.StudentFirstName} {bookingData.StudentLastName}",
                    bookingData.CheckRideType,
                    bookingData.StartDate ?? DateTime.UtcNow.AddDays(7));

                // Контактуємо з екзаменаторами
                var maxExaminers = _configuration.GetValue("ApplicationSettings:MaxExaminersToContact", 3);
                var examinersToContact = nearbyExaminers.Take(maxExaminers).ToList();
                _logger.LogInformation($"Contacting {examinersToContact.Count} examiners");

                var contactTasks = examinersToContact.Select(examiner =>
                    ContactExaminerAsync(examiner, bookingData, bookingId));
                await Task.WhenAll(contactTasks);

                _logger.LogInformation($"✅ Successfully processed payment and created booking {bookingId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing successful payment");
                throw;
            }
        }

        private string? NormalizeExamType(string? examType)
        {
            if (string.IsNullOrWhiteSpace(examType))
                return null;

            var normalized = examType.Trim();

            // Маппінг повних назв на скорочені
            if (normalized.Contains("Private", StringComparison.OrdinalIgnoreCase))
                return "Private";

            if (normalized.Contains("Instrument", StringComparison.OrdinalIgnoreCase))
                return "Instrument";

            if (normalized.Contains("Commercial", StringComparison.OrdinalIgnoreCase))
                return "Commercial";

            if (normalized.Contains("CFI", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("Flight Instructor", StringComparison.OrdinalIgnoreCase))
                return "CFI";

            if (normalized.Contains("CFII", StringComparison.OrdinalIgnoreCase))
                return "CFII";

            if (normalized.Contains("MEI", StringComparison.OrdinalIgnoreCase))
                return "MEI";

            if (normalized.Contains("ATP", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("Airline Transport", StringComparison.OrdinalIgnoreCase))
                return "ATP";

            if (normalized.Contains("Multi", StringComparison.OrdinalIgnoreCase))
                return "MultiEngine";

            if (normalized.Contains("Sport", StringComparison.OrdinalIgnoreCase))
                return "SportPilot";

            _logger.LogWarning($"Could not normalize exam type: '{examType}'");
            return normalized;
        }
        private async Task CreateMinimalBooking(Session session)
        {
            try
            {
                _logger.LogInformation("Creating minimal booking from session data");

                var bookingData = new CreateBookingDto
                {
                    StudentFirstName = "Unknown",
                    StudentLastName = "Student",
                    StudentEmail = session.CustomerEmail ?? "unknown@example.com",
                    StudentPhone = session.CustomerDetails?.Phone ?? "",
                    CheckRideType = "Private",
                    PreferredAirport = "Unknown",
                    AircraftType = "Unknown",
                    SearchRadius = 50,
                    WillingToFly = true,
                    DateOption = "ASAP",
                    StartDate = DateTime.UtcNow.AddDays(7)
                };

                await ProcessSuccessfulPayment(session, bookingData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create minimal booking");
            }
        }

        private string TruncateString(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private async Task ContactExaminerAsync(ExaminerLocation examiner, CreateBookingDto request, string bookingId)
        {
            try
            {
                _logger.LogInformation($"Contacting examiner {examiner.Name} ({examiner.Email})");

                var success = await _emailService.SendExaminerContactEmailAsync(
                    examiner.Email,
                    examiner.Name,
                    $"{request.StudentFirstName} {request.StudentLastName}",
                    request.CheckRideType,
                    request.StartDate ?? DateTime.UtcNow.AddDays(7),
                    request.EndDate,
                    request.FtnNumber,
                    request.AdditionalNotes,
                    request.WillingToFly);

                if (success)
                {
                    _logger.LogInformation($"✅ Successfully contacted examiner {examiner.Name}");
                }
                else
                {
                    _logger.LogWarning($"❌ Failed to contact examiner {examiner.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error contacting examiner {examiner.Name}");
            }
        }

        // Тестові endpoints залишаються без змін
        [HttpPost("test-webhook")]
        public async Task<IActionResult> TestWebhook()
        {
            _logger.LogInformation("=== TEST WEBHOOK TRIGGERED ===");

            try
            {
                var testBookingData = new CreateBookingDto
                {
                    StudentFirstName = "Test",
                    StudentLastName = "Student",
                    StudentEmail = "test@example.com",
                    StudentPhone = "+1234567890",
                    AircraftType = "Cessna 172",
                    CheckRideType = "Private",
                    PreferredAirport = "KJFK",
                    SearchRadius = 50,
                    WillingToFly = true,
                    DateOption = "ASAP",
                    StartDate = DateTime.UtcNow.AddDays(7),
                    AdditionalRating = false,
                    IsRecheck = false,
                    AdditionalNotes = "Test booking via test webhook"
                };

                var fakeSession = new Session
                {
                    Id = $"cs_test_{Guid.NewGuid():N}",
                    PaymentIntentId = $"pi_test_{Guid.NewGuid():N}",
                    CustomerEmail = testBookingData.StudentEmail
                };

                await ProcessSuccessfulPayment(fakeSession, testBookingData);

                return Ok(new
                {
                    message = "Test webhook processed successfully",
                    sessionId = fakeSession.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in test webhook");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}