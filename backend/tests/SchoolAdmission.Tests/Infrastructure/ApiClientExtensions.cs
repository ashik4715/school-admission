using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolAdmission.Tests.Infrastructure;

public static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task<HttpResponseMessage> CreateClassAsync(this HttpClient client, string name, int totalSeats) =>
        client.PostAsJsonAsync("/classes", new { name, totalSeats });

    public static async Task<ClassDto> CreateClassOrThrowAsync(this HttpClient client, string name, int totalSeats)
    {
        var response = await client.CreateClassAsync(name, totalSeats);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClassDto>(Json))!;
    }

    public static Task<HttpResponseMessage> HoldAsync(this HttpClient client, string classId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", studentId);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> ConfirmAsync(this HttpClient client, string holdId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        request.Headers.Add("X-Student-Id", studentId);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CancelAsync(this HttpClient client, string holdId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId}");
        request.Headers.Add("X-Student-Id", studentId);
        return client.SendAsync(request);
    }

    public static async Task<HoldDto> HoldOrThrowAsync(this HttpClient client, string classId, string studentId)
    {
        var response = await client.HoldAsync(classId, studentId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HoldDto>(Json))!;
    }

    public static async Task<ClassDto> GetClassAsync(this HttpClient client, string classId)
    {
        var response = await client.GetAsync($"/classes/{classId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClassDto>(Json))!;
    }

    public static async Task<HoldDto> GetHoldAsync(this HttpClient client, string holdId)
    {
        var response = await client.GetAsync($"/holds/{holdId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HoldDto>(Json))!;
    }

    public static async Task<List<HoldDto>> GetMyHoldsAsync(this HttpClient client, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        request.Headers.Add("X-Student-Id", studentId);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<HoldDto>>(Json))!;
    }

    public static Task<HttpStatusCode> StatusOfAsync(this HttpClient client, string path)
    {
        return client.GetAsync(path).ContinueWith(task => task.Result.StatusCode);
    }
}

public sealed record ClassDto(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);

public sealed record HoldDto(
    string HoldId,
    string ClassId,
    string? ClassName,
    string? StudentId,
    string Status,
    DateTime ExpiresAt);

public sealed record ConfirmDto(string HoldId, string Status);

public sealed record ErrorDto(string Error, string Message);
