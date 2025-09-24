using ExamBookingSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamBookingSystem.Services
{
    public interface IExaminerRotationService
    {
        Task CheckAndRotateExaminers();
    }

    public class ExaminerRotationService : IExaminerRotationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBookingService _bookingService;
        private readonly ILocationService _locationService;
        private readonly IEmailService _emailService;
        private readonly ISlackService _slackService;
        private readonly ILogger<ExaminerRotationService> _logger;
        private readonly IConfiguration _configuration;

        public ExaminerRotationService(
            ApplicationDbContext context,
            IBookingService bookingService,
            ILocationService locationService,
            IEmailService emailService,
            ISlackService slackService,
            ILogger<ExaminerRotationService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _bookingService = bookingService;
            _locationService = locationService;
            _emailService = emailService;
            _slackService = slackService;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task CheckAndRotateExaminers()
        {
            try
            {
                _logger.LogInformation("Starting examiner rotation check");

                var timeoutHours = _configuration.GetValue("ApplicationSettings:ExaminerResponseTimeoutHours", 24);
                var cutoffTime = DateTime.UtcNow.AddHours(-timeoutHours);

                // Знаходимо бронювання, які потребують ротації
                var bookingsToRotate = await _context.BookingRequests
                    .Where(b => b.Status == Models.BookingStatus.ExaminersContacted)
                    .Where(b => b.CreatedAt < cutoffTime)
                    .Where(b => b.AssignedExaminerId == null) // Ще не призначений
                    .Include(b => b.ExaminerResponses)
                    .ToListAsync();

                if (!bookingsToRotate.Any())
                {
                    _logger.LogInformation("No bookings require examiner rotation");
                    return;
                }

                _logger.LogInformation($"Found {bookingsToRotate.Count} bookings that need examiner rotation");

                foreach (var booking in bookingsToRotate)
                {
                    await RotateExaminersForBooking(booking);
                }

                _logger.LogInformation("Completed examiner rotation check");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during examiner rotation check");
                await _slackService.NotifyErrorAsync("Examiner rotation failed", ex.Message);
            }
        }

        private async Task RotateExaminersForBooking(Models.BookingRequest booking)
        {
            try
            {
                var bookingId = $"BK{booking.Id:D6}";
                _logger.LogInformation($"Rotating examiners for booking {bookingId}");

                // Отримуємо список екзаменаторів, які вже були контактовані
                var contactedExaminerIds = booking.ExaminerResponses
                    .Select(r => r.ExaminerId)
                    .ToList();

                // Знаходимо координати студента (використовуємо StudentAddress як airport)
                var coordinates = await _locationService.GeocodeAddressAsync(booking.StudentAddress);
                if (!coordinates.HasValue)
                {
                    _logger.LogWarning($"Cannot rotate examiners for booking {bookingId} - unable to geocode address");
                    return;
                }

                // Розширюємо радіус пошуку (збільшуємо на 50%)
                var expandedRadiusKm = 75; // Збільшений радіус для ротації

                // Знаходимо всіх екзаменаторів в розширеному радіусі
                var allNearbyExaminers = await _locationService.FindNearbyExaminersAsync(
                    coordinates.Value.Latitude,
                    coordinates.Value.Longitude,
                    expandedRadiusKm,
                    booking.ExamType);

                // Фільтруємо тих, кого ще не контактували
                var newExaminers = allNearbyExaminers
                    .Where(e => !contactedExaminerIds.Contains(e.ExaminerId))
                    .Take(3)
                    .ToList();

                if (!newExaminers.Any())
                {
                    _logger.LogWarning($"No new examiners found for booking {bookingId} rotation");

                    // Повідомляємо адміністраторів про проблему
                    await _slackService.SendNotificationAsync(
                        $"⚠️ No more examiners available for booking {bookingId} " +
                        $"({booking.StudentFirstName} {booking.StudentLastName} - {booking.ExamType}). " +
                        $"Manual intervention may be required.");

                    return;
                }

                _logger.LogInformation($"Found {newExaminers.Count} new examiners for booking {bookingId}");

                // Контактуємо з новими екзаменаторами
                var contactTasks = newExaminers.Select(examiner =>
                    ContactExaminerForRotation(examiner, booking, bookingId));

                await Task.WhenAll(contactTasks);

                // Створюємо записи про контакт з новими екзаменаторами
                foreach (var examiner in newExaminers)
                {
                    var examinerResponse = new Models.ExaminerResponse
                    {
                        BookingRequestId = booking.Id,
                        ExaminerId = examiner.ExaminerId,
                        Response = Models.ResponseType.NoResponse,
                        ContactedAt = DateTime.UtcNow
                    };

                    _context.ExaminerResponses.Add(examinerResponse);
                }

                // Зберігаємо зміни
                await _context.SaveChangesAsync();

                // Повідомляємо про ротацію
                await _slackService.SendNotificationAsync(
                    $"🔄 Rotated examiners for booking {bookingId} " +
                    $"({booking.StudentFirstName} {booking.StudentLastName}). " +
                    $"Contacted {newExaminers.Count} additional examiners.");

                _logger.LogInformation($"Successfully rotated examiners for booking {bookingId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error rotating examiners for booking BK{booking.Id:D6}");
            }
        }

        private async Task ContactExaminerForRotation(ExaminerLocation examiner, Models.BookingRequest booking, string bookingId)
        {
            try
            {
                _logger.LogInformation($"Contacting examiner {examiner.Name} for booking {bookingId} rotation");

                var success = await _emailService.SendExaminerContactEmailAsync(
                    examiner.Email,
                    examiner.Name,
                    $"{booking.StudentFirstName} {booking.StudentLastName}",
                    booking.ExamType,
                    booking.PreferredDate);

                if (success)
                {
                    _logger.LogInformation($"Successfully contacted examiner {examiner.Name} for booking {bookingId} rotation");
                }
                else
                {
                    _logger.LogWarning($"Failed to contact examiner {examiner.Name} for booking {bookingId} rotation");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error contacting examiner {examiner.Name} for rotation");
            }
        }
    }
}