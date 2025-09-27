using Stripe;
using Stripe.Checkout;

namespace ExamBookingSystem.Services
{
    public interface IStripeService
    {
        Task<string> CreateCheckoutSessionAsync(string customerEmail, string bookingId, decimal amount);
        Task<bool> ProcessRefundAsync(string paymentIntentId, decimal amount, string reason);
        Task<Session> GetSessionAsync(string sessionId);
        Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId);
    }

    public class StripeService : IStripeService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripeService> _logger;

        public StripeService(IConfiguration configuration, ILogger<StripeService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
        }

        public async Task<string> CreateCheckoutSessionAsync(string customerEmail, string bookingId, decimal amount)
        {
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
                                Description = $"Booking ID: {bookingId}"
                            },
                            UnitAmount = (long)(amount * 100),
                        },
                        Quantity = 1,
                    }
                },
                Mode = "payment",
                CustomerEmail = customerEmail,
                Metadata = new Dictionary<string, string>
                {
                    {"bookingId", bookingId}
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return session.Id;
        }

        public async Task<bool> ProcessRefundAsync(string paymentIntentId, decimal amount, string reason)
        {
            try
            {
                _logger.LogInformation($"Processing Stripe refund for payment intent: {paymentIntentId}, amount: ${amount}");

                // Перевіряємо чи PaymentIntent існує
                var paymentIntentService = new PaymentIntentService();
                var paymentIntent = await paymentIntentService.GetAsync(paymentIntentId);

                if (paymentIntent == null)
                {
                    _logger.LogError($"Payment intent not found: {paymentIntentId}");
                    return false;
                }

                // Створюємо refund
                var refundService = new RefundService();
                var refundOptions = new RefundCreateOptions
                {
                    PaymentIntent = paymentIntentId,
                    Amount = (long)(amount * 100), // Stripe працює в центах
                    Reason = reason switch
                    {
                        "No examiner available" => "requested_by_customer",
                        "duplicate" => "duplicate",
                        "fraudulent" => "fraudulent",
                        _ => "requested_by_customer"
                    },
                    Metadata = new Dictionary<string, string>
            {
                { "refund_reason", reason },
                { "refund_date", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
            }
                };

                var refund = await refundService.CreateAsync(refundOptions);

                _logger.LogInformation($"Refund created - ID: {refund.Id}, Status: {refund.Status}, Amount: ${refund.Amount / 100}");

                // Перевіряємо статус
                if (refund.Status == "succeeded")
                {
                    _logger.LogInformation($"✅ Refund succeeded immediately");
                    return true;
                }
                else if (refund.Status == "pending")
                {
                    _logger.LogInformation($"⏳ Refund pending, will be processed by Stripe");
                    return true;
                }
                else if (refund.Status == "failed")
                {
                    _logger.LogError($"❌ Refund failed: {refund.FailureReason}");
                    return false;
                }

                return false;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe API error during refund: {ex.StripeError?.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during refund processing");
                return false;
            }
        }

        public async Task<Session> GetSessionAsync(string sessionId)
        {
            var service = new SessionService();
            return await service.GetAsync(sessionId);
        }

        public async Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId)
        {
            var service = new PaymentIntentService();
            return await service.GetAsync(paymentIntentId);
        }
    }
}