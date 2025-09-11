using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace LineBotDemo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExternalApiController : ControllerBase
    {
        // 用來接收四位數字的 API
        [HttpGet("receive/{id}")]
        public async Task<IActionResult> Receive(string id)
        {
            if (id.Length == 4 && int.TryParse(id, out _))
            {
                // 這裡您可以執行其他操作，例如儲存這四位數字，或與資料庫互動
                Console.WriteLine($"接收到四位數字: {id}");
                return Ok(new { message = $"接收到的四位數字是: {id}" });
            }

            return BadRequest(new { message = "無效的四位數字" });
        }
    }
}
