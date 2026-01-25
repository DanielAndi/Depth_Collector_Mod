namespace DebtCollector
{
    public enum DebtStatus
    {
        None,       // No active debt
        Current,    // Loan is active and in good standing
        Delinquent, // Missed payment(s) but not yet in collections
        Collections,// Final notice given, raid imminent if unpaid
        LockedOut   // Post-raid, borrowing disabled until tribute paid
    }
}
