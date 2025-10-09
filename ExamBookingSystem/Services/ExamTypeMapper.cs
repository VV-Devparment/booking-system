namespace ExamBookingSystem.Services
{
    public static class ExamTypeMapper
    {
        private static readonly Dictionary<string, List<string>> ExamTypeToQualifications = new(StringComparer.OrdinalIgnoreCase)
        {
            // Private Pilot
            { "Private Pilot Single Engine", new List<string> { "DPE-PE-ASEL", "DPE-PE", "PE" } },
            { "Private Single Engine", new List<string> { "DPE-PE-ASEL", "DPE-PE", "PE" } },
            { "Private Pilot", new List<string> { "DPE-PE-ASEL", "DPE-PE", "PE" } },
            { "Private", new List<string> { "DPE-PE", "PE" } },
            
            // Instrument Rating
            { "Instrument Rating", new List<string> { "DPE-CIRE-ASEL", "DPE-CIRE", "CIRE", "IR" } },
            { "Instrument", new List<string> { "DPE-CIRE", "CIRE", "IR" } },
            
            // Commercial
            { "Commercial Pilot Single Engine", new List<string> { "DPE-CE-ASEL", "DPE-CE", "CE" } },
            { "Commercial Single Engine", new List<string> { "DPE-CE-ASEL", "DPE-CE", "CE" } },
            { "Commercial Pilot", new List<string> { "DPE-CE", "CE" } },
            { "Commercial", new List<string> { "DPE-CE", "CE" } },
            
            // CFI
            { "Flight Instructor", new List<string> { "DPE-FIE", "DPE-CFI", "CFI", "FIE" } },
            { "CFI", new List<string> { "DPE-FIE", "DPE-CFI", "CFI", "FIE" } },
            { "Certified Flight Instructor", new List<string> { "DPE-FIE", "CFI", "FIE" } },
            
            // CFII
            { "CFII", new List<string> { "DPE-CFII", "CFII" } },
            { "Instrument Instructor", new List<string> { "DPE-CFII", "CFII" } },
            
            // MEI
            { "MEI", new List<string> { "DPE-MEI", "MEI" } },
            { "Multi Engine Instructor", new List<string> { "DPE-MEI", "MEI" } },
            
            // Multi-Engine
            { "Multi Engine", new List<string> { "DPE-ME-AMEL", "DPE-PE-A", "DPE-ME", "ME", "AMEL" } },
            { "MultiEngine", new List<string> { "DPE-ME-AMEL", "DPE-PE-A", "DPE-ME", "ME" } },
            
            // ATP
            { "ATP", new List<string> { "DPE-ATP", "ATP" } },
            { "Airline Transport Pilot", new List<string> { "DPE-ATP", "ATP" } },
            
            // Sport Pilot
            { "Sport Pilot", new List<string> { "DPE-SP", "SP" } },
            { "SportPilot", new List<string> { "DPE-SP", "SP" } }
        };

        public static List<string> GetQualificationsForExamType(string examType)
        {
            if (string.IsNullOrWhiteSpace(examType))
                return new List<string>();

            if (ExamTypeToQualifications.TryGetValue(examType.Trim(), out var qualifications))
            {
                return qualifications;
            }

            // Fallback - шукаємо часткове співпадіння
            foreach (var kvp in ExamTypeToQualifications)
            {
                if (examType.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return new List<string> { examType };
        }

        public static bool HasMatchingQualification(string? examinerQualification, string examType)
        {
            if (string.IsNullOrWhiteSpace(examinerQualification) || string.IsNullOrWhiteSpace(examType))
                return false;

            var requiredQualifications = GetQualificationsForExamType(examType);
            var examinerQualList = examinerQualification
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(q => q.Trim().ToUpper())
                .ToList();

            // Перевіряємо часткові співпадіння
            foreach (var required in requiredQualifications)
            {
                foreach (var examinerQual in examinerQualList)
                {
                    if (examinerQual.Contains(required.ToUpper(), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}