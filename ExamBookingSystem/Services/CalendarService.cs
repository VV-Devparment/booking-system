using System.Text;

namespace ExamBookingSystem.Services
{
    public interface ICalendarService
    {
        string GenerateIcsFile(string title, DateTime start, DateTime end, string location, string description);
    }

    public class CalendarService : ICalendarService
    {
        public string GenerateIcsFile(string title, DateTime start, DateTime end, string location, string description)
        {
            var sb = new StringBuilder();

            // Calendar header
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//Exam Booking System//Aviation Checkride//EN");
            sb.AppendLine("METHOD:REQUEST");
            sb.AppendLine("CALSCALE:GREGORIAN");

            // Timezone component for US Eastern Time
            sb.AppendLine("BEGIN:VTIMEZONE");
            sb.AppendLine("TZID:America/New_York");
            sb.AppendLine("BEGIN:STANDARD");
            sb.AppendLine("DTSTART:20231105T020000");
            sb.AppendLine("TZOFFSETFROM:-0400");
            sb.AppendLine("TZOFFSETTO:-0500");
            sb.AppendLine("RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU");
            sb.AppendLine("END:STANDARD");
            sb.AppendLine("BEGIN:DAYLIGHT");
            sb.AppendLine("DTSTART:20240310T020000");
            sb.AppendLine("TZOFFSETFROM:-0500");
            sb.AppendLine("TZOFFSETTO:-0400");
            sb.AppendLine("RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU");
            sb.AppendLine("END:DAYLIGHT");
            sb.AppendLine("END:VTIMEZONE");

            // Event component
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{Guid.NewGuid()}@exambooking.com");
            sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
            sb.AppendLine($"DTSTART;TZID=America/New_York:{start:yyyyMMddTHHmmss}");
            sb.AppendLine($"DTEND;TZID=America/New_York:{end:yyyyMMddTHHmmss}");
            sb.AppendLine($"SUMMARY:{EscapeIcsText(title)}");
            sb.AppendLine($"DESCRIPTION:{EscapeIcsText(description)}");
            sb.AppendLine($"LOCATION:{EscapeIcsText(location)}");
            sb.AppendLine("PRIORITY:1");
            sb.AppendLine("CLASS:PUBLIC");
            sb.AppendLine("STATUS:CONFIRMED");
            sb.AppendLine("TRANSP:OPAQUE");
            sb.AppendLine("SEQUENCE:0");

            // Categories
            sb.AppendLine("CATEGORIES:AVIATION,CHECKRIDE,EXAM");

            // Reminder 1 day before
            sb.AppendLine("BEGIN:VALARM");
            sb.AppendLine("TRIGGER:-P1D");
            sb.AppendLine("ACTION:DISPLAY");
            sb.AppendLine("DESCRIPTION:Checkride tomorrow! Review your materials and get good rest.");
            sb.AppendLine("END:VALARM");

            // Reminder 2 hours before
            sb.AppendLine("BEGIN:VALARM");
            sb.AppendLine("TRIGGER:-PT2H");
            sb.AppendLine("ACTION:DISPLAY");
            sb.AppendLine("DESCRIPTION:Checkride in 2 hours! Gather your documents and head to the airport.");
            sb.AppendLine("END:VALARM");

            // Reminder 30 minutes before
            sb.AppendLine("BEGIN:VALARM");
            sb.AppendLine("TRIGGER:-PT30M");
            sb.AppendLine("ACTION:DISPLAY");
            sb.AppendLine("DESCRIPTION:Checkride in 30 minutes! You should be at the airport now.");
            sb.AppendLine("END:VALARM");

            sb.AppendLine("END:VEVENT");
            sb.AppendLine("END:VCALENDAR");

            return sb.ToString();
        }

        private string EscapeIcsText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            // Escape special characters for ICS format
            return text
                .Replace("\\", "\\\\")
                .Replace(",", "\\,")
                .Replace(";", "\\;")
                .Replace("\n", "\\n")
                .Replace("\r", "");
        }
    }
}