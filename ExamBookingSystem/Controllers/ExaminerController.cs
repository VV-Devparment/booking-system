using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamBookingSystem.Data;

namespace ExamBookingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExaminerController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ExaminerController> _logger;

        public ExaminerController(ApplicationDbContext context, ILogger<ExaminerController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] ExaminerLoginDto loginDto)
        {
            try
            {
                _logger.LogInformation($"Login attempt with login: {loginDto.Username}");

                var login = loginDto.Username?.Trim().ToLower();
                var password = loginDto.Password?.Trim();

                // Шукаємо по login полю
                var examiner = await _context.Examiners
                    .FirstOrDefaultAsync(e => e.Login != null && e.Login.ToLower() == login);

                if (examiner == null)
                {
                    _logger.LogWarning($"No examiner found with login: {login}");
                    return Ok(new { success = false, message = "Invalid login or password" });
                }

                _logger.LogInformation($"Found examiner: {examiner.Name}");

                if (examiner.Password != password)
                {
                    _logger.LogWarning($"Invalid password for login: {login}");
                    return Ok(new { success = false, message = "Invalid login or password" });
                }

                HttpContext.Session.SetString("ExaminerId", examiner.Id.ToString());
                HttpContext.Session.SetString("ExaminerEmail", examiner.Email ?? "");
                HttpContext.Session.SetString("ExaminerName", examiner.Name);
                HttpContext.Session.SetString("ExaminerLogin", examiner.Login ?? "");

                _logger.LogInformation($"Login successful for: {examiner.Name}");

                return Ok(new
                {
                    success = true,
                    examiner = new
                    {
                        id = examiner.Id,
                        name = examiner.Name,
                        email = examiner.Email ?? "",
                        login = examiner.Login
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("logout")]
        public ActionResult Logout()
        {
            HttpContext.Session.Clear();
            return Ok(new { success = true });
        }

        [HttpGet("check-auth")]
        public ActionResult CheckAuth()
        {
            var examinerId = HttpContext.Session.GetString("ExaminerId");
            if (string.IsNullOrEmpty(examinerId))
            {
                return Ok(new { authenticated = false });
            }

            return Ok(new
            {
                authenticated = true,
                examiner = new
                {
                    id = examinerId,
                    email = HttpContext.Session.GetString("ExaminerEmail"),
                    name = HttpContext.Session.GetString("ExaminerName"),
                    login = HttpContext.Session.GetString("ExaminerLogin")
                }
            });
        }
    }

    // DTO клас
    public class ExaminerLoginDto
    {
        public string Username { get; set; } = string.Empty;  // Username для сумісності з frontend
        public string Password { get; set; } = string.Empty;
    }
}