using ExamBookingSystem.Data;
using ExamBookingSystem.DTOs;
using ExamBookingSystem.Models;
using ExamBookingSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SendGrid.Helpers.Mail;
using BookingStatus = ExamBookingSystem.Models.BookingStatus;
using ExamBookingSystem.Data;
// Замініть початок класу BookingController на це:

namespace ExamBookingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookingController : ControllerBase
    {
        private readonly ApplicationDbContext _context; // ДОДАЙТЕ ЦЮ ЛІНІЮ
        private readonly IEmailService _emailService;
        private readonly ISlackService _slackService;
        private readonly ILocationService _locationService;
        private readonly IBookingService _bookingService;
        private readonly ISmsService? _smsService;
        private readonly ICalendarService? _calendarService;
        private readonly ILogger<BookingController> _logger;
        private readonly IConfiguration _configuration;

        public BookingController(
            ApplicationDbContext context, // ДОДАЙТЕ ЦЕЙ ПАРАМЕТР
            IEmailService emailService,
            ISlackService slackService,
            ILocationService locationService,
            IBookingService bookingService,
            ILogger<BookingController> logger,
            IConfiguration configuration,
            ISmsService? smsService = null,
            ICalendarService? calendarService = null)
        {
            _context = context; // ДОДАЙТЕ ЦЮ ЛІНІЮ
            _emailService = emailService;
            _slackService = slackService;
            _locationService = locationService;
            _bookingService = bookingService;
            _logger = logger;
            _configuration = configuration;
            _smsService = smsService;
            _calendarService = calendarService;
        }

        [HttpPost("create")]
        public async Task<ActionResult<BookingResponseDto>> CreateBooking([FromBody] CreateBookingDto request)
        {
            try
            {
                _logger.LogInformation($"Creating booking for {request.StudentFirstName} {request.StudentLastName} - {request.CheckRideType}");

                // 1. Створити бронювання
                var bookingId = await _bookingService.CreateBookingAsync(request);

                // 2. Geocode student preferred airport/address
                var coordinates = await _locationService.GeocodeAddressAsync(request.PreferredAirport);
                if (!coordinates.HasValue)
                {
                    _logger.LogWarning($"Unable to geocode airport: {request.PreferredAirport}");
                    await _bookingService.CancelBookingAsync(bookingId, "Unable to geocode preferred airport location");
                    return BadRequest($"Unable to find location for airport: {request.PreferredAirport}");
                }

                _logger.LogInformation($"Geocoded {request.PreferredAirport} to ({coordinates.Value.Latitude}, {coordinates.Value.Longitude})");

                // 3. Find nearby examiners
                var radiusKm = request.SearchRadius * 1.852; // Перетворюємо nautical miles в km
                var nearbyExaminers = await _locationService.FindNearbyExaminersAsync(
                    coordinates.Value.Latitude,
                    coordinates.Value.Longitude,
                    radiusKm,
                    request.CheckRideType);

                if (!nearbyExaminers.Any())
                {
                    _logger.LogWarning($"No examiners found for {request.CheckRideType} within {request.SearchRadius}nm of {request.PreferredAirport}");
                    await _bookingService.CancelBookingAsync(bookingId, $"No qualified examiners found within {request.SearchRadius} nautical miles");
                    return BadRequest($"No qualified examiners found within {request.SearchRadius} nautical miles of {request.PreferredAirport}");
                }

                // 4. Update booking status
                await _bookingService.UpdateBookingStatusAsync(bookingId, Services.BookingStatus.ExaminersContacted);

                // 5. Send Slack notification about new booking
                await _slackService.NotifyNewBookingAsync(
                    $"{request.StudentFirstName} {request.StudentLastName}",
                    request.CheckRideType,
                    request.StartDate ?? DateTime.UtcNow.AddDays(7));

                // 6. Contact examiners in parallel
                var maxExaminers = _configuration.GetValue("ApplicationSettings:MaxExaminersToContact", 3);
                var examinersToContact = nearbyExaminers.Take(maxExaminers).ToList();

                _logger.LogInformation($"Contacting {examinersToContact.Count} examiners for booking {bookingId}");

                var contactTasks = examinersToContact.Select(examiner =>
                    ContactExaminerAsync(examiner, request, bookingId));

                await Task.WhenAll(contactTasks);

                return Ok(new BookingResponseDto
                {
                    BookingId = bookingId,
                    Message = $"Booking request sent to {examinersToContact.Count} qualified examiners",
                    ExaminersContacted = examinersToContact.Select(e => e.Name).ToList(),
                    Status = "ExaminersContacted"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating booking");
                await _slackService.NotifyErrorAsync("Booking creation failed", ex.Message);
                return StatusCode(500, "Internal server error while processing booking request");
            }
        }

        [HttpPost("examiner/respond")]
        public async Task<ActionResult> ExaminerResponse([FromBody] ExaminerResponseDto response)
        {
            try
            {
                _logger.LogInformation($"Processing examiner response: {response.Response} from {response.ExaminerEmail} for booking {response.BookingId}");

                // Validate request
                if (string.IsNullOrEmpty(response.BookingId) ||
                    string.IsNullOrEmpty(response.ExaminerEmail) ||
                    string.IsNullOrEmpty(response.Response))
                {
                    return BadRequest("Missing required fields");
                }

                // Get booking
                var booking = await _bookingService.GetBookingAsync(response.BookingId);
                if (booking == null)
                {
                    _logger.LogWarning($"Booking not found: {response.BookingId}");
                    return NotFound("Booking not found");
                }

                // Check if booking is still available for assignment
                var isAvailable = await _bookingService.IsBookingAvailableAsync(response.BookingId);
                if (!isAvailable)
                {
                    _logger.LogInformation($"Booking {response.BookingId} is no longer available. Current status: {booking.Status}");

                    return Ok(new
                    {
                        message = "Sorry, this booking is no longer available. Another examiner may have already been assigned.",
                        assigned = false,
                        currentStatus = booking.Status.ToString()
                    });
                }

                if (response.Response.Equals("Accepted", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to assign examiner (first YES wins logic)
                    var assigned = await _bookingService.TryAssignExaminerAsync(
                        response.BookingId,
                        response.ExaminerEmail,
                        response.ExaminerName);

                    if (assigned)
                    {
                        // Send confirmation email to student
                        var emailResult = await _emailService.SendStudentConfirmationEmailAsync(
                            response.StudentEmail,
                            response.StudentName,
                            response.ExaminerName,
                            response.ProposedDateTime ?? DateTime.UtcNow.AddDays(7));

                        if (emailResult)
                        {
                            _logger.LogInformation($"✅ Student confirmation email sent successfully");
                        }
                        else
                        {
                            _logger.LogWarning($"❌ Failed to send student confirmation email");
                        }

                        // Send SMS confirmation if available
                        if (_smsService != null && !string.IsNullOrEmpty(booking.StudentEmail))
                        {
                            var studentPhone = await GetStudentPhoneFromBooking(response.BookingId);
                            if (!string.IsNullOrEmpty(studentPhone))
                            {
                                var smsMessage = $"Your checkride is confirmed! " +
                                              $"Examiner: {response.ExaminerName}, " +
                                              $"Date: {response.ProposedDateTime?.ToString("MMM dd, yyyy HH:mm") ?? "TBD"}. " +
                                              $"Check your email for details.";

                                var smsResult = await _smsService.SendSmsAsync(studentPhone, smsMessage);
                                if (smsResult)
                                {
                                    _logger.LogInformation($"✅ SMS confirmation sent to student successfully");
                                }
                                else
                                {
                                    _logger.LogWarning($"❌ Failed to send SMS confirmation to student");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("📱 No phone number available for SMS confirmation");
                            }
                        }

                        // Generate and send calendar invitation
                        if (_calendarService != null && response.ProposedDateTime.HasValue)
                        {
                            var icsContent = _calendarService.GenerateIcsFile(
                                $"Aviation Checkride - {booking.ExamType}",
                                response.ProposedDateTime.Value,
                                response.ProposedDateTime.Value.AddHours(3),
                                "Location TBD - Examiner will provide details",
                                $"Checkride examination with {response.ExaminerName}.\\n" +
                                $"Student: {response.StudentName}\\n" +
                                $"Type: {booking.ExamType}\\n" +
                                $"Please bring all required documents and logbooks.\\n" +
                                $"Contact examiner at: {response.ExaminerEmail}"
                            );

                            await SendCalendarInvitationEmail(
                                response.StudentEmail,
                                response.StudentName,
                                response.ExaminerName,
                                response.ProposedDateTime.Value,
                                icsContent,
                                response.BookingId);

                            _logger.LogInformation($"📅 Calendar invitation processing completed");
                        }

                        // Notify Slack
                        var slackResult = await _slackService.NotifyExaminerResponseAsync(
                            response.ExaminerName,
                            "ACCEPTED (ASSIGNED!)",
                            response.StudentName);

                        if (slackResult)
                        {
                            _logger.LogInformation($"✅ Slack notification sent successfully");
                        }

                        _logger.LogInformation($"Examiner {response.ExaminerName} successfully assigned to booking {response.BookingId}");

                        return Ok(new
                        {
                            message = "Congratulations! You have been assigned to this booking. The student has been notified.",
                            assigned = true,
                            bookingId = response.BookingId,
                            studentName = response.StudentName,
                            scheduledDateTime = response.ProposedDateTime
                        });
                    }
                    else
                    {
                        // Another examiner was faster
                        await _slackService.NotifyExaminerResponseAsync(
                            response.ExaminerName,
                            "ACCEPTED (too late)",
                            response.StudentName);

                        return Ok(new
                        {
                            message = "Sorry, another examiner responded first and has been assigned to this booking.",
                            assigned = false
                        });
                    }
                }
                else if (response.Response.Equals("Declined", StringComparison.OrdinalIgnoreCase))
                {
                    // Log decline
                    await _slackService.NotifyExaminerResponseAsync(
                        response.ExaminerName,
                        "DECLINED",
                        response.StudentName);

                    _logger.LogInformation($"Examiner {response.ExaminerName} declined booking {response.BookingId}");

                    return Ok(new
                    {
                        message = "Thank you for your response. Your decline has been recorded.",
                        assigned = false
                    });
                }
                else
                {
                    return BadRequest("Invalid response. Must be 'Accepted' or 'Declined'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing examiner response for booking {response.BookingId}");
                await _slackService.NotifyErrorAsync("Examiner response processing failed", ex.Message);
                return StatusCode(500, "Internal server error");
            }
        }

        // Helper method to get student phone
        // Замініть метод GetStudentPhoneFromBooking на:
        private async Task<string?> GetStudentPhoneFromBooking(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return null;

                // Використовуємо ServiceProvider для отримання контексту
                using var scope = HttpContext.RequestServices.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var booking = await context.BookingRequests
                    .Where(b => b.Id == id)
                    .Select(b => b.StudentPhone)
                    .FirstOrDefaultAsync();

                return booking;
            }
            catch
            {
                return null;
            }
        }

        // Method for sending calendar invitation email
        private async Task SendCalendarInvitationEmail(
            string studentEmail,
            string studentName,
            string examinerName,
            DateTime scheduledDateTime,
            string icsContent,
            string bookingId)
        {
            try
            {
                var subject = "Calendar Invitation - Your Checkride is Scheduled";

                var body = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; color: white; border-radius: 10px 10px 0 0;'>
                            <h2 style='margin: 0;'>Calendar Invitation</h2>
                        </div>
                        
                        <div style='padding: 20px; background: #f8f9fa; border: 1px solid #dee2e6; border-top: none;'>
                            <p>Hello <strong>{studentName}</strong>,</p>
                            
                            <p>Your checkride has been scheduled!</p>
                            
                            <div style='background: white; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                                <h3 style='color: #28a745; margin-top: 0;'>Appointment Details</h3>
                                <table style='width: 100%;'>
                                    <tr>
                                        <td style='padding: 8px 0;'><strong>Examiner:</strong></td>
                                        <td>{examinerName}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px 0;'><strong>Date & Time:</strong></td>
                                        <td>{scheduledDateTime:dddd, MMMM dd, yyyy at HH:mm}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px 0;'><strong>Duration:</strong></td>
                                        <td>Approximately 3 hours</td>
                                    </tr>
                                </table>
                            </div>
                            
                            <div style='background: #d1ecf1; border: 1px solid #bee5eb; padding: 15px; border-radius: 8px; margin: 20px 0;'>
                                <p style='margin: 0; color: #0c5460;'>
                                    <strong>Calendar File Attached</strong><br>
                                    Download the attached .ics file and double-click it to add the event to your calendar (Outlook, Google Calendar, Apple Calendar, etc.).
                                </p>
                            </div>
                            
                            <div style='background: #fff3cd; border: 1px solid #ffeeba; padding: 15px; border-radius: 8px; margin: 20px 0;'>
                                <h4 style='margin-top: 0; color: #856404;'>Important Reminders:</h4>
                                <ul style='margin: 0; padding-left: 20px; color: #856404;'>
                                    <li>Bring your logbook and all required documents</li>
                                    <li>Ensure your medical certificate is current</li>
                                    <li>Review the Practical Test Standards (PTS/ACS)</li>
                                    <li>Get a good night's rest before the exam</li>
                                    <li>Arrive at least 15 minutes early</li>
                                </ul>
                            </div>
                            
                            <p>If you need to reschedule or have any questions, please contact your examiner directly.</p>
                            
                            <p>Best of luck with your checkride!</p>
                            
                            <div style='margin-top: 30px; padding-top: 20px; border-top: 1px solid #dee2e6; color: #6c757d; font-size: 12px;'>
                                <p>Exam Booking System<br>
                                This is an automated message with your calendar invitation.</p>
                            </div>
                        </div>
                    </div>";

                var success = await _emailService.SendEmailWithAttachmentAsync(
                    studentEmail,
                    subject,
                    body,
                    icsContent,
                    $"checkride_{bookingId}.ics",
                    "text/calendar",
                    "Exam Booking System"
                );

                if (success)
                {
                    _logger.LogInformation($"✅ Calendar invitation email sent successfully to {studentEmail}");
                }
                else
                {
                    _logger.LogWarning($"❌ Failed to send calendar invitation email to {studentEmail}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending calendar invitation email");
            }
        }

        [HttpGet("debug/{bookingId}")]
        public async Task<ActionResult> DebugBooking(string bookingId)
        {
            try
            {
                var booking = await _bookingService.GetBookingAsync(bookingId);
                if (booking == null)
                    return NotFound($"Booking {bookingId} not found");

                var isAvailable = await _bookingService.IsBookingAvailableAsync(bookingId);

                return Ok(new
                {
                    BookingId = booking.BookingId,
                    StudentName = booking.StudentName,
                    Status = booking.Status.ToString(),
                    AssignedExaminerEmail = booking.AssignedExaminerEmail,
                    AssignedExaminerName = booking.AssignedExaminerName,
                    IsAvailable = isAvailable,
                    IsPaid = booking.IsPaid,
                    CreatedAt = booking.CreatedAt,
                    ScheduledDateTime = booking.ScheduledDateTime
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error debugging booking {bookingId}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("reset/{bookingId}")]
        public async Task<ActionResult> ResetBooking(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return BadRequest("Invalid booking ID format");

                if (_bookingService is EntityFrameworkBookingService efService)
                {
                    await efService.ResetBookingForTestingAsync(bookingId);
                    return Ok(new { message = $"Booking {bookingId} reset successfully" });
                }

                return BadRequest("Reset not supported for this booking service");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting booking {bookingId}");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("{bookingId}")]
        public async Task<ActionResult<BookingInfo>> GetBooking(string bookingId)
        {
            try
            {
                var booking = await _bookingService.GetBookingAsync(bookingId);
                if (booking == null)
                    return NotFound($"Booking {bookingId} not found");

                return Ok(booking);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving booking {bookingId}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("active")]
        public async Task<ActionResult<List<BookingInfo>>> GetActiveBookings()
        {
            try
            {
                var bookings = await _bookingService.GetActiveBookingsAsync();
                return Ok(bookings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving active bookings");
                return StatusCode(500, "Internal server error");
            }
        }

        private async Task ContactExaminerAsync(ExaminerLocation examiner, CreateBookingDto request, string bookingId)
        {
            try
            {
                _logger.LogInformation($"Contacting examiner {examiner.Name} ({examiner.Email}) for booking {bookingId}");

                var success = await _emailService.SendExaminerContactEmailAsync(
                    examiner.Email,
                    examiner.Name,
                    $"{request.StudentFirstName} {request.StudentLastName}",
                    request.CheckRideType,
                    request.StartDate ?? DateTime.UtcNow.AddDays(7));

                if (success)
                {
                    _logger.LogInformation($"✅ Successfully contacted examiner {examiner.Name} for booking {bookingId}");
                }
                else
                {
                    _logger.LogWarning($"❌ Failed to contact examiner {examiner.Name} for booking {bookingId}");
                    await _slackService.NotifyErrorAsync(
                        $"Failed to contact examiner",
                        $"Could not send email to {examiner.Name} ({examiner.Email})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error contacting examiner {examiner.Name} for booking {bookingId}");
            }
        }

        [HttpGet("diagnose/{bookingId}")]
        public async Task<ActionResult> DiagnoseBooking(string bookingId)
        {
            try
            {
                if (_bookingService is EntityFrameworkBookingService efService)
                {
                    var diagnosticInfo = await efService.GetBookingDiagnosticInfoAsync(bookingId);
                    var isAvailable = await _bookingService.IsBookingAvailableAsync(bookingId);

                    return Ok(new
                    {
                        diagnostic = diagnosticInfo,
                        isAvailable = isAvailable,
                        canBeAssigned = diagnosticInfo?.AssignedExaminerId == null && isAvailable
                    });
                }

                return BadRequest("Diagnostic not available for this service type");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error diagnosing booking {bookingId}");
                return StatusCode(500, ex.Message);
            }
        }
        [HttpGet("available-for-examiner")]
        public async Task<ActionResult> GetAvailableBookingsForExaminer(
       [FromQuery] string? examinerEmail = null,
       [FromQuery] string? examType = null,
       [FromQuery] string? state = null,
       [FromQuery] DateTime? dateFrom = null,
       [FromQuery] DateTime? dateTo = null)
        {
            try
            {
                _logger.LogInformation($"Filtering bookings: examinerEmail={examinerEmail}, examType={examType}, state={state}, dateFrom={dateFrom}");

                var query = _context.BookingRequests
                    .Where(b => b.Status == Models.BookingStatus.ExaminersContacted ||
                               b.Status == Models.BookingStatus.PaymentConfirmed)
                    .Where(b => b.AssignedExaminerId == null);

                // Filter by exam type
                if (!string.IsNullOrEmpty(examType))
                {
                    query = query.Where(b => b.ExamType.Contains(examType));
                    _logger.LogInformation($"Applied exam type filter: {examType}");
                }

                // Filter by state (from address) - ВИПРАВЛЕНО
                if (!string.IsNullOrEmpty(state))
                {
                    var upperState = state.ToUpper();
                    query = query.Where(b => b.StudentAddress.ToUpper().Contains(upperState));
                    _logger.LogInformation($"Applied state filter: {state}");
                }

                // Filter by date range - ВИПРАВЛЕНО
                if (dateFrom.HasValue)
                {
                    var dateFromUnspecified = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Unspecified);
                    query = query.Where(b => b.PreferredDate >= dateFromUnspecified);
                    _logger.LogInformation($"Applied date from filter: {dateFrom}");
                }
                if (dateTo.HasValue)
                {
                    var dateToUnspecified = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified);
                    query = query.Where(b => b.PreferredDate <= dateToUnspecified);
                    _logger.LogInformation($"Applied date to filter: {dateTo}");
                }

                // Examiner email filter - ГОЛОВНЕ ВИПРАВЛЕННЯ
                if (!string.IsNullOrEmpty(examinerEmail))
                {
                    var examiner = await _context.Examiners
                        .FirstOrDefaultAsync(e => e.Email.ToLower() == examinerEmail.ToLower());

                    _logger.LogInformation($"Looking for examiner with email: {examinerEmail}");

                    if (examiner != null)
                    {
                        _logger.LogInformation($"Found examiner: {examiner.Name} (ID: {examiner.Id})");

                        var respondedBookingIds = await _context.ExaminerResponses
                            .Where(r => r.ExaminerId == examiner.Id)
                            .Select(r => r.BookingRequestId)
                            .ToListAsync();

                        _logger.LogInformation($"Examiner {examiner.Name} responded to {respondedBookingIds.Count} bookings: [{string.Join(", ", respondedBookingIds)}]");

                        if (respondedBookingIds.Any())
                        {
                            query = query.Where(b => !respondedBookingIds.Contains(b.Id));
                            _logger.LogInformation($"Filtered out {respondedBookingIds.Count} bookings examiner already responded to");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"No examiner found with email: {examinerEmail}");
                        // Якщо екзаменатора не знайдено, показуємо всі доступні букінги
                    }
                }

                var totalCount = await query.CountAsync();
                _logger.LogInformation($"Total bookings after all filters: {totalCount}");

                var bookings = await query
                    .OrderBy(b => b.PreferredDate)
                    .Take(20)
                    .Select(b => new
                    {
                        BookingId = $"BK{b.Id:D6}",
                        StudentName = $"{b.StudentFirstName} {b.StudentLastName}",
                        ExamType = b.ExamType,
                        Location = b.StudentAddress,
                        PreferredDate = b.PreferredDate,
                        SpecialRequirements = b.SpecialRequirements,
                        CreatedAt = b.CreatedAt,
                        DaysWaiting = (int)(DateTime.UtcNow - b.CreatedAt).TotalDays
                    })
                    .ToListAsync();

                _logger.LogInformation($"Returning {bookings.Count} bookings");

                return Ok(bookings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available bookings for examiner");
                return StatusCode(500, "Failed to retrieve available bookings");
            }
        }

        [HttpPost("fix/{bookingId}")]
        public async Task<ActionResult> FixBooking(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return BadRequest("Invalid booking ID");

                using var scope = HttpContext.RequestServices.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var booking = await context.BookingRequests.FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                    return NotFound("Booking not found");

                booking.AssignedExaminerId = null;
                booking.Status = Models.BookingStatus.ExaminersContacted;
                booking.ScheduledDate = null;
                booking.ScheduledTime = null;
                booking.UpdatedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Booking reset successfully",
                    status = booking.Status.ToString(),
                    assignedExaminerId = booking.AssignedExaminerId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fixing booking {bookingId}");
                return StatusCode(500, ex.Message);
            }
        }
    }
}