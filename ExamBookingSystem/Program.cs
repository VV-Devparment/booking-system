using ExamBookingSystem.Data;
using ExamBookingSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SendGrid;
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Тестуємо з'єднання з БД
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var safeConnection = connectionString?.Contains("Password=") == true
    ? connectionString.Substring(0, connectionString.IndexOf("Password=")) + "Password=***"
    : connectionString;

Console.WriteLine($"Connection string: {safeConnection}");

try
{
    using var testConn = new NpgsqlConnection(connectionString);
    await testConn.OpenAsync();
    Console.WriteLine("Database connection: SUCCESS!");
    testConn.Close();
}
catch (Exception ex)
{
    Console.WriteLine($"Database connection failed: {ex.Message}");
}

// Базові сервіси
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressInferBindingSourcesForParameters = true;
});

// Add session support for admin authentication
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
// Session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddEndpointsApiExplorer();

// Swagger конфігурація
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Exam Booking System API",
        Version = "v1",
        Description = "API for Aviation Checkride Booking System"
    });
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
});

// Entity Framework конфігурація
// Entity Framework конфігурація
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseNetTopologySuite();
    });

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging(true);
        options.EnableDetailedErrors(true);
    }
});

// Memory Cache для геокодування
builder.Services.AddMemoryCache();

// Email сервіс (SendGrid)
builder.Services.AddSingleton<ISendGridClient>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var apiKey = configuration["SendGrid:ApiKey"];

    if (string.IsNullOrEmpty(apiKey) || apiKey == "demo-key-for-testing")
    {
        apiKey = "SG.dummy_key_for_demo";
    }

    return new SendGridClient(apiKey);
});

// HTTP клієнти
builder.Services.AddHttpClient<ISlackService, SlackService>();
builder.Services.AddHttpClient<ILocationService, LocationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Основні сервіси
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISlackService, SlackService>();
builder.Services.AddScoped<IBookingService, EntityFrameworkBookingService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IExaminerRotationService, ExaminerRotationService>();
builder.Services.AddHttpContextAccessor();
// Додайте після інших сервісів:
builder.Services.AddScoped<IStripeService, StripeService>();
// CORS для розробки
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

var app = builder.Build();

// Діагностика запитів (опціонально)
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        Console.WriteLine($"Request: {context.Request.Method} {context.Request.Path}");
        await next();
    });
}

// Налаштування middleware - ВАЖЛИВИЙ ПОРЯДОК!
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Exam Booking System API v1");
        c.RoutePrefix = "swagger";
        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
    });

    app.UseDeveloperExceptionPage();
}

// Перенаправлення HTTPS (якщо потрібно)
// app.UseHttpsRedirection();

// CORS
app.UseCors("AllowAll");

// Статичні файли - КРИТИЧНО ВАЖЛИВИЙ ПОРЯДОК!
app.UseDefaultFiles(); // Має бути ПЕРЕД UseStaticFiles
app.UseStaticFiles();  // Обслуговування файлів з wwwroot

// Routing
app.UseRouting();

// Session
app.UseSession();

// Authorization (якщо буде потрібна)
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Додаткові endpoints
app.MapGet("/", () => Results.Redirect("/index.html"));
app.MapGet("/swagger-redirect", () => Results.Redirect("/swagger"));
app.MapGet("/docs", () => Results.Redirect("/swagger"));

// Health check endpoint
app.MapGet("/health", async (ApplicationDbContext dbContext) =>
{
    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync();
        var examinerCount = canConnect ? await dbContext.Examiners.CountAsync() : 0;
        var bookingCount = canConnect ? await dbContext.BookingRequests.CountAsync() : 0;

        return Results.Ok(new
        {
            Status = canConnect ? "Healthy" : "Database Unavailable",
            Database = canConnect ? "Connected" : "Disconnected",
            Examiners = examinerCount,
            Bookings = bookingCount,
            Timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Health check failed: {ex.Message}");
    }
});

// API info endpoint
app.MapGet("/api", () =>
    "Exam Booking System API is running! " +
    "Go to /swagger for API documentation or /health for system status");

// Database check при старті
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync();
            Console.WriteLine($"Database connection test: {(canConnect ? "SUCCESS" : "FAILED")}");

            if (canConnect)
            {
                // Створюємо таблиці якщо їх немає
                await dbContext.Database.EnsureCreatedAsync();

                // Статистика по таблицях
                var examinerCount = await dbContext.Examiners.CountAsync();
                var bookingCount = await dbContext.BookingRequests.CountAsync();
                var responseCount = await dbContext.ExaminerResponses.CountAsync();
                var logCount = await dbContext.ActionLogs.CountAsync();

                Console.WriteLine("\nDatabase Statistics:");
                Console.WriteLine($"   Examiners: {examinerCount}");
                Console.WriteLine($"   Bookings: {bookingCount}");
                Console.WriteLine($"   Responses: {responseCount}");
                Console.WriteLine($"   Action Logs: {logCount}");

                // Перевіряємо кілька екзаменаторів
                var sampleExaminers = await dbContext.Examiners
                    .Where(e => !string.IsNullOrEmpty(e.Email))
                    .Take(3)
                    .Select(e => new { e.Name, e.Email, e.Address })
                    .ToListAsync();

                if (sampleExaminers.Any())
                {
                    Console.WriteLine("\nSample Examiners:");
                    foreach (var examiner in sampleExaminers)
                    {
                        Console.WriteLine($"   • {examiner.Name} ({examiner.Email})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database error: {ex.Message}");
            Console.WriteLine("Continuing with limited functionality...");
        }
    }
}

// Startup інформація
Console.WriteLine("\n==============================================");
Console.WriteLine("Exam Booking System API started!");
Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");
Console.WriteLine("==============================================\n");

if (app.Environment.IsDevelopment())
{
    Console.WriteLine("Available URLs:");
    Console.WriteLine($"   Web Interface: http://localhost:5082/");
    Console.WriteLine($"   Swagger UI: http://localhost:5082/swagger");
    Console.WriteLine($"   Health check: http://localhost:5082/health");
    Console.WriteLine($"   API endpoint: http://localhost:5082/api");
    Console.WriteLine();
}

// Виводимо інформацію про конфігурацію
Console.WriteLine("Configuration Status:");
Console.WriteLine($"   SendGrid: {(!string.IsNullOrEmpty(builder.Configuration["SendGrid:ApiKey"]) && builder.Configuration["SendGrid:ApiKey"] != "demo-key-for-testing" ? "Configured" : "Demo Mode")}");
Console.WriteLine($"   Slack: {(!string.IsNullOrEmpty(builder.Configuration["Slack:WebhookUrl"]) ? "Configured" : "Not configured")}");
Console.WriteLine($"   Stripe: {(!string.IsNullOrEmpty(builder.Configuration["Stripe:SecretKey"]) ? "Configured" : "Not configured")}");
Console.WriteLine($"   Geocoding APIs:");
Console.WriteLine($"      OpenCage: {(!string.IsNullOrEmpty(builder.Configuration["Geocoding:OpenCage:ApiKey"]) ? "Yes" : "No")}");
Console.WriteLine($"      Google Maps: {(!string.IsNullOrEmpty(builder.Configuration["Geocoding:Google:MapsApiKey"]) ? "Yes" : "No")}");
Console.WriteLine($"      MapBox: {(!string.IsNullOrEmpty(builder.Configuration["Geocoding:MapBox:AccessToken"]) ? "Yes" : "No")}");
Console.WriteLine();

app.Run();