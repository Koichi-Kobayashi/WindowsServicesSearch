# Microsoft Store 配信用 MSIX の作成手順

## 前提

- Windows 11 と Visual Studio 2026、または対応する .NET SDK / Windows SDK を使用します。
- NuGet パッケージを復元できる環境が必要です。
- Microsoft Store に提出する前に、Partner Center で製品名を予約し、パッケージ ID を取得します。
- Store 提出用パッケージは署名せずに作成します。提出後の署名は Microsoft Store が行います。

## Store ID の設定

`Package.appxmanifest` には、Partner Center で予約した次の製品 ID を設定しています。

| 項目 | 設定値 |
| --- | --- |
| `Package/Identity/@Name` | `FastExplorerStudio.WindowsServicesSearch` |
| `Package/Identity/@Publisher` | `CN=9006F72B-0B3B-47AD-BF23-A0D351017457` |
| `Package/Properties/PublisherDisplayName` | `Fast Explorer Studio` |

製品の予約を変更した場合は、Partner Center の製品ページで **製品管理 > 製品 ID** を開き、次の対応で `WindowsServicesSearch/Package.appxmanifest` を更新します。

| マニフェスト | Partner Center |
| --- | --- |
| `Package/Identity/@Name` | パッケージ/ID/名前 |
| `Package/Identity/@Publisher` | パッケージ/ID/発行元 |
| `Package/Properties/PublisherDisplayName` | パッケージ/プロパティ/発行元表示名 |

値は空白、句読点、大文字と小文字を含めて完全に一致させてください。Visual Studio の **発行 > アプリケーションをストアに関連付ける** を使用して設定しても構いません。関連付け後はマニフェストの差分を確認します。

## バージョンの更新

パッケージバージョンは `Directory.Build.props` の `AppxPackageVersion` で管理します。形式は `Major.Minor.Build.Revision` の4要素で、各要素は 0～65535 です。Store に提出するたびに、以前の提出より大きいバージョンへ更新してください。

次のスクリプトは、その値を `Package.appxmanifest` に反映します。

```powershell
.\scripts\Sync-PackageVersion.ps1
```

## Store提出用MSIXUPLOADの生成

リポジトリのルートで次を実行すると、x64とARM64をまとめた署名なしの `.msixupload` が生成されます。通常はこのファイルをPartner Centerへアップロードします。

```powershell
.\scripts\Build-StoreUpload.ps1
```

出力先は次の配下です。

```text
WindowsServicesSearch/AppPackages/StoreUpload/
```

`.msixupload` にはx64とARM64の `.msix` をまとめた `.msixbundle` と、クラッシュ解析用のシンボルが含まれます。Partner Centerは利用者の端末に適したアーキテクチャを自動的に配信します。

## 個別MSIXの生成

リポジトリのルートで次を実行すると、x64 と ARM64 の署名なし Release MSIX が生成されます。

```powershell
.\scripts\Build-StoreMsix.ps1
```

片方のアーキテクチャだけを生成する場合は次のように指定します。

```powershell
.\scripts\Build-StoreMsix.ps1 -Platform x64
.\scripts\Build-StoreMsix.ps1 -Platform ARM64
```

出力先は次の配下です。

```text
WindowsServicesSearch/AppPackages/x64/
WindowsServicesSearch/AppPackages/ARM64/
```

スクリプトを使わずにビルドする場合の x64 コマンドは次のとおりです。ARM64 では `Platform` と出力ディレクトリを置き換えます。

```powershell
dotnet build .\WindowsServicesSearch\WindowsServicesSearch.csproj `
  --configuration Release `
  -p:Platform=x64 `
  -p:PublishTrimmed=false `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageSigningEnabled=false `
  -p:AppxPackageDir=AppPackages\x64\
```

## 提出前の確認

1. `Package.appxmanifest` の Name、Publisher、PublisherDisplayName が Partner Center の値と一致していることを確認します。
2. x64 と ARM64 のビルドがエラーなく完了したことを確認します。
3. 生成された `.msixupload` を Partner Center のパッケージ画面へアップロードします。
4. Partner Center の検証結果で、Identity、対象 OS、アーキテクチャ、アイコンにエラーがないことを確認します。
5. 説明、スクリーンショット、プライバシーポリシーなどのストア掲載情報を入力して認定へ提出します。

CmdPal 拡張機能の実動作は PowerToys Command Palette が利用できる Windows 環境で確認してください。サービスのプロパティを開く操作では UAC が表示されます。
