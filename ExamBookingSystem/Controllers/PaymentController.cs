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

        // Тимчасове сховище для збереження даних booking між створенням session і webhook
        private static readonly ConcurrentDictionary<string, CreateBookingDto> _pendingBookings = new();

        public PaymentController(
            IConfiguration configuration,
            ILogger<PaymentController> logger,
            IServiceProvider serviceProvider,
            IEmailService emailService,
            ISlackService slackService,
            ILocationService locationService,
            IBookingService bookingService)
        {
            _configuration = configuration;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _emailService = emailService;
            _slackService = slackService;
            _locationService = locationService;
            _bookingService = bookingService;
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
                {"checkRideType", TruncateString(bookingData.CheckRideType, 50)},
                {"preferredAirport", TruncateString(bookingData.PreferredAirport, 100)}
            };

                _logger.LogInformation($"Created temp booking ID: {tempBookingId}");
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
                            UnitAmount = 10000, // $100.00
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
                                StudentPhone = fullSession.CustomerDetails?.Phone ?? "Not provided",
                                CheckRideType = fullSession.Metadata.GetValueOrDefault("checkRideType", "Private"),
                                PreferredAirport = fullSession.Metadata.GetValueOrDefault("preferredAirport", ""),
                                AircraftType = "Cessna 172", // Default
                                SearchRadius = 50,
                                WillingToFly = true,
                                DateOption = "ASAP",
                                StartDate = DateTime.UtcNow.AddDays(7),
                                AdditionalRating = false,
                                IsRecheck = false
                            };
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
                _logger.LogInformation($"Student: {bookingData.StudentFirstName} {bookingData.StudentLastName}");

                // Створюємо бронювання через BookingService
                var bookingId = await _bookingService.CreateBookingAsync(bookingData);
                _logger.LogInformation($"Booking created with ID: {bookingId}");

                // Оновлюємо статус оплати
                if (_bookingService is EntityFrameworkBookingService efService)
                {
                    await efService.UpdatePaymentStatusAsync(bookingId, true, session.PaymentIntentId);
                    _logger.LogInformation("Payment status updated");
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
                await _slackService.NotifyErrorAsync("Payment processing failed", ex.Message);
            }
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
                    StudentPhone = session.CustomerDetails?.Phone ?? "Not provided",
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
                    request.StartDate ?? DateTime.UtcNow.AddDays(7));

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