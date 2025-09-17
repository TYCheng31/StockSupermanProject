using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using LineBotDemo.Data;    // AppDbContext
using LineBotDemo.Models;  // AppUser
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

        //=================================================================================================
        //PROMPT示範
        //=================================================================================================
        //查詢股票代號:     2330
        //使用者加入庫存:   加入庫存:2330
        //查詢我的庫存:     我的庫存
        //使用者刪除庫存:   刪除庫存:2330
        //=================================================================================================
        public async Task HandleMessageAsync(JsonElement ev)
        {
            var replyToken = ev.GetProperty("replyToken").GetString();
            var userText = ev.GetProperty("message").GetProperty("text").GetString() ?? "";

            string replyText;

            Console.WriteLine($"[DEBUG] userText: {userText}"); // 確認輸入的訊息是否正確

            //=================================================================================================
            //INPUT:    2330(股票代號)
            //RETURN:   API回傳的指定資訊
            //=================================================================================================
            if (Regex.IsMatch(userText, @"^\d{4,}$"))
            {
                var stockCode = userText.Trim();
                var apiUrl = $"http://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_{stockCode}.tw";
                Console.WriteLine("有GET API");

                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var response = await client.GetAsync(apiUrl);

                    string text = "請輸入正確的股票代碼。";

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("有接收到API");
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(jsonResponse);
                        using var jsonDoc = JsonDocument.Parse(jsonResponse);

                        if (jsonDoc.RootElement.TryGetProperty("msgArray", out var arr) &&
                            arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                        {
                            var item = arr[0];
                            var nf = item.TryGetProperty("n", out var _n) ? _n.GetString() : "（無名稱）";
                            var at = item.TryGetProperty("c", out var _at) ? _at.GetString() : stockCode;
                            var price = item.TryGetProperty("z", out var _p) ? _p.GetString() : "成交價--";
                            var volume = item.TryGetProperty("v", out var _v) ? _v.GetString() : "量--";

                            //
                            var buyer = item.TryGetProperty("a", out var _a) ? _a.GetString() : "買";
                            var seller = item.TryGetProperty("b", out var _b) ? _b.GetString() : "賣";
                            var Bvolume = item.TryGetProperty("f", out var _f) ? _f.GetString() : "買量--";
                            var Svolume = item.TryGetProperty("g", out var _g) ? _g.GetString() : "賣量--";
                            //

                            var time = item.TryGetProperty("%", out var _t) ? _t.GetString() : "時間--";


                            string[] buyerValues = buyer.Split('_', StringSplitOptions.RemoveEmptyEntries);
                            string[] buyerVolumes = Bvolume.Split('_', StringSplitOptions.RemoveEmptyEntries);
                            string[] sellerValues = seller.Split('_', StringSplitOptions.RemoveEmptyEntries);
                            string[] sellerVolumes = Svolume.Split('_', StringSplitOptions.RemoveEmptyEntries);

                            //回傳訊息
                            text = $"{nf} ({at})\n" +
                                $"{time}\n\n" +
                                $"即時價格: {price,13}\n" + //要修改小數問題
                                $"成交量: {volume,15}\n\n" +
                                $"五檔:\n";


                            for (int i = 0; i < 5; i++)
                            {
                                string buyerPrice = double.Parse(buyerValues[i]).ToString("0.00");  
                                string sellerPrice = double.Parse(sellerValues[i]).ToString("0.00");  
                                text += $"{sellerPrice,0} {sellerVolumes[i],5} | {buyerPrice,0} {buyerVolumes[i],5}\n"; 
                            }
                        }
                    }
                    await SendReplyMessageAsync(replyToken, new { replyToken, messages = new object[] { new { type = "text", text } } });
                    return; // 當場送完，直接結束
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 查詢失敗: {ex}");
                    await SendReplyMessageAsync(replyToken, new { replyToken, messages = new object[] { new { type = "text", text = "查詢失敗，請稍後再試。" } } });
                    return;
                }
            }



            //=================================================================================================
            //INPUT:    加入庫存:2330(股票代號)
            //RETURN:   回傳成功加入資訊
            //=================================================================================================
            else if (userText.StartsWith("加入庫存：") || userText.StartsWith("加入庫存:"))
            {
                // 這裡處理中文冒號或英文冒號情況
                userText = userText.Replace("：", ":");  // 把中文冒號替換為英文冒號

                var stockCode = userText.Substring(5).Trim(); // 提取股票代號
                Console.WriteLine($"[DEBUG] 提取的股票代號: {stockCode}");  // 確認提取的股票代號

                if (Regex.IsMatch(stockCode, @"^\d{4,}$")) // 確保股票代號是四位或更多數字
                {
                    replyText = await HandleAddStockAsync(ev, stockCode); // 處理加入庫存邏輯
                }
                else
                {
                    replyText = "股票代號必須是四位或更多數字，請重新輸入。";
                }
            }

            //=================================================================================================
            //INPUT:    我的庫存
            //RETURN:   該使用者曾經加入的庫存
            //=================================================================================================
            else if (userText == "我的庫存")
            {
                // 處理用戶查詢庫存的邏輯
                var userId = ev.GetProperty("source").GetProperty("userId").GetString();
                if (string.IsNullOrEmpty(userId))
                {
                    replyText = "無法識別您的帳號，請再試一次。";
                }
                else
                {
                    // 查詢用戶庫存
                    var user = await _db.AppUsers.SingleOrDefaultAsync(u => u.LineUserId == userId);
                    if (user != null)
                    {
                        // 查找該用戶的所有股票
                        var userStocks = await _db.UserStocks
                            .Where(us => us.UserId == user.Id)
                            .Select(us => us.StockCode)
                            .ToListAsync(); // 提取為列表後，再進行排序

                        // 在客戶端進行數字排序
                        var sortedStocks = userStocks
                            .OrderBy(stockCode => int.TryParse(stockCode, out int result) ? result : int.MaxValue) // 確保數字排序，無效的股票代號排在最後
                            .ToList();

                        if (sortedStocks.Any())
                        {
                            // 如果有股票，回傳庫存
                            replyText = $"您的庫存有以下股票代號：\n{string.Join("\n", sortedStocks)}";
                        }
                        else
                        {
                            // 如果沒有股票
                            replyText = "您的庫存目前沒有任何股票。";
                        }
                    }
                    else
                    {
                        replyText = "無法找到您的帳號，請再試一次。";
                    }
                }
            }

            //=================================================================================================
            //INPUT:    刪除庫存:2330
            //RETURN:   回傳刪除結果
            //=================================================================================================
            else if (userText.StartsWith("刪除庫存：") || userText.StartsWith("刪除庫存:"))
            {
                // 處理刪除庫存的邏輯
                userText = userText.Replace("：", ":");  // 替換中文冒號為英文冒號
                var stockCode = userText.Substring(5).Trim(); // 提取股票代號
                Console.WriteLine($"[DEBUG] 要刪除的股票代號: {stockCode}");

                if (Regex.IsMatch(stockCode, @"^\d{4,}$")) // 確保股票代號是四位或更多數字
                {
                    replyText = await HandleDeleteStockAsync(ev, stockCode); // 處理刪除庫存邏輯
                }
                else
                {
                    replyText = "股票代號必須是四位或更多數字，請重新輸入。";
                }
            }

            //=================================================================================================
            //INPUT:    其他沒有被指定的prompt
            //RETURN:   哈囉，我是你的股票小幫手 📈
            //=================================================================================================
            else
            {
                replyText = "哈囉，我是你的股票小幫手 📈";
            }

            // 發送回覆訊息
            await SendReplyMessageAsync(replyToken, new { replyToken, messages = new object[] { new { type = "text", text = replyText } } });

            // ✅ 回覆成功 → 該使用者 reply_count +1
            var userIdForCount = ev.GetProperty("source").GetProperty("userId").GetString();
            if (!string.IsNullOrEmpty(userIdForCount))
            {
                var userForCount = await _db.AppUsers.SingleOrDefaultAsync(u => u.LineUserId == userIdForCount);
                if (userForCount != null)
                {
                    userForCount.ReplyCount += 1;
                    userForCount.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }
        }


        private async Task<string> HandleAddStockAsync(JsonElement ev, string stockCode)
        {
            var userId = ev.GetProperty("source").GetProperty("userId").GetString();
            if (string.IsNullOrEmpty(userId))
            {
                return "無法識別您的帳號，請再試一次。";
            }

            // 檢查用戶是否存在
            var user = await _db.AppUsers.SingleOrDefaultAsync(u => u.LineUserId == userId);
            if (user == null)
            {
                return "無法找到您的帳號，請再試一次。";
            }

            // 檢查是否已經加入該股票
            var existingStock = await _db.UserStocks
                .SingleOrDefaultAsync(us => us.UserId == user.Id && us.StockCode == stockCode);

            if (existingStock != null)
            {
                return $"您已經加入過股票 {stockCode}。";
            }

            // 如果用戶還沒有加入該股票，新增一筆
            _db.UserStocks.Add(new UserStock
            {
                UserId = user.Id,
                StockCode = stockCode,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return $"成功加入股票 {stockCode} 到庫存！";
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

        private async Task<string> HandleDeleteStockAsync(JsonElement ev, string stockCode)
        {
            var userId = ev.GetProperty("source").GetProperty("userId").GetString();
            if (string.IsNullOrEmpty(userId))
            {
                return "無法識別您的帳號，請再試一次。";
            }

            // 查詢該用戶的股票並刪除
            var user = await _db.AppUsers.SingleOrDefaultAsync(u => u.LineUserId == userId);
            if (user != null)
            {
                // 查找該用戶擁有的股票
                var stockToDelete = await _db.UserStocks
                    .Where(us => us.UserId == user.Id && us.StockCode == stockCode)
                    .FirstOrDefaultAsync();

                if (stockToDelete != null)
                {
                    _db.UserStocks.Remove(stockToDelete);  // 刪除該股票
                    await _db.SaveChangesAsync();
                    return $"成功刪除股票 {stockCode} 從您的庫存！";
                }
                else
                {
                    return $"您的庫存中沒有找到股票 {stockCode}，無法刪除。";
                }
            }
            else
            {
                return "無法找到您的帳號，請再試一次。";
            }
        }
    }
}