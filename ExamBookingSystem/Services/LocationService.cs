using ExamBookingSystem.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ExamBookingSystem.Services
{
    public class LocationService : ILocationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LocationService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);

        public LocationService(
            ApplicationDbContext context,
            ILogger<LocationService> logger,
            HttpClient httpClient,
            IConfiguration configuration,
            IMemoryCache cache)
        {
            _context = context;
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
            _cache = cache;
        }

        public async Task<List<ExaminerLocation>> FindNearbyExaminersAsync(
			double latitude,
			double longitude,
			double radiusKm = 50,
			string? examType = null)
		{
			try
			{
				_logger.LogInformation($"Searching for examiners within {radiusKm}km of ({latitude}, {longitude}), examType: {examType ?? "ANY"}");

				// Отримуємо тільки екзаменаторів які ВRЖЕ МАЮТЬ координати в БД
				var examiners = await _context.Examiners
					.Where(e => e.Latitude != null && e.Longitude != null)
					.Where(e => !string.IsNullOrEmpty(e.Email))
					.ToListAsync();

				if (!examiners.Any())
				{
					_logger.LogWarning("No examiners with coordinates found in database");
					return new List<ExaminerLocation>();
				}

				_logger.LogInformation($"Processing {examiners.Count} examiners with coordinates from database");

				var nearbyExaminers = new List<ExaminerLocation>();

				// Обробляємо тільки тих, хто має координати
				foreach (var examiner in examiners)
				{
					var distance = CalculateDistance(
						latitude, longitude,
						examiner.Latitude!.Value, examiner.Longitude!.Value);

					_logger.LogDebug($"Examiner {examiner.Name}: distance = {distance:F1}km, specializations = {string.Join(", ", examiner.Specializations)}");

					if (distance <= radiusKm)
					{
						// Фільтруємо по типу екзамену якщо вказано
						if (string.IsNullOrEmpty(examType) || examiner.HasSpecialization(examType))
						{
							var examinerLocation = new ExaminerLocation
							{
								ExaminerId = examiner.Id,
								Name = examiner.GetDisplayName(),
								Email = examiner.Email,
								Latitude = examiner.Latitude.Value,
								Longitude = examiner.Longitude.Value,
								DistanceKm = distance,
								Specializations = examiner.Specializations
							};

							nearbyExaminers.Add(examinerLocation);
							_logger.LogDebug($"✅ Added examiner {examiner.Name} ({distance:F1}km)");
						}
						else
						{
							_logger.LogDebug($"❌ Examiner {examiner.Name} skipped - no matching specialization for {examType}");
						}
					}
				}

				// Сортуємо по відстані та повертаємо топ 3
				var result = nearbyExaminers
					.OrderBy(e => e.DistanceKm)
					.Take(3)
					.ToList();

				_logger.LogInformation($"Found {result.Count} nearby examiners within {radiusKm}km");

				foreach (var examiner in result)
				{
					_logger.LogInformation($"  ✅ {examiner.Name} ({examiner.DistanceKm:F1}km) - {string.Join(", ", examiner.Specializations)}");
				}

				return result;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error finding nearby examiners");
				return new List<ExaminerLocation>();
			}
		}

        private async Task<(Models.Examiner examiner, (double, double)? coords)> GeocodeExaminerAsync(Models.Examiner examiner)
        {
            try
            {
                var coords = await GetCoordinatesForAddressAsync(examiner.Address);
                return (examiner, coords);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to geocode address for {examiner.Name}: {examiner.Address}");
                return (examiner, null);
            }
        }

        public async Task<(double Latitude, double Longitude)?> GeocodeAddressAsync(string address)
        {
            return await GetCoordinatesForAddressAsync(address);
        }

        private async Task<(double Latitude, double Longitude)?> GetCoordinatesForAddressAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            // Нормалізуємо адресу для кешування
            var normalizedAddress = address.Trim().ToLowerInvariant();
            var cacheKey = $"geocode:{normalizedAddress}";

            // Перевіряємо кеш
            if (_cache.TryGetValue(cacheKey, out (double lat, double lon)? cachedCoords))
            {
                _logger.LogDebug($"Using cached coordinates for: {address}");
                return cachedCoords;
            }

            try
            {
                var coords = await GeocodeWithProviderAsync(address);

                if (coords.HasValue)
                {
                    // Зберігаємо в кеш
                    _cache.Set(cacheKey, coords, _cacheExpiration);
                    _logger.LogDebug($"Geocoded and cached: {address} -> ({coords.Value.lat}, {coords.Value.lon})");
                }

                return coords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Geocoding failed for address: {address}");
                return null;
            }
        }

        private async Task<(double lat, double lon)?> GeocodeWithProviderAsync(string address)
        {
            // Пробуємо різні провайдери геокодування по черзі

            // 1. OpenCage (рекомендований для production)
            var openCageKey = _configuration["Geocoding:OpenCage:ApiKey"];
            if (!string.IsNullOrEmpty(openCageKey))
            {
                var result = await TryOpenCageGeocoding(address, openCageKey);
                if (result.HasValue) return result;
            }

            // 2. Google Maps Geocoding API
            var googleKey = _configuration["Geocoding:Google:MapsApiKey"];
            if (!string.IsNullOrEmpty(googleKey))
            {
                var result = await TryGoogleGeocoding(address, googleKey);
                if (result.HasValue) return result;
            }

            // 3. MapBox (альтернатива)
			var mapBoxKey = _configuration["Geocoding:MapBox:AccessToken"];
			if (!string.IsNullOrEmpty(mapBoxKey))
			{
				var result = await TryMapBoxGeocoding(address, mapBoxKey);
				if (result.HasValue) return result;
			}

			// ВИДАЛЕНО fallback - повертаємо null
			_logger.LogWarning($"All geocoding providers failed for: {address}. No coordinates available.");
			return null;
        }

        private async Task<(double lat, double lon)?> TryOpenCageGeocoding(string address, string apiKey)
        {
            try
            {
                var url = $"https://api.opencagedata.com/geocode/v1/json?q={Uri.EscapeDataString(address)}&key={apiKey}&limit=1&countrycode=us";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JsonDocument.Parse(json);

                    var results = data.RootElement.GetProperty("results");
                    if (results.GetArrayLength() > 0)
                    {
                        var geometry = results[0].GetProperty("geometry");
                        var lat = geometry.GetProperty("lat").GetDouble();
                        var lng = geometry.GetProperty("lng").GetDouble();

                        _logger.LogDebug($"OpenCage geocoded: {address} -> ({lat}, {lng})");
                        return (lat, lng);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenCage geocoding failed");
            }

            return null;
        }

        private async Task<(double lat, double lon)?> TryGoogleGeocoding(string address, string apiKey)
        {
            try
            {
                var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}&key={apiKey}&region=us";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JsonDocument.Parse(json);

                    var results = data.RootElement.GetProperty("results");
                    if (results.GetArrayLength() > 0)
                    {
                        var location = results[0].GetProperty("geometry").GetProperty("location");
                        var lat = location.GetProperty("lat").GetDouble();
                        var lng = location.GetProperty("lng").GetDouble();

                        _logger.LogDebug($"Google geocoded: {address} -> ({lat}, {lng})");
                        return (lat, lng);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google geocoding failed");
            }

            return null;
        }

        private async Task<(double lat, double lon)?> TryMapBoxGeocoding(string address, string accessToken)
        {
            try
            {
                var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{Uri.EscapeDataString(address)}.json?access_token={accessToken}&country=us&limit=1";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JsonDocument.Parse(json);

                    var features = data.RootElement.GetProperty("features");
                    if (features.GetArrayLength() > 0)
                    {
                        var center = features[0].GetProperty("center");
                        var lng = center[0].GetDouble();
                        var lat = center[1].GetDouble();

                        _logger.LogDebug($"MapBox geocoded: {address} -> ({lat}, {lng})");
                        return (lat, lng);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MapBox geocoding failed");
            }

            return null;
        }

        public double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Earth's radius in kilometers

            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}