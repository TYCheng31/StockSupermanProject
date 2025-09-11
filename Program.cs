using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// 給 LINE Messaging API 用
builder.Services.AddHttpClient();

// Swagger（可留）
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 讓同網域/其他裝置可連：綁 0.0.0.0:5077
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, 5077); // HTTP
    // 如需同時保留本機預設埠，可另外加一條 ListenLocalhost(xxx)
    // options.ListenLocalhost(5046);
});

// CORS（對 Webhook 不需要；你若有前端要打才需要）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

// 開發才開 Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 不強制 https（ngrok 用 http → https 轉發）
/* app.UseHttpsRedirection(); */

// 沒設定 Auth，就不用 UseAuthorization 也可
// app.UseAuthorization();

app.MapControllers();

// 健康檢查
app.MapGet("/", () => "UP");

app.Run();
