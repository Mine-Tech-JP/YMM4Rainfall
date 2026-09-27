# 雨雪と浮上のビルド手順

リポジトリのルートで以下の操作を行います。

## ソースからビルドする

.NET SDK 10.0.400 系と、YMM4 の実行フォルダーにある参照DLLが必要です。`Directory.Build.local.props.sample` を `Directory.Build.local.props` にコピーし、`YMM4DirPath` を自分の環境の YMM4 実行フォルダーに変更してください。この設定ファイルは Git の管理対象から除外します。

PowerShell で次を実行します。

```powershell
./scripts/build.ps1 -Configuration Release
```

検証コードは `tests/YMM4Rainfall.Verification/`、検証スクリプトは `scripts/verify.ps1` にあります。ビルドスクリプトは YMM4 への DLL 配置を行いません。


[READMEへ戻る](../README.md)
