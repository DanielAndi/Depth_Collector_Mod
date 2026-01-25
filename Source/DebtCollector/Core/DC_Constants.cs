namespace DebtCollector
{
    public static class DC_Constants
    {
        // Default settings values
        public const float DEFAULT_INTEREST_RATE = 10f; // 10%
        public const float DEFAULT_INTEREST_INTERVAL_DAYS = 3f;
        public const float DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS = 24f;
        public const int DEFAULT_GRACE_MISSED_PAYMENTS = 2;
        public const float DEFAULT_COLLECTIONS_DEADLINE_HOURS = 18f;
        public const int DEFAULT_MIN_SETTLEMENT_DISTANCE = 4;
        public const int DEFAULT_MAX_SETTLEMENT_DISTANCE = 15;
        public const float DEFAULT_TRIBUTE_MULTIPLIER = 1.5f;
        public const float DEFAULT_RAID_STRENGTH_MULTIPLIER = 1.5f;

        // Loan tiers
        public static readonly int[] LOAN_TIERS = { 500, 1000, 2000, 5000 };

        // Time constants
        public const int TICKS_PER_HOUR = 2500;
        public const int TICKS_PER_DAY = 60000;

        // Minimum time after raid start before checking if it ended
        public const int MIN_RAID_DURATION_TICKS = 5000; // About 2 in-game hours
    }
}
