using ExamBookingSystem.DTOs;
using ExamBookingSystem.Data;
using ExamBookingSystem.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ExamBookingSystem.Services
{
    public class EntityFrameworkBookingService : IBookingService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EntityFrameworkBookingService> _logger;

        public EntityFrameworkBookingService(
            ApplicationDbContext context,
            ILogger<EntityFrameworkBookingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public class BookingDiagnosticInfo
        {
            public string? BookingId { get; set; }
            public string? Status { get; set; }
            public int? AssignedExaminerId { get; set; }
            public string? AssignedExaminerName { get; set; }
            public bool IsPaid { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
            public int ResponseCount { get; set; }
            public int AcceptedResponses { get; set; }
            public int DeclinedResponses { get; set; }
            public string? Error { get; set; }
        }

        public async Task<string> CreateBookingAsync(CreateBookingDto request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var bookingRequest = new BookingRequest
                {
                    StudentFirstName = request.StudentFirstName,
                    StudentLastName = request.StudentLastName,
                    StudentEmail = request.StudentEmail,
                    StudentPhone = request.StudentPhone,
                    StudentAddress = request.PreferredAirport,
                    ExamType = request.CheckRideType,
                    PreferredDate = DateTime.SpecifyKind(request.StartDate ?? DateTime.Now.AddDays(7), DateTimeKind.Unspecified),
                    PreferredTime = "10:00",
                    SpecialRequirements = request.AdditionalNotes,
                    Status = Models.BookingStatus.Created,
                    Amount = 100.00m,
                    Currency = "USD",
                    IsPaid = false,
                    CreatedAt = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified),
                    UpdatedAt = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified),
                    Latitude = 0,
                    Longitude = 0
                };

                _context.BookingRequests.Add(bookingRequest);
                await _context.SaveChangesAsync();

                var bookingId = $"BK{bookingRequest.Id:D6}";

                await AddActionLogAsync(bookingRequest.Id, null, ActionType.BookingCreated,
                    $"Booking created for {request.StudentFirstName} {request.StudentLastName}",
                    JsonSerializer.Serialize(new
                    {
                        ExamType = request.CheckRideType,
                        PreferredAirport = request.PreferredAirport,
                        SearchRadius = request.SearchRadius
                    }));

                await transaction.CommitAsync();

                _logger.LogInformation($"Booking created: {bookingId} for {bookingRequest.StudentFirstName} {bookingRequest.StudentLastName}");

                return bookingId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create booking");
                throw;
            }
        }

        public async Task<BookingInfo?> GetBookingAsync(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                {
                    _logger.LogWarning($"Invalid booking ID format: {bookingId}");
                    return null;
                }

                var booking = await _context.BookingRequests
                    .Include(b => b.AssignedExaminer)
                    .Include(b => b.ExaminerResponses)
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (booking == null)
                    return null;

                return MapToBookingInfo(booking);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting booking {bookingId}");
                return null;
            }
        }

        public async Task<bool> TryAssignExaminerAsync(string bookingId, string examinerEmail, string examinerName)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                {
                    _logger.LogWarning($"Invalid booking ID format: {bookingId}");
                    return false;
                }

                var booking = await _context.BookingRequests
                    .Where(b => b.Id == id)
                    .FirstOrDefaultAsync();

                if (booking == null)
                {
                    _logger.LogWarning($"Booking not found: {bookingId}");
                    await transaction.RollbackAsync();
                    return false;
                }

                if (booking.AssignedExaminerId != null ||
                    booking.Status == Models.BookingStatus.ExaminerAssigned ||
                    booking.Status == Models.BookingStatus.Scheduled)
                {
                    _logger.LogInformation($"Booking {bookingId} already assigned to examiner ID: {booking.AssignedExaminerId}");
                    await transaction.RollbackAsync();
                    return false;
                }

                if (booking.Status != Models.BookingStatus.Created &&
                    booking.Status != Models.BookingStatus.PaymentConfirmed &&
                    booking.Status != Models.BookingStatus.ExaminersContacted)
                {
                    _logger.LogWarning($"Booking {bookingId} has invalid status for assignment: {booking.Status}");
                    await transaction.RollbackAsync();
                    return false;
                }

                var examiner = await _context.Examiners
                    .FirstOrDefaultAsync(e => e.Email.ToLower() == examinerEmail.ToLower());

                if (examiner == null)
                {
                    _logger.LogWarning($"Examiner not found with email: {examinerEmail}. Creating temporary record.");

                    examiner = new Examiner
                    {
                        Name = examinerName,
                        Email = examinerEmail,
                        Address = "TBD",
                        Phone = "TBD"
                    };
                    _context.Examiners.Add(examiner);
                    await _context.SaveChangesAsync();
                }

                booking.AssignedExaminerId = examiner.Id;
                booking.Status = Models.BookingStatus.ExaminerAssigned;
                booking.ScheduledDate = booking.PreferredDate;
                booking.ScheduledTime = booking.PreferredTime ?? "10:00";
                booking.UpdatedAt = DateTime.UtcNow;

                var existingResponse = await _context.ExaminerResponses
                    .FirstOrDefaultAsync(r => r.BookingRequestId == booking.Id && r.ExaminerId == examiner.Id);

                if (existingResponse == null)
                {
                    var examinerResponse = new ExaminerResponse
                    {
                        BookingRequestId = booking.Id,
                        ExaminerId = examiner.Id,
                        Response = ResponseType.Accepted,
                        ContactedAt = DateTime.UtcNow,
                        RespondedAt = DateTime.UtcNow,
                        ResponseMessage = "Accepted via API",
                        IsWinner = true
                    };
                    _context.ExaminerResponses.Add(examinerResponse);
                }
                else
                {
                    existingResponse.Response = ResponseType.Accepted;
                    existingResponse.RespondedAt = DateTime.UtcNow;
                    existingResponse.IsWinner = true;
                }

                await AddActionLogAsync(booking.Id, examiner.Id, ActionType.ExaminerAssigned,
                    $"Examiner {examinerName} assigned to booking",
                    JsonSerializer.Serialize(new { ExaminerEmail = examinerEmail, ExaminerName = examinerName }));

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation($"SUCCESS: Examiner {examinerName} ({examinerEmail}) assigned to booking {bookingId}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error assigning examiner to booking {bookingId}");
                return false;
            }
        }

        public async Task<bool> IsBookingAvailableAsync(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                {
                    _logger.LogWarning($"Invalid booking ID format: {bookingId}");
                    return false;
                }

                var booking = await _context.BookingRequests
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (booking == null)
                {
                    _logger.LogWarning($"Booking not found: {bookingId}");
                    return false;
                }

                var isAvailable = booking.AssignedExaminerId == null &&
                                  (booking.Status == Models.BookingStatus.Created ||
                                   booking.Status == Models.BookingStatus.PaymentPending ||
                                   booking.Status == Models.BookingStatus.PaymentConfirmed ||
                                   booking.Status == Models.BookingStatus.ExaminersContacted);

                _logger.LogInformation($"Booking {bookingId} availability check: " +
                    $"Status={booking.Status}, " +
                    $"AssignedExaminerId={booking.AssignedExaminerId}, " +
                    $"IsAvailable={isAvailable}");

                return isAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking availability for booking {bookingId}");
                return false;
            }
        }

        public async Task<List<BookingInfo>> GetActiveBookingsAsync()
        {
            try
            {
                var bookings = await _context.BookingRequests
                    .Include(b => b.AssignedExaminer)
                    .Where(b => b.Status != Models.BookingStatus.Completed &&
                               b.Status != Models.BookingStatus.Cancelled)
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(50)
                    .ToListAsync();

                return bookings.Select(MapToBookingInfo).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active bookings");
                return new List<BookingInfo>();
            }
        }

        public async Task<bool> CancelBookingAsync(string bookingId, string reason)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return false;

                var booking = await _context.BookingRequests.FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                    return false;

                booking.Status = Models.BookingStatus.Cancelled;
                booking.UpdatedAt = DateTime.UtcNow;

                await AddActionLogAsync(booking.Id, null, ActionType.BookingCancelled,
                    $"Booking cancelled: {reason}",
                    JsonSerializer.Serialize(new { Reason = reason }));

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Booking {bookingId} cancelled. Reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling booking {bookingId}");
                return false;
            }
        }

        public async Task<bool> UpdateBookingStatusAsync(string bookingId, Services.BookingStatus status)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return false;

                var booking = await _context.BookingRequests.FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                    return false;

                var oldStatus = booking.Status;
                booking.Status = MapToModelStatus(status);
                booking.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Booking {bookingId} status updated: {oldStatus} -> {booking.Status}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating booking {bookingId} status");
                return false;
            }
        }

        public async Task<bool> UpdatePaymentStatusAsync(string bookingId, bool isPaid, string? paymentIntentId = null)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return false;

                var booking = await _context.BookingRequests.FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                    return false;

                booking.IsPaid = isPaid;
                booking.PaymentIntentId = paymentIntentId;
                booking.Status = isPaid ? Models.BookingStatus.PaymentConfirmed : Models.BookingStatus.PaymentPending;
                booking.UpdatedAt = DateTime.UtcNow;

                await AddActionLogAsync(booking.Id, null, ActionType.PaymentConfirmed,
                    $"Payment {(isPaid ? "confirmed" : "initiated")}",
                    JsonSerializer.Serialize(new { PaymentIntentId = paymentIntentId, IsPaid = isPaid }));

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Booking {bookingId} payment status updated: {isPaid}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating payment status for booking {bookingId}");
                return false;
            }
        }

        public async Task<bool> ResetBookingForTestingAsync(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return false;

                var booking = await _context.BookingRequests.FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                    return false;

                booking.AssignedExaminerId = null;
                booking.Status = Models.BookingStatus.ExaminersContacted;
                booking.ScheduledDate = null;
                booking.ScheduledTime = null;
                booking.UpdatedAt = DateTime.UtcNow;

                var responses = await _context.ExaminerResponses
                    .Where(r => r.BookingRequestId == booking.Id)
                    .ToListAsync();
                _context.ExaminerResponses.RemoveRange(responses);

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Booking {bookingId} reset for testing purposes");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting booking {bookingId}");
                return false;
            }
        }

        public async Task<BookingDiagnosticInfo> GetBookingDiagnosticInfoAsync(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                {
                    return new BookingDiagnosticInfo { Error = "Invalid booking ID format" };
                }

                var booking = await _context.BookingRequests
                    .Include(b => b.AssignedExaminer)
                    .Include(b => b.ExaminerResponses)
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (booking == null)
                {
                    return new BookingDiagnosticInfo { Error = "Booking not found" };
                }

                return new BookingDiagnosticInfo
                {
                    BookingId = bookingId,
                    Status = booking.Status.ToString(),
                    AssignedExaminerId = booking.AssignedExaminerId,
                    AssignedExaminerName = booking.AssignedExaminer?.Name,
                    IsPaid = booking.IsPaid,
                    CreatedAt = booking.CreatedAt,
                    UpdatedAt = booking.UpdatedAt,
                    ResponseCount = booking.ExaminerResponses.Count,
                    AcceptedResponses = booking.ExaminerResponses.Count(r => r.Response == ResponseType.Accepted),
                    DeclinedResponses = booking.ExaminerResponses.Count(r => r.Response == ResponseType.Declined)
                };
            }
            catch (Exception ex)
            {
                return new BookingDiagnosticInfo { Error = ex.Message };
            }
        }

        private async Task AddActionLogAsync(int bookingRequestId, int? examinerId, ActionType actionType, string description, string? details = null)
        {
            var actionLog = new ActionLog
            {
                BookingRequestId = bookingRequestId,
                ExaminerId = examinerId,
                ActionType = actionType,
                Description = description,
                Details = details,
                CreatedAt = DateTime.UtcNow
            };

            _context.ActionLogs.Add(actionLog);
        }

        private BookingInfo MapToBookingInfo(BookingRequest booking)
        {
            var bookingId = $"BK{booking.Id:D6}";

            return new BookingInfo
            {
                BookingId = bookingId,
                StudentName = $"{booking.StudentFirstName} {booking.StudentLastName}",
                StudentEmail = booking.StudentEmail,
                ExamType = booking.ExamType,
                PreferredDate = booking.PreferredDate,
                Status = MapToServiceStatus(booking.Status),
                AssignedExaminerEmail = booking.AssignedExaminer?.Email,
                AssignedExaminerName = booking.AssignedExaminer?.Name,
                ScheduledDateTime = booking.ScheduledDate,
                CreatedAt = booking.CreatedAt,
                IsPaid = booking.IsPaid
            };
        }

        private Models.BookingStatus MapToModelStatus(Services.BookingStatus serviceStatus)
        {
            return serviceStatus switch
            {
                Services.BookingStatus.Created => Models.BookingStatus.Created,
                Services.BookingStatus.ExaminersContacted => Models.BookingStatus.ExaminersContacted,
                Services.BookingStatus.ExaminerAssigned => Models.BookingStatus.ExaminerAssigned,
                Services.BookingStatus.Scheduled => Models.BookingStatus.Scheduled,
                Services.BookingStatus.Completed => Models.BookingStatus.Completed,
                Services.BookingStatus.Cancelled => Models.BookingStatus.Cancelled,
                _ => Models.BookingStatus.Created
            };
        }

        private Services.BookingStatus MapToServiceStatus(Models.BookingStatus modelStatus)
        {
            return modelStatus switch
            {
                Models.BookingStatus.Created => Services.BookingStatus.Created,
                Models.BookingStatus.PaymentPending => Services.BookingStatus.Created,
                Models.BookingStatus.PaymentConfirmed => Services.BookingStatus.ExaminersContacted,
                Models.BookingStatus.ExaminersContacted => Services.BookingStatus.ExaminersContacted,
                Models.BookingStatus.ExaminerAssigned => Services.BookingStatus.ExaminerAssigned,
                Models.BookingStatus.Scheduled => Services.BookingStatus.Scheduled,
                Models.BookingStatus.Completed => Services.BookingStatus.Completed,
                Models.BookingStatus.Cancelled => Services.BookingStatus.Cancelled,
                Models.BookingStatus.Refunded => Services.BookingStatus.Cancelled,
                _ => Services.BookingStatus.Created
            };
        }
    }
}