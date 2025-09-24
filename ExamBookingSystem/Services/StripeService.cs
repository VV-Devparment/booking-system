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
                _logger.LogInformation($"Processing refund for payment intent: {paymentIntentId}");

                var refundOptions = new RefundCreateOptions
                {
                    PaymentIntent = paymentIntentId,
                    Amount = (long)(amount * 100), // Convert to cents
                    Reason = reason switch
                    {
                        "duplicate" => "duplicate",
                        "fraudulent" => "fraudulent",
                        _ => "requested_by_customer"
                    }
                };

                var refundService = new RefundService();
                var refund = await refundService.CreateAsync(refundOptions);

                _logger.LogInformation($"Refund processed successfully. Refund ID: {refund.Id}");
                return refund.Status == "succeeded" || refund.Status == "pending";
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe refund failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing refund");
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