using System.Threading.Tasks;

namespace LineBotDemo.Services
{
    public interface ILineBotService
    {
        Task<string> ReadRequestBodyAsync(Microsoft.AspNetCore.Http.HttpRequest request);
        bool VerifySignature(Microsoft.AspNetCore.Http.HttpRequest request, string body);
        Task HandleUnfollowAsync(string userId);
        Task HandleFollowAsync(System.Text.Json.JsonElement ev, string userId);
        Task HandleMessageAsync(System.Text.Json.JsonElement ev);
    }
}
