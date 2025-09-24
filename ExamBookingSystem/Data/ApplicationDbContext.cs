using Microsoft.EntityFrameworkCore;
using ExamBookingSystem.Models;

namespace ExamBookingSystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Examiner> Examiners { get; set; }
        public DbSet<BookingRequest> BookingRequests { get; set; }
        public DbSet<ExaminerResponse> ExaminerResponses { get; set; }
        public DbSet<ActionLog> ActionLogs { get; set; }
        public DbSet<Administrator> Administrators { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Конфігурація для Examiner
            modelBuilder.Entity<Examiner>(entity =>
            {
                entity.ToTable("Examiners");
                entity.HasKey(e => e.Id);

                // Ігноруємо calculated properties (оскільки вони вже з [NotMapped])
                entity.Ignore(e => e.FirstName);
                entity.Ignore(e => e.LastName);
                entity.Ignore(e => e.PhoneNumber);
                entity.Ignore(e => e.Specializations);
                entity.Ignore(e => e.Latitude);
                entity.Ignore(e => e.Longitude);
                entity.Ignore(e => e.DistanceKm);
                entity.Ignore(e => e.Responses);

                // Індекси для оптимізації
                entity.HasIndex(e => e.Email);
                entity.HasIndex(e => e.FSDO);
            });

            // BookingRequest конфігурація
            modelBuilder.Entity<BookingRequest>(entity =>
            {
                entity.HasIndex(e => e.StudentEmail);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
                entity.Property(e => e.Amount).HasPrecision(10, 2);

                // Зв'язок з екзаменатором (може бути null)
                entity.HasOne(e => e.AssignedExaminer)
                    .WithMany()
                    .HasForeignKey(e => e.AssignedExaminerId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Administrator>(entity =>
            {
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.Phone);
            });

            // ExaminerResponse конфігурація
            modelBuilder.Entity<ExaminerResponse>(entity =>
            {
                entity.HasIndex(e => e.Response);
                entity.HasIndex(e => e.ContactedAt);
                entity.HasIndex(e => new { e.BookingRequestId, e.ExaminerId })
                    .IsUnique(); // Один екзаменатор може відповісти тільки один раз на бронювання

                entity.HasOne(e => e.BookingRequest)
                    .WithMany(b => b.ExaminerResponses)
                    .HasForeignKey(e => e.BookingRequestId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Тимчасово відключаємо зв'язок з Examiner через проблеми з моделлю
                entity.Ignore(e => e.Examiner);
            });

            // ActionLog конфігурація
            modelBuilder.Entity<ActionLog>(entity =>
            {
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.ActionType);

                entity.HasOne(e => e.BookingRequest)
                    .WithMany(b => b.ActionLogs)
                    .HasForeignKey(e => e.BookingRequestId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Тимчасово відключаємо зв'язок з Examiner
                entity.Ignore(e => e.Examiner);
            });
        }

        // Helper методи для роботи з екзаменаторами
        public async Task<List<Examiner>> GetActiveExaminersAsync()
        {
            return await Examiners
                .Where(e => !string.IsNullOrEmpty(e.Email) && !string.IsNullOrEmpty(e.Address))
                .ToListAsync();
        }

        public async Task<List<Examiner>> GetExaminersBySpecializationAsync(string examType)
        {
            return await Examiners
                .Where(e => !string.IsNullOrEmpty(e.Email) &&
                           !string.IsNullOrEmpty(e.Address) &&
                           !string.IsNullOrEmpty(e.Qualification))
                .ToListAsync();
            // Фільтрацію по спеціалізації робимо в коді, бо це calculated property
        }

        public async Task<Examiner?> GetExaminerByEmailAsync(string email)
        {
            return await Examiners
                .FirstOrDefaultAsync(e => e.Email.ToLower() == email.ToLower());
        }

    }
}