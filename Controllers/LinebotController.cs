using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;

// ★ 新增：EF Core / DbContext / Models
using Microsoft.EntityFrameworkCore;
using LineBotDemo.Data;
using LineBotDemo.Models;

namespace LineBotDemo.Controllers
{
    [ApiController]
    [Route("callback")]
    public class LineBotController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppDbContext _db; // ★ 新增

        public LineBotController(
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            AppDbContext db // ★ 新增
        )
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _db = db; // ★ 新增
        }

        // 給 LINE 後台 Verify 用
        [HttpGet]
        public IActionResult Get() => Ok("OK");

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            Console.WriteLine($"[Webhook Body] {body}");

            // 驗簽
            var signatureHeader = Request.Headers["x-line-signature"].ToString();
            var channelSecret = _config["Line:ChannelSecret"] ?? string.Empty;
            if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(channelSecret))
                return Unauthorized();

            byte[] signatureBytes;
            try { signatureBytes = Convert.FromBase64String(signatureHeader); }
            catch { return Unauthorized(); }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            if (!CryptographicOperations.FixedTimeEquals(signatureBytes, hash))
                return Unauthorized();

            var accessToken = _config["Line:ChannelAccessToken"] ?? string.Empty;
            var http = _httpClientFactory.CreateClient();

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("events", out var events)) return Ok();

            foreach (var ev in events.EnumerateArray())
            {
                var type = ev.GetProperty("type").GetString();
                // 取 userId（單聊情境）
                string? userId = null;
                if (ev.TryGetProperty("source", out var source) && source.TryGetProperty("userId", out var uidProp))
                    userId = uidProp.GetString();

                // ★★ 1) 使用者封鎖/刪除 → unfollow：把 is_active=false
                if (type == "unfollow" && !string.IsNullOrEmpty(userId))
                {
                    var u = await _db.AppUsers.SingleOrDefaultAsync(x => x.LineUserId == userId);
                    if (u != null)
                    {
                        u.IsActive = false;
                        u.UpdatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync();
                        Console.WriteLine($"[UNFOLLOW] {userId} → is_active=false");
                    }
                    continue;
                }

                // ★★ 2) 使用者加好友 → follow：Upsert 使用者並 is_active=true
                if (type == "follow" && !string.IsNullOrEmpty(userId))
                {
                    string? displayName = null, pictureUrl = null, statusMessage = null;

                    // 取 LINE Profile（可選，但建議）
                    var preq = new HttpRequestMessage(HttpMethod.Get, $"https://api.line.me/v2/bot/profile/{userId}");

                    Console.WriteLine($"LINE INFORMATION:{preq}");

                    preq.Headers.Add("Authorization", $"Bearer {accessToken}");
                    var presp = await http.SendAsync(preq);
                    if (presp.IsSuccessStatusCode)
                    {
                        using var pdoc = JsonDocument.Parse(await presp.Content.ReadAsStringAsync());
                        var root = pdoc.RootElement;
                        displayName   = root.GetProperty("displayName").GetString();
                        pictureUrl    = root.TryGetProperty("pictureUrl", out var pu) ? pu.GetString() : null;
                        statusMessage = root.TryGetProperty("statusMessage", out var sm) ? sm.GetString() : null;
                    }

                    var user = await _db.AppUsers.SingleOrDefaultAsync(x => x.LineUserId == userId);
                    if (user == null)
                    {
                        _db.AppUsers.Add(new AppUser
                        {
                            LineUserId = userId,
                            DisplayName = displayName,
                            PictureUrl = pictureUrl,
                            StatusMessage = statusMessage,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        user.DisplayName = displayName ?? user.DisplayName;
                        user.PictureUrl = pictureUrl ?? user.PictureUrl;
                        user.StatusMessage = statusMessage ?? user.StatusMessage;
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
                        var reqWelcome = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/reply");
                        reqWelcome.Headers.Add("Authorization", $"Bearer {accessToken}");
                        reqWelcome.Content = new StringContent(JsonSerializer.Serialize(payloadWelcome), Encoding.UTF8, "application/json");
                        await http.SendAsync(reqWelcome);
                    }
                    continue;
                }

                // ★★ 3) 文字訊息：沿用你原本的四碼查價（保持不變）
                if (type == "message" &&
                    ev.GetProperty("message").GetProperty("type").GetString() == "text")
                {
                    var replyToken = ev.GetProperty("replyToken").GetString();
                    var userText = ev.GetProperty("message").GetProperty("text").GetString() ?? "";

                    string replyText;
                    if (Regex.IsMatch(userText, @"^\d{4}$"))
                    {
                        var stockCode = userText;
                        var apiUrl = $"http://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_{stockCode}.tw";
                        var response = await http.GetAsync(apiUrl);

                        if (response.IsSuccessStatusCode)
                        {
                            var jsonResponse = await response.Content.ReadAsStringAsync();
                            using var jsonDoc = JsonDocument.Parse(jsonResponse);

                            var nf = jsonDoc.RootElement.GetProperty("msgArray")[0].GetProperty("nf").GetString() ?? "無法取得 'nf' 資料";
                            var at = jsonDoc.RootElement.GetProperty("msgArray")[0].GetProperty("@").GetString() ?? "無法取得 '@' 資料";
                            var z  = jsonDoc.RootElement.GetProperty("msgArray")[0].GetProperty("z").GetString() ?? "無法取得 'z' 資料";

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

                    var payload = new
                    {
                        replyToken,
                        messages = new object[] { new { type = "text", text = replyText } }
                    };

                    var req = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/reply");
                    req.Headers.Add("Authorization", $"Bearer {accessToken}");
                    req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var resp = await http.SendAsync(req);
                    Console.WriteLine($"[ReplyAPI] {resp.StatusCode}");
                }
            }

            return Ok();
        }
    }
}
