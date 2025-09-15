namespace LineBotDemo.Models
{
    public class LineUser
    {
        public long Id { get; set; }
        public string? LineUserId { get; set; } 
        public string? DisplayName { get; set; }
        public DateTime CreatedAt { get; set; }
        public int MessageCount { get; set; }
    }
}
