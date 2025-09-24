using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamBookingSystem.Data;

namespace ExamBookingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DebugController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("data-check")]
        public async Task<ActionResult> GetDataCheck()
        {
            var bookings = await _context.BookingRequests
                .Take(5)
                .Select(b => new
                {
                    Id = b.Id,
                    Status = b.Status.ToString(),
                    ExamType = b.ExamType,
                    StudentAddress = b.StudentAddress,
                    AssignedExaminerId = b.AssignedExaminerId,
                    PreferredDate = b.PreferredDate
                })
                .ToListAsync();

            var statusCounts = await _context.BookingRequests
                .GroupBy(b => b.Status)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToListAsync();

            var sampleExaminers = await _context.Examiners
                .Where(e => !string.IsNullOrEmpty(e.Email))
                .Take(3)
                .Select(e => new { e.Id, e.Email })
                .ToListAsync();

            return Ok(new
            {
                SampleBookings = bookings,
                StatusCounts = statusCounts,
                SampleExaminers = sampleExaminers
            });
        }

        [HttpGet("test-booking-details/{bookingId}")]
        public async Task<ActionResult> TestBookingDetails(string bookingId)
        {
            try
            {
                if (!bookingId.StartsWith("BK") || !int.TryParse(bookingId.Substring(2), out int id))
                    return BadRequest("Invalid booking ID");

                var booking = await _context.BookingRequests
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (booking == null)
                    return NotFound("Booking not found");

                var responses = await _context.ExaminerResponses
                    .Where(r => r.BookingRequestId == id)
                    .ToListAsync();

                var actionLogs = await _context.ActionLogs
                    .Where(a => a.BookingRequestId == id)
                    .ToListAsync();

                return Ok(new
                {
                    BookingExists = booking != null,
                    BookingId = bookingId,
                    ResponsesCount = responses.Count,
                    ActionLogsCount = actionLogs.Count,
                    RawBooking = new
                    {
                        booking.Id,
                        booking.StudentFirstName,
                        booking.StudentLastName,
                        booking.Status
                    },
                    RawResponses = responses.Select(r => new {
                        r.Id,
                        r.ExaminerId,
                        r.Response
                    }),
                    RawActionLogs = actionLogs.Select(a => new {
                        a.Id,
                        a.ActionType,
                        a.Description
                    })
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }
    }
}