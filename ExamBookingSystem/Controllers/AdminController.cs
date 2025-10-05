using BCrypt.Net;
using ExamBookingSystem.Data;
using ExamBookingSystem.Models;
using ExamBookingSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Tax;

namespace ExamBookingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AdminController> _logger;
        private readonly IStripeService _stripeService;
        private readonly ISettingsService _settingsService;

        public AdminController(ApplicationDbContext context, ILogger<AdminController> logger, IStripeService stripeService, ISettingsService settingsService)
        {
            _context = context;
            _logger = logger;
            _stripeService = stripeService;
            _settingsService = settingsService;

        }

        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] AdminLoginDto dto)
        {
            try
            {
                _logger.LogInformation($"Login attempt for email: {dto.Email}");

                // Спрощена перевірка для тестування
                if (dto.Email == "admin@exambooking.com" && dto.Password == "Admin123!")
                {
                    var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

                    return Ok(new
                    {
                        success = true,
                        token = token,
                        admin = new
                        {
                            email = "admin@exambooking.com",
                            fullName = "System Administrator"
                        }
                    });
                }

                // Перевірка в БД
                var admin = await _context.Administrators
                    .FirstOrDefaultAsync(a => a.Email == dto.Email && a.IsActive);

                if (admin == null)
                {
                    _logger.LogWarning($"Admin not found: {dto.Email}");
                    return Unauthorized(new { message = "Invalid email or password" });
                }

                // Перевірка пароля
                if (!BCrypt.Net.BCrypt.Verify(dto.Password, admin.PasswordHash))
                {
                    _logger.LogWarning($"Invalid password for: {dto.Email}");
                    return Unauthorized(new { message = "Invalid email or password" });
                }

                admin.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                var dbToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

                return Ok(new
                {
                    success = true,
                    token = dbToken,
                    admin = new
                    {
                        email = admin.Email,
                        fullName = admin.FullName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin login error");
                return StatusCode(500, new { message = "Login failed", error = ex.Message });
            }
        }

        [HttpGet("bookings")]
        public async Task<ActionResult> GetAllBookings()
        {
            try
            {
                var bookings = await _context.BookingRequests
                    .Include(b => b.AssignedExaminer)
                    .OrderByDescending(b => b.CreatedAt)
                    .Select(b => new
                    {
                        BookingId = $"BK{b.Id:D6}",
                        StudentName = $"{b.StudentFirstName} {b.StudentLastName}",
                        StudentEmail = b.StudentEmail,
                        StudentPhone = b.StudentPhone,
                        ExamType = b.ExamType,
                        Status = b.Status.ToString(),
                        IsPaid = b.IsPaid,
                        AssignedExaminerName = b.AssignedExaminer != null ? b.AssignedExaminer.Name : null,
                        CreatedAt = b.CreatedAt,
                        ScheduledDate = b.ScheduledDate
                    })
                    .ToListAsync();

                return Ok(bookings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all bookings");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("dashboard-stats")]
        public async Task<ActionResult> GetDashboardStats()
        {
            try
            {
                var stats = new
                {
                    TotalBookings = await _context.BookingRequests.CountAsync(),
                    AssignedBookings = await _context.BookingRequests
                        .CountAsync(b => b.AssignedExaminerId != null),
                    PendingBookings = await _context.BookingRequests
                        .CountAsync(b => b.Status == Models.BookingStatus.ExaminersContacted),
                    NoExaminerFound = await _context.BookingRequests
                        .CountAsync(b => b.Status == Models.BookingStatus.ExaminersContacted &&
                                   b.CreatedAt < DateTime.UtcNow.AddDays(-2)),
                    TotalRevenue = await _context.BookingRequests
                        .Where(b => b.IsPaid)
                        .SumAsync(b => b.Amount)
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard stats");
                return StatusCode(500, "Failed to load statistics");
            }
        }

        [HttpGet("examiner-responses")]
        public async Task<ActionResult> GetExaminerResponses()
        {
            try
            {
                var responses = await _context.ExaminerResponses
                    .Include(r => r.BookingRequest)
                    .OrderByDescending(r => r.ContactedAt)
                    .Take(50)
                    .Select(r => new
                    {
                        BookingId = $"BK{r.BookingRequestId:D6}",
                        ExaminerName = "Examiner", // Тимчасово, бо зв'язок з Examiner відключений
                        ExaminerEmail = "examiner@example.com",
                        Response = r.Response.ToString(),
                        ContactedAt = r.ContactedAt,
                        RespondedAt = r.RespondedAt,
                        ResponseMessage = r.ResponseMessage,
                        StudentName = $"{r.BookingRequest.StudentFirstName} {r.BookingRequest.StudentLastName}"
                    })
                    .ToListAsync();

                return Ok(responses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting examiner responses");
                return StatusCode(500, "Failed to load responses");
            }
        }

        [HttpPost("process-refund/{bookingId}")]
        public async Task<ActionResult> ProcessRefund(string bookingId, [FromBody] RefundRequestDto? refundRequest = null)
        {
            try
            {
                _logger.LogInformation($"Processing refund request for booking {bookingId}");

                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return BadRequest("Invalid booking ID");

                var booking = await _context.BookingRequests
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (booking == null)
                    return NotFound("Booking not found");

                if (!booking.IsPaid)
                    return BadRequest("Booking is not paid - cannot refund");

                if (booking.Status == Models.BookingStatus.Refunded)
                    return BadRequest("Booking already refunded");

                // Якщо немає PaymentIntentId - можливо старий booking
                if (string.IsNullOrEmpty(booking.PaymentIntentId))
                {
                    _logger.LogWarning($"No PaymentIntentId for booking {bookingId} - processing manual refund");

                    // Оновлюємо статус без Stripe refund
                    booking.Status = Models.BookingStatus.Refunded;
                    booking.UpdatedAt = DateTime.UtcNow;

                    var actionLog = new Models.ActionLog
                    {
                        BookingRequestId = booking.Id,
                        ActionType = Models.ActionType.RefundProcessed,
                        Description = $"Manual refund (no payment intent): ${booking.Amount}",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.ActionLogs.Add(actionLog);
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        message = "Manual refund recorded (no Stripe payment found)",
                        refundAmount = booking.Amount,
                        bookingId = bookingId,
                        manual = true
                    });
                }

                // Спробуємо зробити refund через Stripe
                var refundAmount = refundRequest?.Amount ?? booking.Amount;
                var refundReason = refundRequest?.Reason ?? "No examiner available";

                _logger.LogInformation($"Initiating Stripe refund: PaymentIntent={booking.PaymentIntentId}, Amount=${refundAmount}");

                bool refundSuccess = false;
                string refundError = "";

                try
                {
                    refundSuccess = await _stripeService.ProcessRefundAsync(
                        booking.PaymentIntentId,
                        refundAmount,
                        refundReason);
                }
                catch (Exception stripeEx)
                {
                    _logger.LogError(stripeEx, "Stripe refund exception");
                    refundError = stripeEx.Message;
                }

                if (!refundSuccess)
                {
                    // Якщо Stripe refund не вдався, запитаємо користувача чи хоче він зробити manual refund
                    return BadRequest(new
                    {
                        message = "Stripe refund failed. Payment may be too old or already refunded.",
                        error = refundError,
                        suggestion = "You can mark this as manually refunded if you process the refund outside the system.",
                        bookingId = bookingId
                    });
                }

                // Оновлюємо статус в базі даних
                booking.Status = Models.BookingStatus.Refunded;
                booking.UpdatedAt = DateTime.UtcNow;

                // Додаємо лог
                var successLog = new Models.ActionLog
                {
                    BookingRequestId = booking.Id,
                    ActionType = Models.ActionType.RefundProcessed,
                    Description = $"Stripe refund processed: ${refundAmount} - {refundReason}",
                    Details = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Amount = refundAmount,
                        PaymentIntentId = booking.PaymentIntentId,
                        Reason = refundReason,
                        ProcessedAt = DateTime.UtcNow
                    }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.ActionLogs.Add(successLog);

                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Refund completed for booking {bookingId}: ${refundAmount}");

                // Відправити email студенту про повернення
                if (!string.IsNullOrEmpty(booking.StudentEmail))
                {
                    await SendRefundNotificationEmail(booking, refundAmount, refundReason);
                }

                return Ok(new
                {
                    message = "Refund processed successfully through Stripe",
                    refundAmount = refundAmount,
                    bookingId = bookingId,
                    paymentIntentId = booking.PaymentIntentId,
                    stripe = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing refund for {bookingId}");
                return StatusCode(500, $"Failed to process refund: {ex.Message}");
            }
        }

        // Додайте метод для відправки email про повернення
        private async Task SendRefundNotificationEmail(BookingRequest booking, decimal amount, string reason)
        {
            try
            {
                var emailService = HttpContext.RequestServices.GetService<IEmailService>();
                if (emailService != null)
                {
                    var subject = "Refund Processed - Exam Booking";
                    var body = $@"
                <h3>Refund Confirmation</h3>
                <p>Dear {booking.StudentFirstName} {booking.StudentLastName},</p>
                <p>Your refund has been processed successfully.</p>
                <ul>
                    <li>Booking ID: BK{booking.Id:D6}</li>
                    <li>Refund Amount: ${amount}</li>
                    <li>Reason: {reason}</li>
                </ul>
                <p>The refund should appear on your card within 5-10 business days.</p>
                <p>Best regards,<br>JUMPSEAT Team</p>
            ";

                    await emailService.SendEmailAsync(booking.StudentEmail, subject, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send refund notification email");
            }
        }

        [HttpGet("examiner-responses/{bookingId}")]
        public async Task<ActionResult> GetBookingExaminerResponses(string bookingId)
        {
            try
            {
                _logger.LogInformation($"Getting examiner responses for booking: {bookingId}");

                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                {
                    _logger.LogWarning($"Invalid booking ID format: {bookingId}");
                    return BadRequest("Invalid booking ID");
                }

                // Перевіряємо чи існує бронювання
                var bookingExists = await _context.BookingRequests
                    .AnyAsync(b => b.Id == id);

                if (!bookingExists)
                {
                    _logger.LogWarning($"Booking not found: {bookingId}");
                    return NotFound("Booking not found");
                }

                // Отримуємо відповіді екзаменаторів
                var responses = await _context.ExaminerResponses
                    .Where(r => r.BookingRequestId == id)
                    .Select(r => new
                    {
                        ExaminerId = r.ExaminerId,
                        ExaminerName = "Unknown", // Тимчасово, бо зв'язок з Examiner відключений
                        ExaminerEmail = "unknown@example.com", // Тимчасово
                        Response = r.Response.ToString(),
                        ContactedAt = r.ContactedAt,
                        RespondedAt = r.RespondedAt,
                        ResponseMessage = r.ResponseMessage ?? "",
                        ProposedDate = r.ProposedDate,
                        ProposedTime = r.ProposedTime ?? "",
                        ProposedLocation = r.ProposedLocation ?? "",
                        IsWinner = r.IsWinner
                    })
                    .ToListAsync();

                // Отримуємо історію дій
                var actionLogs = await _context.ActionLogs
                    .Where(a => a.BookingRequestId == id)
                    .OrderBy(a => a.CreatedAt)
                    .Select(a => new
                    {
                        ActionType = a.ActionType.ToString(),
                        Description = a.Description ?? "",
                        Details = a.Details ?? "",
                        CreatedAt = a.CreatedAt,
                        UserId = a.UserId ?? "",
                        IpAddress = a.IpAddress ?? ""
                    })
                    .ToListAsync();

                var result = new
                {
                    BookingId = bookingId,
                    ExaminerResponses = responses, // ЗАВЖДИ буде масивом
                    ActionHistory = actionLogs,    // ЗАВЖДИ буде масивом
                    TotalContacted = responses.Count,
                    TotalResponded = responses.Count(r => r.RespondedAt != null),
                    AcceptedCount = responses.Count(r => r.Response == "Accepted"),
                    DeclinedCount = responses.Count(r => r.Response == "Declined")
                };

                _logger.LogInformation($"Returning data for {bookingId}: {responses.Count} responses, {actionLogs.Count} action logs");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting examiner responses for {bookingId}");
                return StatusCode(500, new
                {
                    error = "Failed to retrieve examiner responses",
                    details = ex.Message
                });
            }
        }

        [HttpPost("create-admin")]
        public async Task<ActionResult> CreateAdmin([FromBody] CreateAdminDto dto)
        {
            try
            {
                if (await _context.Administrators.AnyAsync(a => a.Email == dto.Email))
                {
                    return BadRequest("Admin with this email already exists");
                }

                var admin = new Models.Administrator
                {
                    Email = dto.Email,
                    Phone = dto.Phone,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    FullName = dto.FullName,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Administrators.Add(admin);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Admin created successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating admin");
                return StatusCode(500, "Failed to create admin");
            }
        }

        [HttpPost("manual-refund/{bookingId}")]
        public async Task<ActionResult> ManualRefund(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return BadRequest("Invalid booking ID");

                var booking = await _context.BookingRequests
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (booking == null)
                    return NotFound("Booking not found");

                if (booking.Status == Models.BookingStatus.Refunded)
                    return BadRequest("Booking already marked as refunded");

                // Оновлюємо статус
                booking.Status = Models.BookingStatus.Refunded;
                booking.UpdatedAt = DateTime.UtcNow;

                // Додаємо лог
                var actionLog = new Models.ActionLog
                {
                    BookingRequestId = booking.Id,
                    ActionType = Models.ActionType.RefundProcessed,
                    Description = "Manual refund processed by admin",
                    CreatedAt = DateTime.UtcNow
                };
                _context.ActionLogs.Add(actionLog);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Booking marked as refunded (manual)",
                    bookingId = bookingId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing manual refund for {bookingId}");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("settings/booking-fee")]
        public IActionResult GetBookingFee()
        {
            var fee = _settingsService.GetBookingFee();
            return Ok(new { fee });
        }

        [HttpPost("settings/booking-fee")]
        public IActionResult UpdateBookingFee([FromBody] UpdateFeeDto request)
        {
            try
            {
                _settingsService.SetBookingFee(request.Fee);
                return Ok(new { message = "Fee updated successfully" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    // Додайте DTO клас
    public class UpdateFeeDto
    {
        public int Fee { get; set; }
    }

    public class AdminLoginDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
    public class RefundRequestDto
    {
        public decimal? Amount { get; set; }
        public string? Reason { get; set; }
    }
    public class CreateAdminDto
    {
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }
}