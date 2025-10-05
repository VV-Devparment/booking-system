using ExamBookingSystem.Data;
using ExamBookingSystem.Models;
using System.Collections.Concurrent;

namespace ExamBookingSystem.Services
{
    public interface ISettingsService
    {
        int GetBookingFee();
        void SetBookingFee(int fee);
    }

    public class SettingsService : ISettingsService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private int _cachedBookingFee = 100;

        public SettingsService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            LoadBookingFeeFromDb();
        }

        private void LoadBookingFeeFromDb()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var setting = context.SystemSettings.FirstOrDefault(s => s.Key == "BookingFee");
                if (setting != null && int.TryParse(setting.Value, out int fee))
                {
                    _cachedBookingFee = fee;
                }
            }
            catch { }
        }

        public int GetBookingFee() => _cachedBookingFee;

        public void SetBookingFee(int fee)
        {
            if (fee < 1 || fee > 1000)
                throw new ArgumentException("Fee must be between $1 and $1000");

            _cachedBookingFee = fee;

            // Зберігаємо в БД
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var setting = context.SystemSettings.FirstOrDefault(s => s.Key == "BookingFee");
            if (setting == null)
            {
                setting = new SystemSetting { Key = "BookingFee" };
                context.SystemSettings.Add(setting);
            }

            setting.Value = fee.ToString();
            setting.UpdatedAt = DateTime.UtcNow;
            context.SaveChanges();
        }
    }
}