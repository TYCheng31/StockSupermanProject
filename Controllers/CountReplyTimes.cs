using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LineBotDemo.Data;    // AppDbContext 在這裡
using LineBotDemo.Models;  // 如果你要直接用 AppUser

namespace LineBotDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CountReplyTimesController : ControllerBase
    {
        private readonly AppDbContext _db;

        public CountReplyTimesController(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 每當回覆一個使用者時，讓 reply_count +1
        /// </summary>
        /// <param name="lineUserId">LINE 使用者 ID</param>
        [HttpPost("reply/{lineUserId}")]
        public async Task<IActionResult> ReplyUser(string lineUserId)
        {
            // ⚡ 原子更新：reply_count = reply_count + 1
            var rows = await _db.AppUsers
                .Where(u => u.LineUserId == lineUserId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.ReplyCount, u => u.ReplyCount + 1));

            if (rows == 0)
            {
                return NotFound(new { message = $"找不到使用者 {lineUserId}" });
            }

            // 查詢最新 reply_count 回傳
            var replyCount = await _db.AppUsers
                .Where(u => u.LineUserId == lineUserId)
                .Select(u => u.ReplyCount)
                .FirstAsync();

            return Ok(new
            {
                lineUserId,
                replyCount
            });
        }
    }
}
