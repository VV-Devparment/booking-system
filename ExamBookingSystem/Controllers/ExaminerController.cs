using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamBookingSystem.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

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
                _logger.LogInformation($"=== LOGIN ATTEMPT ===");
                _logger.LogInformation($"Raw input - Email: '{loginDto.Email}', Password: '{loginDto.Password}'");

                var email = loginDto.Email?.Trim().ToLower();
                var password = loginDto.Password?.Trim();

                _logger.LogInformation($"Cleaned - Email: '{email}', Password: '{password}'");

                var examiners = await _context.Examiners.ToListAsync();
                _logger.LogInformation($"Total examiners loaded: {examiners.Count}");

                // Знайдемо Bruce для тесту
                var bruce = examiners.FirstOrDefault(e => e.Name.Contains("Bruce"));
                if (bruce != null)
                {
                    _logger.LogInformation($"Bruce found - Email: '{bruce.Email}', Password: '{bruce.Password}'");
                    _logger.LogInformation($"Comparing: '{bruce.Email.ToLower()}' == '{email}' ? {bruce.Email.ToLower() == email}");
                    _logger.LogInformation($"Password: '{bruce.Password}' == '{password}' ? {bruce.Password == password}");
                }

                var examiner = examiners.FirstOrDefault(e => e.Email.ToLower() == email);

                if (examiner == null)
                {
                    _logger.LogWarning($"No examiner found with email: {email}");
                    _logger.LogInformation($"Available emails: {string.Join(", ", examiners.Take(5).Select(e => e.Email))}");
                    return Ok(new { success = false, message = "Invalid email or password" });
                }

                _logger.LogInformation($"Found examiner: {examiner.Name}");
                _logger.LogInformation($"Password check: DB='{examiner.Password}' Input='{password}' Match={examiner.Password == password}");

                if (examiner.Password != password)
                {
                    return Ok(new { success = false, message = "Invalid email or password" });
                }

                HttpContext.Session.SetString("ExaminerId", examiner.Id.ToString());
                HttpContext.Session.SetString("ExaminerEmail", examiner.Email);
                HttpContext.Session.SetString("ExaminerName", examiner.Name);

                _logger.LogInformation($"LOGIN SUCCESS for {examiner.Name}");

                return Ok(new
                {
                    success = true,
                    examiner = new
                    {
                        id = examiner.Id,
                        name = examiner.Name,
                        email = examiner.Email
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
                return Unauthorized(new { authenticated = false });
            }

            return Ok(new
            {
                authenticated = true,
                examiner = new
                {
                    id = examinerId,
                    email = HttpContext.Session.GetString("ExaminerEmail"),
                    name = HttpContext.Session.GetString("ExaminerName")
                }
            });
        }


        [HttpGet("test")]
        public async Task<ActionResult> Test()
        {
            try
            {
                var examiners = await _context.Examiners.Take(3).ToListAsync();

                return Ok(new
                {
                    count = examiners.Count,
                    data = examiners.Select(e => new
                    {
                        id = e.Id,
                        name = e.Name,
                        email = e.Email,
                        password = e.Password
                    })
                });
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message, stack = ex.StackTrace });
            }
        }
        [HttpGet("test-login")]
        public async Task<ActionResult> TestLogin()
        {
            var targetEmail = "bruce.avian1@gmail.com";

            // Отримаємо всіх екзаменаторів
            var allExaminers = await _context.Examiners.ToListAsync();

            // Знайдемо Bruce
            var bruce = allExaminers.FirstOrDefault(e =>
                e.Name.Contains("Bruce") || e.Email.Contains("bruce"));

            if (bruce == null)
                return Ok(new { message = "Bruce not found in database" });

            return Ok(new
            {
                found = true,
                name = bruce.Name,
                dbEmail = bruce.Email,
                searchEmail = targetEmail,
                emailsMatch = bruce.Email.ToLower() == targetEmail.ToLower(),
                dbPassword = bruce.Password,
                testPassword = "bru5exam",
                passwordMatch = bruce.Password == "bru5exam"
            });
        }
        public class ExaminerLoginDto
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
    }
}   