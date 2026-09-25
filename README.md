# KeepHistory

最近使ったファイルを一覧・検索できる、Windows の通知領域に常駐するアプリです。

Windows の「最近使った項目」を監視して履歴を自動で記録し、ホットキー（既定 **Ctrl + Alt + H**）で呼び出して、ファイル名やパスで検索できます。

## 動作環境

- Windows 10 / 11
- .NET 8

## ビルドと起動

```
dotnet build
dotnet run --project src/KeepHistory/KeepHistory.csproj
```

テストは `dotnet test` で実行できます。

データは `%APPDATA%\KeepHistory\` に保存されます。
