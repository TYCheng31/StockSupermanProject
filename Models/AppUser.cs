namespace LineBotDemo.Models
{
    public class AppUser
    {
        public long Id { get; set; }                  // 對應 app_users.id (PK)
        public string LineUserId { get; set; } = "";  // LINE 使用者唯一 ID
        public string? DisplayName { get; set; }
        public bool IsActive { get; set; } = true;    // follow / unfollow
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // 🔹 新增這個欄位，對應資料庫 app_users.reply_count
        public int ReplyCount { get; set; } = 0;

        // 一個使用者可以擁有多個股票
        public ICollection<UserStock> Stocks { get; set; } = new List<UserStock>();
    }
}
