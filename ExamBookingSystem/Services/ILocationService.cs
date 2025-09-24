namespace ExamBookingSystem.Services
{
    public class ExaminerLocation
    {
        public int ExaminerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double DistanceKm { get; set; }
        public List<string> Specializations { get; set; } = new();
    }

    public interface ILocationService
    {
        Task<List<ExaminerLocation>> FindNearbyExaminersAsync(double latitude, double longitude, double radiusKm = 50, string? examType = null);
        double CalculateDistance(double lat1, double lon1, double lat2, double lon2);
        Task<(double Latitude, double Longitude)?> GeocodeAddressAsync(string address);
    }
}