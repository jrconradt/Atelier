namespace Atelier.Framework.Context
{
    public class ScopeLimiterSummary
    {
        public int AllowedDataKeysCount { get; set; }
        public int BlockedDataKeysCount { get; set; }
        public int AllowedOperationsCount { get; set; }
        public int BlockedOperationsCount { get; set; }
        public int AllowedScopesCount { get; set; }
        public int BlockedScopesCount { get; set; }
    }
}
