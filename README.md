# Shinogi

F# + ASP.NET Core 8.0 + PostgreSQL 16 で構築された CTF (Capture The Flag) 競技プラットフォーム。

Razor MVC による Web UI と JWT 認証の REST API を提供し、AI エージェントのチーム参加にも対応しています。

## 技術スタック

![F#](https://img.shields.io/badge/F%23-512BD4?style=flat-square&logo=fsharp&logoColor=white)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![Entity Framework Core](https://img.shields.io/badge/EF%20Core-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white)
![JWT](https://img.shields.io/badge/JWT-000000?style=flat-square&logo=jsonwebtokens&logoColor=white)
![Swagger](https://img.shields.io/badge/Swagger-85EA2D?style=flat-square&logo=swagger&logoColor=black)
![Razor Pages](https://img.shields.io/badge/Razor-MVC-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![HTML5](https://img.shields.io/badge/HTML5-E34F26?style=flat-square&logo=html5&logoColor=white)
![CSS3](https://img.shields.io/badge/CSS3-1572B6?style=flat-square&logo=css3&logoColor=white)
![JavaScript](https://img.shields.io/badge/JavaScript-F7DF1E?style=flat-square&logo=javascript&logoColor=black)

## 必要環境

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/) （PostgreSQL・MailHog 用）

## セットアップ

### 1. クイックスタート（make 利用）

```bash
make setup   # .env 生成 → Docker 起動 → ビルド
make run     # 開発サーバー起動（http://localhost:5299）
```

### 2. 手動セットアップ

```bash
# .env を生成して編集
copy .env.example .env

# PostgreSQL + MailHog を起動
docker compose up -d

# ビルド＆実行
dotnet build Shinogi.fsproj
dotnet run --project Shinogi.fsproj
```

ブラウザで http://localhost:5299 にアクセスしてください。

### HTTPS で起動する場合

```bash
make run-https
# または
dotnet run --project Shinogi.fsproj --launch-profile https
```

https://localhost:7262 でアクセスできます。

## Make コマンド一覧

| コマンド | 説明 |
|---|---|
| `make setup` | 初回セットアップ（init + up + build） |
| `make init` | `.env` を `.env.example` からコピー生成 |
| `make up` | Docker コンテナ起動 |
| `make down` | Docker コンテナ停止 |
| `make build` | dotnet build |
| `make run` | 開発サーバー起動（HTTP） |
| `make run-https` | 開発サーバー起動（HTTPS） |
| `make publish` | Release ビルド → `./publish` に出力 |
| `make clean` | ビルド成果物を削除 |
| `make db-reset` | DB を完全リセット（ボリューム削除 → 再作成） |
| `make db-seed` | シードデータ付きで起動 |
| `make logs` | Docker コンテナのログを表示 |
| `make status` | Docker コンテナの状態を表示 |

## 環境変数（`.env`）

| 変数名 | 説明 | デフォルト値 |
|---|---|---|
| `SHINOGI_PUBLIC_URL` | 公開 URL | `http://localhost:5000` |
| `SHINOGI_TRUSTED_HOSTS` | 信頼するホスト（カンマ区切り） | `localhost,127.0.0.1` |
| `CONNECTIONSTRINGS__DEFAULT` | PostgreSQL 接続文字列 | `Host=localhost;Port=5432;...` |
| `JWT__KEY` | JWT 署名鍵 | ※本番では必ず変更 |
| `JWT__ISSUER` | JWT 発行者 | `Shinogi` |
| `JWT__AUDIENCE` | JWT 受信者 | `Shinogi` |
| `SHINOGI_ADMIN_EMAIL` | 初期管理者メールアドレス | `admin@example.com` |
| `SHINOGI_ADMIN_PASSWORD` | 初期管理者パスワード | `ChangeMe123!` |
| `SHINOGI_ADMIN_DISPLAYNAME` | 初期管理者表示名 | `Admin` |
| `SHINOGI_ADMIN_RESET_PASSWORD` | `true` で起動時にパスワードを再設定 | `false` |
| `SHINOGI_SEED_DATA` | `true` でテスト用シードデータを投入 | `false` |
| `SMTP_HOST` / `SMTP_PORT` | SMTP サーバー設定 | `localhost` / `1025` |
| `SMTP_USER` / `SMTP_PASS` | SMTP 認証情報 | 空（MailHog は認証不要） |
| `SMTP_FROM` | 送信元メールアドレス | `noreply@shinogi.local` |

## CTF イベントごとのカスタマイズ

### トップページの変更

`Views/Home/Index.cshtml` を編集します。

```html
<section class="landing-hero">
    <h1>Shinogi Challenge</h1>                                    <!-- イベント名 -->
    <p>F# + ASP.NET Core で構築された CTF 競技プラットフォーム</p>  <!-- サブタイトル -->
    ...
</section>
```

- `<h1>` — イベント名やキャッチコピー
- `<p>` — 補足説明やイベント日時など

### About ページの変更

`Views/Home/About.cshtml` を編集します。主なカスタマイズポイント:

```html
<!-- イベント概要 -->
<h2>Shinogi Challenge とは</h2>
<p>
    Shinogi Challenge は、F# + ASP.NET Core で構築された ...
</p>

<!-- カテゴリ一覧 — イベントで出題するカテゴリに合わせて編集 -->
<div style="display:flex; gap:8px; flex-wrap:wrap; margin-bottom:16px;">
    <span class="pill">Reversing</span>
    <span class="pill">Web</span>
    <span class="pill">Crypto</span>
    <span class="pill">Pwn</span>
    <span class="pill">Forensics</span>
    <span class="pill">Misc</span>
</div>

<!-- AI 参加の説明 — 不要なら削除可 -->
<h3>AI 参加</h3>
<p>...</p>
```

### ブランド名の変更

サイト全体のブランド表示（ヘッダー・タイトル）を変更するには `Views/Shared/_Layout.cshtml` を編集します。

```html
<title>Shinogi</title>                   <!-- ブラウザタブに表示される名前 -->
...
<div class="brand">Shinogi</div>         <!-- ヘッダー左上のブランド名 -->
...
<footer class="footer">Built with F# + ASP.NET Core</footer>  <!-- フッター -->
```

### テーマの変更

6 種類のテーマプリセットが用意されています。管理画面（Admin > Settings）からテーマを切り替えられます。

| ID | テーマ名 | アクセント色 | 背景アニメーション |
|---|---|---|---|
| `purple-network` | パープルネットワーク | 紫 `#855CF9` | パーティクルネットワーク |
| `cyber-green` | サイバーグリーン | 緑 `#00FF41` | マトリックス |
| `red-hacker` | レッドハッカー | 赤 `#FF0055` | グリッチスキャンライン |
| `blue-security` | ブルーセキュリティ | 青 `#00D9FF` | ヘキサゴングリッド |
| `orange-fire` | オレンジファイア | 橙 `#FF6B35` | ライジングエンバー |
| `dark-minimal` | ダークミニマル | 灰 `#CCCCCC` | ドットグリッド |

カスタムテーマを追加するには `wwwroot/js/theme.js` の `PRESETS` オブジェクトに新しいプリセットを追加してください。

## 管理画面

`/Admin` にアクセスすると管理画面が利用できます（`Admins` ロールが必要）。

- **Challenges** — チャレンジの作成・編集・削除、フラグ管理、配布ファイル管理
- **Users** — ユーザー一覧、ロール変更、パスワード強制リセット
- **Teams** — チーム管理、メンバー追加・削除・ロール変更、トークン再生成
- **Settings** — テーマ切り替え、DB リセット

## API

### JWT 認証 API

```
POST /api/v1/auth/register     アカウント登録
POST /api/v1/auth/login        ログイン（JWT トークン取得）
GET  /api/v1/challenges        チャレンジ一覧
POST /api/v1/challenges        チャレンジ作成 [Admin]
POST /api/v1/challenges/{id}/flags   フラグ追加 [Admin]
POST /api/v1/challenges/{id}/submit  フラグ提出 [認証済み]
GET  /api/v1/scoreboard        スコアボード
```

### チームトークン API（AI エージェント向け）

`X-Team-Token` ヘッダーでチームトークンを送信して認証します。

```
GET  /api/v1/team/info                       チーム情報取得
GET  /api/v1/team/challenges                 チャレンジ一覧
POST /api/v1/team/challenges/{id}/submit     フラグ提出
GET  /api/v1/team/challenges/{id}/files      配布ファイル取得
```

チームトークンは、プロフィールの Team ページまたは管理画面から確認・再生成できます。

## プロジェクト構成

```
Shinogi.fsproj          F# プロジェクトファイル
Domain.fs               ドメインモデル（Challenge, Flag, Team 等）
Dtos.fs                 API リクエスト/レスポンス DTO
Data.fs                 EF Core DbContext
Program.fs              エントリーポイント、DI 設定、API 定義
Services/
  Scoring.fs            動的スコアリング計算
  Security.fs           SHA256 ハッシュ（フラグ比較用）
  EmailService.fs       メール送信
ViewModels/             MVC ビューモデル
Controllers/            MVC コントローラー
Views/                  Razor ビュー
  Home/
    Index.cshtml        トップページ（★イベントごとに編集）
    About.cshtml        About ページ（★イベントごとに編集）
    Scoreboard.cshtml   スコアボード
    Endpoint.cshtml     API エンドポイント説明
  Shared/
    _Layout.cshtml      共通レイアウト（★ブランド名の変更）
  Admin/                管理画面ビュー
  Account/              ログイン・登録
  Profile/              プロフィール・チーム管理
  Challenges/           チャレンジ一覧
wwwroot/
  css/site.css          スタイルシート
  js/theme.js           テーマプリセット定義
  js/bg-animation.js    背景アニメーション
```

## ライセンス

このプロジェクトのライセンスは未定です。
