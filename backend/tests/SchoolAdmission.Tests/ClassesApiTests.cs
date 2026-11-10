using System.Net;
using System.Net.Http.Json;
using SchoolAdmission.Tests.Infrastructure;
using Xunit;

namespace SchoolAdmission.Tests;

[Collection(AdmissionCollection.Name)]
public sealed class ClassesApiTests : IDisposable
{
    private readonly HttpClient _client;

    public ClassesApiTests(AdmissionFixture fixture) => _client = fixture.Primary.CreateClient();

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Creating_a_class_returns_it_with_empty_seat_counts()
    {
        var response = await _client.CreateClassAsync("Class 6 - Morning Shift", 120);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ClassDto>(ApiClientExtensions.Json);

        Assert.NotNull(created);
        Assert.StartsWith("c_", created!.Id);
        Assert.Equal("Class 6 - Morning Shift", created.Name);
        Assert.Equal(120, created.TotalSeats);
        Assert.Equal(0, created.HeldSeats);
        Assert.Equal(0, created.ConfirmedSeats);
        Assert.Equal(120, created.AvailableSeats);
        Assert.Equal($"/classes/{created.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Listing_and_getting_a_class_report_the_same_seat_numbers()
    {
        var created = await _client.CreateClassOrThrowAsync("Class 9 - Day Shift", 40);

        var list = await _client.GetFromJsonAsync<List<ClassDto>>("/classes", ApiClientExtensions.Json);
        var single = await _client.GetClassAsync(created.Id);

        Assert.Contains(list!, c => c.Id == created.Id);
        Assert.Equal(created.Id, single.Id);
        Assert.Equal(40, single.TotalSeats);
        Assert.Equal(40, single.AvailableSeats);
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("Class 7", 0)]
    [InlineData("Class 7", -5)]
    public async Task Invalid_class_input_is_rejected_with_400(string name, int seats)
    {
        var response = await _client.CreateClassAsync(name, seats);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorDto>(ApiClientExtensions.Json);
        Assert.False(string.IsNullOrWhiteSpace(error!.Message));
    }

    [Fact]
    public async Task Unknown_class_returns_404()
    {
        var response = await _client.GetAsync("/classes/c_does_not_exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
