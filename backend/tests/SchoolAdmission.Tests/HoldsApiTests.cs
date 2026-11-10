using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using SchoolAdmission.Tests.Infrastructure;
using Xunit;

namespace SchoolAdmission.Tests;

[Collection(AdmissionCollection.Name)]
public sealed class HoldsApiTests : IDisposable
{
    private readonly AdmissionFixture _fixture;
    private readonly HttpClient _client;

    public HoldsApiTests(AdmissionFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Primary.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Applying_holds_a_seat_and_updates_the_class_counters()
    {
        var studentId = StudentIds.Next();
        var created = await _client.CreateClassOrThrowAsync("Class 8 - Morning Shift", 2);

        var response = await _client.HoldAsync(created.Id, studentId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var hold = await response.Content.ReadFromJsonAsync<HoldDto>(ApiClientExtensions.Json);
        Assert.Equal("HELD", hold!.Status);
        Assert.Equal(created.Id, hold.ClassId);
        Assert.True(hold.ExpiresAt > DateTime.UtcNow);

        var after = await _client.GetClassAsync(created.Id);
        Assert.Equal(1, after.HeldSeats);
        Assert.Equal(1, after.AvailableSeats);

        var mine = await _client.GetMyHoldsAsync(studentId);
        Assert.Single(mine);
        Assert.Equal("Class 8 - Morning Shift", mine[0].ClassName);
    }

    [Fact]
    public async Task A_class_can_never_be_overbooked()
    {
        var created = await _client.CreateClassOrThrowAsync("Full Class", 1);

        var first = await _client.HoldAsync(created.Id, StudentIds.Next());
        var second = await _client.HoldAsync(created.Id, StudentIds.Next());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var error = await second.Content.ReadFromJsonAsync<ErrorDto>(ApiClientExtensions.Json);
        Assert.Equal("NO_SEATS_AVAILABLE", error!.Error);

        var state = await _client.GetClassAsync(created.Id);
        Assert.Equal(1, state.HeldSeats);
        Assert.Equal(0, state.AvailableSeats);
    }

    [Fact]
    public async Task A_student_cannot_hold_two_seats_or_two_applications_in_one_class()
    {
        var studentId = StudentIds.Next();
        var created = await _client.CreateClassOrThrowAsync("Class 5 - Day Shift", 10);

        var first = await _client.HoldAsync(created.Id, studentId);
        var second = await _client.HoldAsync(created.Id, studentId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var state = await _client.GetClassAsync(created.Id);
        Assert.Equal(1, state.HeldSeats);
    }

    [Fact]
    public async Task Confirming_is_idempotent_and_moves_the_seat_to_confirmed_only_once()
    {
        var studentId = StudentIds.Next();
        var created = await _client.CreateClassOrThrowAsync("Class 10 - Morning Shift", 3);
        var hold = await _client.HoldOrThrowAsync(created.Id, studentId);

        var first = await _client.ConfirmAsync(hold.HoldId, studentId);
        var second = await _client.ConfirmAsync(hold.HoldId, studentId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("CONFIRMED", (await second.Content.ReadFromJsonAsync<ConfirmDto>(ApiClientExtensions.Json))!.Status);

        var state = await _client.GetClassAsync(created.Id);
        Assert.Equal(1, state.ConfirmedSeats);
        Assert.Equal(0, state.HeldSeats);
        Assert.Equal(2, state.AvailableSeats);

        // A confirmed seat is final for the class: the student cannot apply again.
        var reapply = await _client.HoldAsync(created.Id, studentId);
        Assert.Equal(HttpStatusCode.Conflict, reapply.StatusCode);
    }

    [Fact]
    public async Task Cancelling_frees_the_seat_for_someone_else_and_only_the_owner_may_cancel()
    {
        var owner = StudentIds.Next();
        var other = StudentIds.Next();
        var created = await _client.CreateClassOrThrowAsync("Class 4 - Morning Shift", 1);
        var hold = await _client.HoldOrThrowAsync(created.Id, owner);

        var blocked = await _client.HoldAsync(created.Id, other);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var foreignCancel = await _client.CancelAsync(hold.HoldId, other);
        Assert.Equal(HttpStatusCode.NotFound, foreignCancel.StatusCode);

        var cancel = await _client.CancelAsync(hold.HoldId, owner);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        var state = await _client.GetClassAsync(created.Id);
        Assert.Equal(0, state.HeldSeats);
        Assert.Equal(1, state.AvailableSeats);

        var retry = await _client.HoldAsync(created.Id, other);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task Expired_holds_release_their_seat_without_any_client_call()
    {
        var shortFactory = _fixture.CreateFactory(holdDurationSeconds: 1);
        using var client = shortFactory.CreateClient();

        var studentId = StudentIds.Next();
        var created = await client.CreateClassOrThrowAsync("Short Window Class", 1);
        var hold = await client.HoldOrThrowAsync(created!.Id, studentId);

        Assert.Equal("HELD", hold.Status);

        // Nobody calls the API during this wait: only the background sweeper runs.
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            var status = await connection.QuerySingleAsync<string>(
                "SELECT status FROM holds WHERE id = @id", new { id = hold.HoldId });
            Assert.Equal("EXPIRED", status);
        }

        var state = await client.GetClassAsync(created.Id);
        Assert.Equal(0, state.HeldSeats);
        Assert.Equal(0, state.ConfirmedSeats);
        Assert.Equal(1, state.AvailableSeats);

        // The application can no longer be confirmed, and the seat can be taken by someone else.
        var confirm = await client.ConfirmAsync(hold.HoldId, studentId);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);

        var someoneElse = await client.HoldAsync(created.Id, StudentIds.Next());
        Assert.Equal(HttpStatusCode.Created, someoneElse.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("2015269123456789X")]
    public async Task A_missing_or_malformed_student_id_is_rejected(string? studentId)
    {
        var created = await _client.CreateClassOrThrowAsync("Header Check Class", 5);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{created.Id}/holds");
        if (studentId is not null) request.Headers.Add("X-Student-Id", studentId);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
