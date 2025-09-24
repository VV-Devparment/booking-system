using ExamBookingSystem.DTOs;
using System.Collections.Concurrent;

namespace ExamBookingSystem.Services
{
    public class BookingService : IBookingService
    {
        private readonly ConcurrentDictionary<string, BookingInfo> _bookings = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _bookingLocks = new();
        private readonly ILogger<BookingService> _logger;

        public BookingService(ILogger<BookingService> logger)
        {
            _logger = logger;
        }

        public async Task<string> CreateBookingAsync(CreateBookingDto request)
        {
            var bookingId = Guid.NewGuid().ToString();

            var booking = new BookingInfo
            {
                BookingId = bookingId,
                StudentName = $"{request.StudentFirstName} {request.StudentLastName}",
                StudentEmail = request.StudentEmail,
                ExamType = request.ExamType,
                PreferredDate = request.PreferredDate,
                Status = BookingStatus.Created
            };

            _bookings.TryAdd(bookingId, booking);
            _bookingLocks.TryAdd(bookingId, new SemaphoreSlim(1, 1));

            _logger.LogInformation($"Booking created: {bookingId} for {booking.StudentName}");

            return await Task.FromResult(bookingId);
        }

        public async Task<BookingInfo?> GetBookingAsync(string bookingId)
        {
            _bookings.TryGetValue(bookingId, out var booking);
            return await Task.FromResult(booking);
        }

        public async Task<bool> TryAssignExaminerAsync(string bookingId, string examinerEmail, string examinerName)
        {
            if (!_bookings.TryGetValue(bookingId, out var booking))
            {
                _logger.LogWarning($"Booking not found: {bookingId}");
                return false;
            }

            if (!_bookingLocks.TryGetValue(bookingId, out var semaphore))
            {
                _logger.LogWarning($"Lock not found for booking: {bookingId}");
                return false;
            }

            await semaphore.WaitAsync();
            try
            {
                if (booking.Status == BookingStatus.ExaminerAssigned)
                {
                    _logger.LogInformation($"Booking {bookingId} already assigned to {booking.AssignedExaminerName}");
                    return false;
                }

                booking.AssignedExaminerEmail = examinerEmail;
                booking.AssignedExaminerName = examinerName;
                booking.Status = BookingStatus.ExaminerAssigned;
                booking.ScheduledDateTime = booking.PreferredDate;

                _logger.LogInformation($"Examiner {examinerName} assigned to booking {bookingId}");

                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<bool> IsBookingAvailableAsync(string bookingId)
        {
            if (!_bookings.TryGetValue(bookingId, out var booking))
                return false;

            var isAvailable = booking.Status == BookingStatus.Created ||
                              booking.Status == BookingStatus.ExaminersContacted;

            _logger.LogInformation($"Booking {bookingId} availability check: Status={booking.Status}, Available={isAvailable}");

            return await Task.FromResult(isAvailable);
        }

        public async Task<List<BookingInfo>> GetActiveBookingsAsync()
        {
            var activeBookings = _bookings.Values
                .Where(b => b.Status != BookingStatus.Completed && b.Status != BookingStatus.Cancelled)
                .OrderByDescending(b => b.CreatedAt)
                .ToList();

            return await Task.FromResult(activeBookings);
        }

        public async Task<bool> CancelBookingAsync(string bookingId, string reason)
        {
            if (!_bookings.TryGetValue(bookingId, out var booking))
                return false;

            if (!_bookingLocks.TryGetValue(bookingId, out var semaphore))
                return false;

            await semaphore.WaitAsync();
            try
            {
                booking.Status = BookingStatus.Cancelled;
                _logger.LogInformation($"Booking {bookingId} cancelled. Reason: {reason}");
                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }

        // Новий метод для сумісності
        public async Task<bool> UpdateBookingStatusAsync(string bookingId, BookingStatus status)
        {
            if (!_bookings.TryGetValue(bookingId, out var booking))
                return false;

            if (!_bookingLocks.TryGetValue(bookingId, out var semaphore))
                return false;

            await semaphore.WaitAsync();
            try
            {
                var oldStatus = booking.Status;
                booking.Status = status;

                _logger.LogInformation($"Booking {bookingId} status updated: {oldStatus} -> {status}");
                return true;
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}