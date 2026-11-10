namespace SchoolAdmission.Tests.Infrastructure;

/// <summary>Generates valid 17-digit birth registration numbers that never collide.</summary>
internal static class StudentIds
{
    private static long _counter = 201_526_912_345_000_00;

    public static string Next() => Interlocked.Increment(ref _counter).ToString().PadLeft(17, '0')[..17];

    public static string[] Many(int count) => Enumerable.Range(0, count).Select(_ => Next()).ToArray();
}
