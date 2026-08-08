using System;

namespace NewDesk.Services;

public interface IClock
{
    DateTime Now { get; }
}

public class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime Now => DateTime.Now;
}

public class TestClock : IClock
{
    public DateTime FixedTime { get; set; }
    public DateTime Now => FixedTime;

    public TestClock(DateTime fixedTime)
    {
        FixedTime = fixedTime;
    }
}
