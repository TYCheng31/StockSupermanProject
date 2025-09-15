namespace LineBotDemo.Models
{
    public class UserStock
    {
        public long Id { get; set; }               // 對應 user_stocks.id
        public long UserId { get; set; }           // 外鍵 -> app_users.id
        public AppUser User { get; set; } = default!;

        public string StockCode { get; set; } = ""; // 股票代號 (例如 2330)
        public string Exchange { get; set; } = "tse"; // 預設上市 tse
        public string? AliasName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

