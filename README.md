# KeepHistory

最近使ったファイルを一覧・検索できる Windows アプリです。

Windows の「最近使った項目」から履歴を自動で記録し、ファイル名やパスで検索できます。
設定で通知領域に常駐させると、ホットキー（既定 **Ctrl + Alt + H**）でいつでも呼び出せます。ホットキーは設定画面で変更できます。

## 動作環境

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（インストールが必要です）

## ビルドと起動

ビルドには [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) が必要です。

```
dotnet build
dotnet run --project src/KeepHistory/KeepHistory.csproj
```

テストは `dotnet test` で実行できます。

データは `%APPDATA%\KeepHistory\` に保存されます。
