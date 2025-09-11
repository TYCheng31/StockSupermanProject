using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;

namespace LineBotDemo.Controllers
{
    [ApiController]
    [Route("callback")]
    public class LineBotController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public LineBotController(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
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
                if (ev.GetProperty("type").GetString() == "message" &&
                    ev.GetProperty("message").GetProperty("type").GetString() == "text")
                {
                    var replyToken = ev.GetProperty("replyToken").GetString();
                    var userText = ev.GetProperty("message").GetProperty("text").GetString() ?? "";

                    // 檢查是否為四位數字
                    string replyText = "";
                    if (Regex.IsMatch(userText, @"^\d{4}$"))
                    {
                        // 使用者輸入的四位數字
                        var stockCode = userText;

                        // 呼叫股票資訊 API 並傳送四位數字
                        var apiUrl = $"http://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_{stockCode}.tw";
                        var response = await http.GetAsync(apiUrl);

                        if (response.IsSuccessStatusCode)
                        {
                            var jsonResponse = await response.Content.ReadAsStringAsync();
                            using var jsonDoc = JsonDocument.Parse(jsonResponse);

                            //TERMINAL print information of the stock
                            //Console.WriteLine($"{jsonDoc.RootElement.ToString()}");

                            //
                            var nf = jsonDoc.RootElement
                                .GetProperty("msgArray")[0]
                                .GetProperty("nf")
                                .GetString() ?? "無法取得 'nf' 資料";
                            
                            var at = jsonDoc.RootElement
                                .GetProperty("msgArray")[0]
                                .GetProperty("@")
                                .GetString() ?? "無法取得 '@' 資料";
                            
                            var oa = jsonDoc.RootElement
                                .GetProperty("msgArray")[0]
                                .GetProperty("oa")
                                .GetString() ?? "無法取得 'oa' 資料";

                            replyText = $"{nf}\n{at}\n{oa}";

                            //replyText = "成功獲得股票資訊";
                        }
                        else
                        {
                            replyText = "無法取得股票資訊，請稍後再試。";
                        }
                    }
                    else
                    {
                        replyText = "哈囉，我是你的股票小幫手 📈"; // 不是四位數字，回傳原本的字
                    }

                    var payload = new
                    {
                        replyToken,
                        messages = new object[] {
                            new { type = "text", text = replyText }
                        }
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
