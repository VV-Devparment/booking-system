using Microsoft.AspNetCore.Mvc;

namespace ExamBookingSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConfigController> _logger;

        public ConfigController(IConfiguration configuration, ILogger<ConfigController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("stripe-publishable-key")]
        public ActionResult<object> GetStripePublishableKey()
        {
            var publishableKey = _configuration["Stripe:PublishableKey"];
            
            if (string.IsNullOrEmpty(publishableKey))
            {
                _logger.LogError("Stripe publishable key not configured");
                return BadRequest(new { error = "Stripe publishable key not configured" });
            }

            return Ok(new { publishableKey = publishableKey });
        }
    }
}
