# KeepHistory

最近使ったファイルを一覧・検索できる Windows アプリです。

Windows の「最近使った項目」から履歴を自動で記録し、ファイル名やパスで検索できます。
設定で通知領域に常駐させると、ホットキー（既定 **Ctrl + Alt + H**）でいつでも呼び出せます。ホットキーは設定画面で変更できます。

## 動作環境

- Windows 10 / 11（64 ビット）

## ダウンロード

[Releases](../../releases) から `KeepHistory-v（バージョン）-win-x64.zip` をダウンロードし、展開した `KeepHistory.exe` を実行してください。
.NET のランタイムを同梱しているため、別途インストールする必要はありません。

## ご利用の前に

配布しているアプリ（exe）はデジタル署名をしていません。そのため、ダウンロードや初回の起動時に、Windows（SmartScreen）やウイルス対策ソフトの警告が表示される場合があります。

SmartScreen の警告が出た場合は、「詳細情報」→「実行」で起動できます。

## 使用技術

- [.NET 8](https://dotnet.microsoft.com/)（C#）
- [WPF](https://github.com/dotnet/wpf)（画面）
- [Windows Forms](https://github.com/dotnet/winforms)（通知領域のアイコン）
- [System.Text.Json](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/overview)（履歴・設定の保存）

## ビルドと起動

ビルドには [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) が必要です。

```
dotnet build
dotnet run --project src/KeepHistory/KeepHistory.csproj
```

テストは `dotnet test` で実行できます。

データは `%APPDATA%\KeepHistory\` に保存されます。
