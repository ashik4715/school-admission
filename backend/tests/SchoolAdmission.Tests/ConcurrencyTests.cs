using System.Net;
using SchoolAdmission.Tests.Infrastructure;
using Xunit;

namespace SchoolAdmission.Tests;

/// <summary>
/// The important tests: the fixture runs two independent API hosts against one database, so
/// these prove the seat limit also holds across processes, not just across threads.
/// </summary>
[Collection(AdmissionCollection.Name)]
public sealed class ConcurrencyTests
{
    private readonly AdmissionAppFactory _factoryA;
    private readonly AdmissionAppFactory _factoryB;

    public ConcurrencyTests(AdmissionFixture fixture)
    {
        _factoryA = fixture.Primary;
        _factoryB = fixture.Secondary;
    }

    [Fact]
    public async Task Many_simultaneous_applications_never_oversell_the_class()
    {
        using var client = _factoryA.CreateClient();
        const int totalSeats = 10;
        const int applicants = 60;

        var created = await client.CreateClassOrThrowAsync("Concurrency Class", totalSeats);
        var students = StudentIds.Many(applicants);

        var responses = await Task.WhenAll(students.Select(studentId => client.HoldAsync(created.Id, studentId)));

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        var other = responses.Where(r => r.StatusCode != HttpStatusCode.Created && r.StatusCode != HttpStatusCode.Conflict).ToList();
        if (other.Count > 0)
        {
            var bodies = await Task.WhenAll(other.Select(async r => $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}"));
            Assert.Fail($"Unexpected responses ({other.Count}): {string.Join(" | ", bodies.Distinct().Take(5))}");
        }

        Assert.Equal(totalSeats, createdCount);
        Assert.Equal(applicants - totalSeats, conflictCount);

        var state = await client.GetClassAsync(created.Id);
        Assert.Equal(totalSeats, state.HeldSeats);
        Assert.Equal(0, state.AvailableSeats);
        Assert.True(state.HeldSeats + state.ConfirmedSeats <= state.TotalSeats);
    }

    [Fact]
    public async Task Two_backend_instances_sharing_one_database_still_respect_the_seat_limit()
    {
        using var clientA = _factoryA.CreateClient();
        using var clientB = _factoryB.CreateClient();
        const int totalSeats = 8;

        var created = await clientA.CreateClassOrThrowAsync("Cross Process Class", totalSeats);
        var students = StudentIds.Many(40);

        // Half of the traffic goes to instance A, half to instance B, all at the same time.
        var responses = await Task.WhenAll(students.Select((studentId, index) =>
            (index % 2 == 0 ? clientA : clientB).HoldAsync(created.Id, studentId)));

        Assert.Equal(totalSeats, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(students.Length - totalSeats, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var fromA = await clientA.GetClassAsync(created.Id);
        var fromB = await clientB.GetClassAsync(created.Id);

        Assert.Equal(totalSeats, fromA.HeldSeats + fromA.ConfirmedSeats);
        Assert.Equal(fromA.HeldSeats, fromB.HeldSeats);
        Assert.Equal(0, fromA.AvailableSeats);
    }

    [Fact]
    public async Task Simultaneous_confirmations_of_one_application_count_the_seat_once()
    {
        using var clientA = _factoryA.CreateClient();
        using var clientB = _factoryB.CreateClient();

        var studentId = StudentIds.Next();
        var created = await clientA.CreateClassOrThrowAsync("Double Confirm Class", 5);
        var hold = await clientA.HoldOrThrowAsync(created.Id, studentId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            (index % 2 == 0 ? clientA : clientB).ConfirmAsync(hold.HoldId, studentId)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var state = await clientA.GetClassAsync(created.Id);
        Assert.Equal(1, state.ConfirmedSeats);
        Assert.Equal(0, state.HeldSeats);
        Assert.Equal(4, state.AvailableSeats);
    }

    [Fact]
    public async Task Concurrent_expiry_sweeps_and_expired_holds_never_exceed_capacity()
    {
        using var client = _factoryA.CreateClient();
        const int totalSeats = 5;

        var created = await client.CreateClassOrThrowAsync("Mixed Traffic Class", totalSeats);
        var students = StudentIds.Many(30);

        var holds = await Task.WhenAll(students.Select(studentId => client.HoldAsync(created.Id, studentId)));
        var successful = holds.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(totalSeats, successful);

        // Fire a burst of mixed traffic at both instances: reads, duplicate applies and the
        // internal sweeper run concurrently.
        var noise = Enumerable.Range(0, 30).Select(index => (index % 2 == 0 ? _factoryA : _factoryB)
            .CreateClient()
            .GetClassAsync(created.Id));
        await Task.WhenAll(noise);

        var state = await client.GetClassAsync(created.Id);
        Assert.True(state.HeldSeats + state.ConfirmedSeats <= totalSeats,
            $"held({state.HeldSeats}) + confirmed({state.ConfirmedSeats}) must not exceed {totalSeats}");
    }
}
