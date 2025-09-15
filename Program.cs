using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

using Microsoft.EntityFrameworkCore;
using LineBotDemo.Data;
using LineBotDemo.Services;  // 引入服務層

// ↓ 新增：命名慣例套件（UseSnakeCaseNamingConvention）
using EFCore.NamingConventions;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// 給 LINE Messaging API 用
builder.Services.AddHttpClient();

// Swagger（可留）
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ★ PostgreSQL DbContext（加上 Snake Case 對應）
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Pg"))
        .UseSnakeCaseNamingConvention() // ← 讓 EF 自動對應到 app_users / user_stocks
);

// 註冊 LineBotService 服務
builder.Services.AddScoped<ILineBotService, LineBotService>();

// 綁定 0.0.0.0:5077
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, 5077);
});

// CORS（若需要前端呼叫）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 啟用 MVC 控制器路由
app.MapControllers();

// 簡單的健康檢查路由
app.MapGet("/", () => "UP");

app.Run();
