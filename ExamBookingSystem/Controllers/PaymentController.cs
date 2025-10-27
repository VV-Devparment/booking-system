using ExamBookingSystem.DTOs;
using ExamBookingSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;

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

                // ✅ ВИПРАВЛЕННЯ: Створюємо букінг в БД ОДРАЗУ зі статусом PaymentPending
                string bookingId;
                try
                {
                    bookingId = await _bookingService.CreateBookingAsync(bookingData);
                    _logger.LogInformation($"✅ Booking created in database: {bookingId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create booking in database");
                    return BadRequest(new { error = "Failed to create booking", details = ex.Message });
                }

                // Зберігаємо bookingId в Stripe metadata (КРИТИЧНО ВАЖЛИВО!)
                var metadata = new Dictionary<string, string>
                {
                    {"bookingId", bookingId}, // ← ГОЛОВНЕ ПОЛЕ для webhook!
                    {"studentFirstName", TruncateString(bookingData.StudentFirstName, 100)},
                    {"studentLastName", TruncateString(bookingData.StudentLastName, 100)},
                    {"studentEmail", TruncateString(bookingData.StudentEmail, 200)},
                    {"studentPhone", TruncateString(bookingData.StudentPhone, 50)},
                    {"checkRideType", TruncateString(bookingData.CheckRideType, 50)},
                    {"preferredAirport", TruncateString(bookingData.PreferredAirport, 100)},
                    {"aircraftType", TruncateString(bookingData.AircraftType, 100)}
                };

                _logger.LogInformation($"Booking ID for Stripe metadata: {bookingId}");
                _logger.LogInformation($"Metadata items: {string.Join(", ", metadata.Keys)}");

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
                    CancelUrl = $"{domain}/index.html?booking_cancelled={bookingId}",
                    CustomerEmail = bookingData.StudentEmail,
                    Metadata = metadata
                };

                var service = new SessionService();
                var session = await service.CreateAsync(options);

                _logger.LogInformation($"Stripe session created: {session.Id}");
                _logger.LogInformation($"Session URL: {session.Url}");

                return Ok(new { sessionId = session.Id, url = session.Url, bookingId = bookingId });
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

                if (string.IsNullOrEmpty(webhookSecret) || webhookSecret == "whsec_GqAYtsMDMgdIdtxMtChivNaAsj3BbLB9")
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

                // ✅ ВИПРАВЛЕНО: використовуємо рядок замість константи (сумісність з версіями)
                if (stripeEvent.Type == "checkout.session.completed")
                {
                    var session = stripeEvent.Data.Object as Session;
                    _logger.LogInformation($"Checkout session completed: {session?.Id}");
                    _logger.LogInformation($"Payment Intent: {session?.PaymentIntentId}");
                    _logger.LogInformation($"Customer Email: {session?.CustomerEmail}");
                    _logger.LogInformation($"Payment Status: {session?.PaymentStatus}");
                    _logger.LogInformation($"Metadata count: {session?.Metadata?.Count ?? 0}");

                    if (session?.Metadata != null && session.Metadata.Count > 0)
                    {
                        _logger.LogInformation("=== METADATA CONTENTS ===");
                        foreach (var kvp in session.Metadata)
                        {
                            _logger.LogInformation($"  {kvp.Key}: {kvp.Value}");
                        }
                    }

                    // ✅ ВИПРАВЛЕННЯ: Шукаємо букінг в БД по bookingId з metadata
                    if (session?.Metadata != null && session.Metadata.TryGetValue("bookingId", out var bookingId))
                    {
                        _logger.LogInformation($"🔍 Found bookingId in metadata: {bookingId}");

                        try
                        {
                            // Знаходимо букінг в БД
                            var booking = await _bookingService.GetBookingAsync(bookingId);
                            
                            if (booking == null)
                            {
                                _logger.LogError($"❌ Booking {bookingId} not found in database!");
                                return BadRequest($"Booking {bookingId} not found");
                            }

                            _logger.LogInformation($"✅ Booking found in database: {bookingId}");
                            _logger.LogInformation($"   Status: {booking.Status}");
                            _logger.LogInformation($"   Student: {booking.StudentName}");
                            _logger.LogInformation($"   Email: {booking.StudentEmail}");

                            // Оновлюємо статус оплати
                            var updated = await _bookingService.UpdatePaymentStatusAsync(
                                bookingId, 
                                isPaid: true, 
                                paymentIntentId: session.PaymentIntentId
                            );

                            if (!updated)
                            {
                                _logger.LogError($"❌ Failed to update payment status for booking {bookingId}");
                                return StatusCode(500, "Failed to update payment status");
                            }

                            _logger.LogInformation($"✅ Payment status updated for booking {bookingId}");

                            // Обробляємо букінг (надсилаємо email, шукаємо екзаменаторів)
                            await ProcessSuccessfulPaymentForBooking(session, bookingId);

                            return Ok(new { received = true, bookingId = bookingId });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error processing booking {bookingId}");
                            return StatusCode(500, ex.Message);
                        }
                    }
                    else
                    {
                        _logger.LogError("❌ No bookingId found in metadata!");
                        _logger.LogError("   This should never happen with the new code.");
                        return BadRequest("No bookingId in metadata");
                    }
                }
                else
                {
                    _logger.LogInformation($"Unhandled event type: {stripeEvent.Type}");
                }

                return Ok(new { received = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return StatusCode(500, ex.Message);
            }
        }

        // ✅ НОВИЙ МЕТОД: Обробка успішної оплати для існуючого букінгу
        private async Task ProcessSuccessfulPaymentForBooking(Session session, string bookingId)
        {
            try
            {
                _logger.LogInformation($"=== PROCESSING SUCCESSFUL PAYMENT FOR BOOKING {bookingId} ===");

                // Отримуємо букінг з БД
                var booking = await _bookingService.GetBookingAsync(bookingId);
                
                if (booking == null)
                {
                    _logger.LogError($"Booking {bookingId} not found");
                    return;
                }

                // ✅ ВИПРАВЛЕНО: Відправляємо email з правильними параметрами
                // SendStudentConfirmationEmailAsync(studentEmail, studentName, examinerName, scheduledDate, ...)
                await _emailService.SendStudentConfirmationEmailAsync(
                    booking.StudentEmail,
                    booking.StudentName,
                    "TBD", // examinerName - буде призначено пізніше
                    booking.PreferredDate, // scheduledDate
                    null, // examinerEmail
                    null, // examinerPhone
                    null, // venueDetails
                    "Your booking has been confirmed. An examiner will contact you soon.", // examinerMessage
                    null  // price
                );

                _logger.LogInformation($"✅ Confirmation email sent to {booking.StudentEmail}");

                // Отримуємо дані з metadata (якщо є додаткова інформація)
                var preferredAirport = session.Metadata?.GetValueOrDefault("preferredAirport") ?? "Unknown";
                var searchRadius = 50.0; // Default radius in km

                // ✅ ВИПРАВЛЕНО: Спочатку геокодуємо airport в координати
                var coordinates = await _locationService.GeocodeAddressAsync(preferredAirport);
                
                if (coordinates == null)
                {
                    _logger.LogWarning($"Could not geocode airport: {preferredAirport}");
                    await _slackService.NotifyNewBookingAsync(
                        booking.StudentName,
                        booking.ExamType,
                        booking.PreferredDate);
                    return;
                }

                // Шукаємо найближчих екзаменаторів
                var nearbyExaminers = await _locationService.FindNearbyExaminersAsync(
                    coordinates.Value.Latitude,
                    coordinates.Value.Longitude,
                    searchRadius,
                    booking.ExamType
                );

                _logger.LogInformation($"Found {nearbyExaminers.Count} examiners within {searchRadius} km");

                if (nearbyExaminers.Count == 0)
                {
                    _logger.LogWarning("No examiners found in the specified area");
                    await _slackService.NotifyNewBookingAsync(
                        booking.StudentName,
                        booking.ExamType,
                        booking.PreferredDate);
                    return;
                }

                // Оновлюємо статус букінгу на ExaminersContacted
                await _bookingService.UpdateBookingStatusAsync(bookingId, Services.BookingStatus.ExaminersContacted);

                // Slack повідомлення
                await _slackService.NotifyNewBookingAsync(
                    booking.StudentName,
                    booking.ExamType,
                    booking.PreferredDate);

                // Контактуємо з екзаменаторами
                var maxExaminers = _configuration.GetValue("ApplicationSettings:MaxExaminersToContact", 3);
                var examinersToContact = nearbyExaminers.Take(maxExaminers).ToList();
                _logger.LogInformation($"Contacting {examinersToContact.Count} examiners");

                // Створюємо CreateBookingDto для контакту з екзаменаторами
                var studentNameParts = booking.StudentName.Split(' ', 2);
                var bookingDto = new CreateBookingDto
                {
                    StudentFirstName = studentNameParts.Length > 0 ? studentNameParts[0] : booking.StudentName,
                    StudentLastName = studentNameParts.Length > 1 ? studentNameParts[1] : "",
                    StudentEmail = booking.StudentEmail,
                    CheckRideType = booking.ExamType,
                    PreferredAirport = preferredAirport,
                    StartDate = booking.PreferredDate,
                    WillingToFly = true
                };

                var contactTasks = examinersToContact.Select(examiner =>
                    ContactExaminerAsync(examiner, bookingDto, bookingId));
                await Task.WhenAll(contactTasks);

                _logger.LogInformation($"✅ Successfully processed payment and updated booking {bookingId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing successful payment for booking {bookingId}");
                throw;
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

        // ===================================
        // ТЕСТОВІ ENDPOINTS
        // ===================================

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

                // Створюємо букінг в БД
                var bookingId = await _bookingService.CreateBookingAsync(testBookingData);
                _logger.LogInformation($"Test booking created: {bookingId}");

                // Оновлюємо статус оплати
                await _bookingService.UpdatePaymentStatusAsync(bookingId, isPaid: true, paymentIntentId: $"pi_test_{Guid.NewGuid():N}");

                var fakeSession = new Session
                {
                    Id = $"cs_test_{Guid.NewGuid():N}",
                    PaymentIntentId = $"pi_test_{Guid.NewGuid():N}",
                    CustomerEmail = testBookingData.StudentEmail,
                    Metadata = new Dictionary<string, string>
                    {
                        {"bookingId", bookingId},
                        {"preferredAirport", testBookingData.PreferredAirport}
                    }
                };

                await ProcessSuccessfulPaymentForBooking(fakeSession, bookingId);

                return Ok(new
                {
                    message = "Test webhook processed successfully",
                    bookingId = bookingId,
                    sessionId = fakeSession.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in test webhook");
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("test-db-connection")]
        public async Task<IActionResult> TestDbConnection()
        {
            try
            {
                // Створюємо тестовий букінг
                var testBooking = new CreateBookingDto
                {
                    StudentFirstName = "DB",
                    StudentLastName = "Test",
                    StudentEmail = "dbtest@example.com",
                    StudentPhone = "+1234567890",
                    AircraftType = "Test Aircraft",
                    CheckRideType = "Private",
                    PreferredAirport = "TEST",
                    SearchRadius = 50,
                    WillingToFly = true,
                    DateOption = "ASAP",
                    StartDate = DateTime.UtcNow.AddDays(7)
                };

                var bookingId = await _bookingService.CreateBookingAsync(testBooking);
                
                // Читаємо назад
                var retrievedBooking = await _bookingService.GetBookingAsync(bookingId);

                return Ok(new
                {
                    message = "Database connection test successful",
                    bookingId = bookingId,
                    created = retrievedBooking != null,
                    booking = retrievedBooking
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }
    }
}
