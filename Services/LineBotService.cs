using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using LineBotDemo.Data;    // AppDbContext
using LineBotDemo.Models;  // AppUser
using Microsoft.Extensions.Configuration;

namespace LineBotDemo.Services
{
    public class LineBotService : ILineBotService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppDbContext _db;

        public LineBotService(IConfiguration config, IHttpClientFactory httpClientFactory, AppDbContext db)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _db = db;
        }

        public async Task<string> ReadRequestBodyAsync(Microsoft.AspNetCore.Http.HttpRequest request)
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        public bool VerifySignature(Microsoft.AspNetCore.Http.HttpRequest request, string body)
        {
            var signatureHeader = request.Headers["x-line-signature"].ToString();
            var channelSecret = _config["Line:ChannelSecret"];
            if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(channelSecret)) return false;

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signatureHeader);
            }
            catch
            {
                return false;
            }

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(signatureBytes, hash);
        }

        public async Task HandleUnfollowAsync(string userId)
        {
            var user = await _db.AppUsers.SingleOrDefaultAsync(x => x.LineUserId == userId);
            if (user != null)
            {
                user.IsActive = false;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                Console.WriteLine($"[UNFOLLOW] {userId} → is_active=false");
            }
        }

        public async Task HandleFollowAsync(JsonElement ev, string userId)
        {
            string? displayName = null;
            var accessToken = _config["Line:ChannelAccessToken"];
            var http = _httpClientFactory.CreateClient();
            var preq = new HttpRequestMessage(HttpMethod.Get, $"https://api.line.me/v2/bot/profile/{userId}");
            preq.Headers.Add("Authorization", $"Bearer {accessToken}");
            var presp = await http.SendAsync(preq);

            if (presp.IsSuccessStatusCode)
            {
                using var pdoc = JsonDocument.Parse(await presp.Content.ReadAsStringAsync());
                displayName = pdoc.RootElement.GetProperty("displayName").GetString();
            }

            var user = await _db.AppUsers.SingleOrDefaultAsync(x => x.LineUserId == userId);
            if (user == null)
            {
                _db.AppUsers.Add(new AppUser
                {
                    LineUserId = userId,
                    DisplayName = displayName,
                    IsActive = true,
                    ReplyCount = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                user.DisplayName = displayName ?? user.DisplayName;
                user.IsActive = true;
                user.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            Console.WriteLine($"[FOLLOW] Upsert {userId} → is_active=true");

            // 回覆歡迎訊息（可選）
            if (ev.TryGetProperty("replyToken", out var rt))
            {
                var payloadWelcome = new
                {
                    replyToken = rt.GetString(),
                    messages = new object[] {
                        new { type = "text", text = $"歡迎加入！{displayName ?? ""}\n輸入四碼股票代號（例：2330）可查價。" }
                    }
                };
                await SendReplyMessageAsync(rt.GetString(), payloadWelcome);
            }
        }

        public async Task HandleMessageAsync(JsonElement ev)
        {
            var replyToken = ev.GetProperty("replyToken").GetString();
            var userText = ev.GetProperty("message").GetProperty("text").GetString() ?? "";

            string replyText;
            if (Regex.IsMatch(userText, @"^\d{4}$"))
            {
                var stockCode = userText;
                var apiUrl = $"http://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_{stockCode}.tw";
                var response = await _httpClientFactory.CreateClient().GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    using var jsonDoc = JsonDocument.Parse(jsonResponse);

                    var nf = jsonDoc.RootElement.GetProperty("msgArray")[0].GetProperty("nf").GetString() ?? "無法取得 'nf' 資料";
                    var at = jsonDoc.RootElement.GetProperty("msgArray")[0].GetProperty("@").GetString() ?? "無法取得 '@' 資料";
                    var z = jsonDoc.RootElement.GetProperty("msgArray")[0].GetProperty("z").GetString() ?? "無法取得 'z' 資料";

                    replyText = $"{nf}\n{at}\n{z}";
                }
                else
                {
                    replyText = "無法取得股票資訊，請稍後再試。";
                }
            }
            else
            {
                replyText = "哈囉，我是你的股票小幫手 📈";
            }

            // 發送回覆訊息
            await SendReplyMessageAsync(replyToken, new { replyToken, messages = new object[] { new { type = "text", text = replyText } } });

            // ✅ 回覆成功 → 該使用者 reply_count +1
            var userId = ev.GetProperty("source").GetProperty("userId").GetString();
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _db.AppUsers.SingleOrDefaultAsync(u => u.LineUserId == userId);
                if (user != null)
                {
                    user.ReplyCount += 1;
                    user.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    //Console.WriteLine($"[REPLY] {userId} → reply_count={user.ReplyCount}");
                }
            }
        }

        private async Task SendReplyMessageAsync(string replyToken, object payload)
        {
            var accessToken = _config["Line:ChannelAccessToken"];
            var http = _httpClientFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/reply")
            {
                Headers = { { "Authorization", $"Bearer {accessToken}" } },
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            await http.SendAsync(req);
        }
    }
}
