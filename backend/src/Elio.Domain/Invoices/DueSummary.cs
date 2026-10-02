namespace Elio.Domain.Invoices;

public sealed record DueSummary(string DueState, int DaysOverdue)
{
    public static DateOnly BusinessDate(DateTimeOffset instant, string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);

    public static DueSummary Calculate(DateOnly dueDate, DateOnly today, decimal balance)
    {
        if (balance < 0) throw new ArgumentException("Balance cannot be negative.");
        if (balance == 0) return new("Paid", 0);
        var days = today.DayNumber - dueDate.DayNumber;
        return days > 0 ? new("Overdue", days) : days == 0 ? new("DueToday", 0) : days >= -7 ? new("DueSoon", 0) : new("NotDue", 0);
    }
}
