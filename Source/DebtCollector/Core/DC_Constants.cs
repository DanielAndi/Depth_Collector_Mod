namespace DebtCollector
{
    public static class DC_Constants
    {
        // Default settings values
        public const float DEFAULT_INTEREST_RATE = 10f; // 10% (legacy, kept for backward compatibility)
        public const float DEFAULT_INTEREST_RATE_PER_DAY = 0.02f; // 2% per day (0.02 = 2%)
        public const float DEFAULT_LATE_PENALTY_RATE_PER_DAY = 0.01f; // Additional 1% per day when delinquent
        public const float DEFAULT_INTEREST_INTERVAL_DAYS = 3f;
        public const float DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS = 24f;
        public const int DEFAULT_GRACE_MISSED_PAYMENTS = 2;
        public const float DEFAULT_COLLECTIONS_DEADLINE_HOURS = 18f;
        public const int DEFAULT_LOAN_TERM_DAYS = 30; // Loan must be paid off within this many days
        public const float DEFAULT_PRINCIPAL_REDUCTION_PER_PAYMENT = 0.05f; // 5% of original principal per payment
        public const int DEFAULT_MISSED_PAYMENT_FEE = 50; // Fixed silver fee per missed payment checkpoint
        public const int DEFAULT_MIN_SETTLEMENT_DISTANCE = 2;
        public const int DEFAULT_MAX_SETTLEMENT_DISTANCE = 8;
        public const float DEFAULT_TRIBUTE_MULTIPLIER = 1.5f;
        public const float DEFAULT_RAID_STRENGTH_MULTIPLIER = 1.5f;
        public const int DEFAULT_MAX_LOAN_AMOUNT = 10000; // Maximum loan amount allowed (0 = unlimited)

        // Loan tiers
        public static readonly int[] LOAN_TIERS = { 500, 1000, 2000, 5000 };

        // Time constants
        public const int TICKS_PER_HOUR = 2500;
        public const int TICKS_PER_DAY = 60000;

        // Minimum time after raid start before checking if it ended
        public const int MIN_RAID_DURATION_TICKS = 5000; // About 2 in-game hours
    }
}
