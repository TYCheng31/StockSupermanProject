using Microsoft.AspNetCore.Mvc;
using LineBotDemo.Services; // 引入服務層
using System.Text.Json;  


namespace LineBotDemo.Controllers
{
    [ApiController]
    [Route("callback")]
    public class LineBotController : ControllerBase
    {
        private readonly ILineBotService _lineBotService;

        public LineBotController(ILineBotService lineBotService)
        {
            _lineBotService = lineBotService;
        }

        // 給 LINE 後台 Verify 用
        [HttpGet]
        public IActionResult Get() => Ok("OK");

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            // 讀取 request body
            var body = await _lineBotService.ReadRequestBodyAsync(Request);

            // 驗證簽名
            if (!_lineBotService.VerifySignature(Request, body))
                return Unauthorized();

            var doc = JsonDocument.Parse(body);
            var events = doc.RootElement.GetProperty("events").EnumerateArray();

            foreach (var ev in events)
            {
                var type = ev.GetProperty("type").GetString();
                var userId = ev.GetProperty("source").GetProperty("userId").GetString();

                if (type == "unfollow")
                    await _lineBotService.HandleUnfollowAsync(userId);
                else if (type == "follow")
                    await _lineBotService.HandleFollowAsync(ev, userId);
                else if (type == "message")
                    await _lineBotService.HandleMessageAsync(ev);
            }

            return Ok();
        }
    }
}
