# CLAUDE.md

このファイルは、Claude Code (claude.ai/code) がこのリポジトリのコードを扱う際のガイダンスを提供します。

## プロジェクト概要

Shinogiは、F# + ASP.NET Core 8.0 + PostgreSQL 16で構築されたCTF (Capture The Flag) 競技プラットフォーム。Razor MVCによるWebUIと、JWT認証のREST APIの両方を提供する。AI参加にも対応したチームトークン認証APIを持つ。

## ビルド・実行コマンド

```bash
# PostgreSQL起動（先に必要）
docker-compose up -d

# ビルド
dotnet build

# 実行（開発用）
dotnet run                          # HTTP: localhost:5299
dotnet run --launch-profile https   # HTTPS: localhost:7262

# 公開ビルド
dotnet publish -c Release -o ./publish
```

テストフレームワーク・リンターは未導入。

## アーキテクチャ

**三重インターフェース構成**: MVC Web UI（Cookie認証、Razorビュー）、Minimal APIレイヤー（`/api/v1/*`、JWT Bearer認証）、チームAPIレイヤー（`/api/v1/team/*`、X-Team-Token ヘッダー認証）が共存し、ドメイン・サービス・EF Coreデータ層を共有している。

### 主要ファイルと役割

- **Program.fs** — エントリーポイント。DI設定、Minimal APIエンドポイント定義、チームAPI定義、.env読み込み、DB初期化、管理者ユーザー作成を担当。
- **Domain.fs** — コア型定義: `Challenge`, `Flag`, `Submission`, `Team`（Token付き）, `TeamMember`（Role付き）, `CtfdUser`。判別共用体で`ScoreFunction`（Linear|Log|Exp）、`ChallengeLogic`（Any|All|TeamConsensus）、`MemberRole`（Owner|Player|AI）を定義。
- **Data.fs** — EF Coreの`CtfdDbContext`。判別共用体の文字列変換用ValueConverter（ScoreFunction, ChallengeLogic, MemberRole）。
- **Services/Scoring.fs** — 動的スコアリング: 解答チーム数が増えるほどポイントが減少する。
- **Services/Security.fs** — フラグ内容比較用のSHA256ハッシュ。

### コントローラー (MVC)

- **HomeController** — ランディングページ + スコアボード（表示名・チーム名付き）
- **ChallengesController** — チャレンジ一覧（coming soon対応）+ フラグ提出機能
- **AccountController** — ログイン/登録/ログアウト
- **ProfileController** — プロフィール表示、設定画面（名前・メアド・PW変更）、チーム管理（作成/参加/脱退、トークン表示）
- **AdminController** — チャレンジCRUD、フラグ管理、ユーザー管理（CRUD・ロール変更・PW強制リセット）、チーム管理（作成/削除/メンバー追加・削除・ロール変更/トークン再生成）

### APIエンドポイント

#### JWT認証API (Minimal API、Program.fs内)
```
POST /api/v1/auth/register|login
GET|POST /api/v1/challenges
POST /api/v1/challenges/{id}/flags      [Admin]
POST /api/v1/challenges/{id}/submit     [認証済みユーザー]
GET  /api/v1/scoreboard
```

#### チームトークン認証API (X-Team-Token ヘッダー)
```
GET  /api/v1/team/info                          チーム情報取得
GET  /api/v1/team/challenges                    チャレンジ一覧
POST /api/v1/team/challenges/{id}/submit        フラグ提出（AI対応）
GET  /api/v1/team/challenges/{id}/files         配布ファイルDL（スタブ）
```

### 重要なパターン

- **フラグ提出フロー**: ユーザーが平文フラグを送信 → SHA256ハッシュ化 → 保存済みハッシュと比較 → 正解の場合、`dynamicValue`が解答数に基づきポイントを計算 → Submissionレコードにスコアを記録。
- **チームトークン認証**: 各チームにUUIDトークンが発行される。APIリクエスト時に`X-Team-Token`ヘッダーで認証。`resolveTeamFromToken`関数で検証。
- **ロールシステム**: TeamMemberにMemberRole（Owner/Player/AI）判別共用体。チームAPI経由のフラグ提出時、AIロールのメンバーのAccountIdで記録。
- **チームオーナーシップ**: オーナーが脱退すると、最も在籍期間の長いメンバーにオーナー権が移譲される。メンバーがいなくなるとチームは削除される。
- **DB初期化**: Program.fsがEF Coreの`EnsureCreated`でテーブル作成後、生SQLでTeams/TeamMembersテーブルを初期化。既存DBへのマイグレーション用ALTER TABLEも含む。
- **設定**: シークレットは`.env`ファイルと`appsettings.json`から取得。

### デザインテーマ

紫ベースのダークテーマ。カラーパレット:
- 黒: #000000（全体背景）
- ダークパープル: #180530（カード・フォーム・テーブル背景）
- ビビッドパープル: #855CF9（アクセント・ボタン）
- 白: #FFFFFF（テキスト）

## F#のコーディング規約

- ルート名前空間: `Shinogi`
- F#ではファイルのコンパイル順序が重要 — `Shinogi.fsproj`で定義（Domain → Dtos → Services → Data → ViewModels → Controllers → Program）。
- EF Core/MVCのモデルバインディング互換のため、レコード型に`[<CLIMutable>]`属性を使用。
- 判別共用体はData.fs内のEF Core ValueConverterで文字列との相互変換を行う。
- task CE内で`if ... then let! ... `のブロックは必ず末尾に`()`を付ける。
- `return`を含むif分岐は、全分岐で`return`するか`mutable`変数でエラーを集約するパターンを使用。
