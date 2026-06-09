namespace Atelier.Framework.Context.Validation
{
    public class ContextSizeValidationResult
    {
        public bool IsValid { get; set; }
        public int TotalSizeBytes { get; set; }
        public int MaxAllowedSizeBytes { get; set; }
        public List<string> Violations { get; } = new();

        public override string ToString()
        {
            if (IsValid)
            {
                return $"Valid: {TotalSizeBytes} bytes (max: {MaxAllowedSizeBytes})";
            }

            return $"Invalid: {string.Join("; ", Violations)}";
        }
    }
}
