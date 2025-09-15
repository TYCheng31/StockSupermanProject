```
LineBotDemo/
├── Controllers/
│   └── LineBotController.cs                  # 控制器，負責處理請求，將邏輯交給服務層
├── Data/
│   └── AppDbContext.cs                       # 資料庫上下文，包含與資料庫的交互邏輯
├── Models/
│   └── AppUser.cs                            # AppUser 模型，定義使用者資料結構
├── Services/
│   └── ILineBotService.cs                    # 服務層接口，定義服務方法
│   └── LineBotService.cs                     # 服務層實現，封裝具體的業務邏輯
├── Program.cs                                # 註冊服務與中介軟體
```

* Services/ILineBotService.cs

  * 這是服務層接口，定義了 LineBotController 需要調用的各個方法，這些方法是實際處理邏輯的服務層所提供的。

* Services/LineBotService.cs

  * 這是服務層的實現，包含具體的業務邏輯，例如處理 follow 事件、unfollow 事件、回覆訊息、更新資料庫等。

