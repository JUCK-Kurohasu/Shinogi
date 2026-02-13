# インスタンス管理機能 設計書

## 概要
WebやPwn問題用の動的インスタンス生成・管理機能。各ユーザー/チームごとに独立したDockerコンテナインスタンスを起動し、一定時間後に自動削除する。

## アーキテクチャ

### 1. ドメインモデル拡張

```fsharp
// Challenge に追加フィールド
type Challenge =
  { ...
    RequiresInstance: bool           // インスタンス起動が必要か
    InstanceImage: string            // Dockerイメージ名 (例: "ctf-web-01:latest")
    InstancePort: int option         // コンテナ内のポート (例: 80)
    InstanceLifetimeMinutes: int     // インスタンス生存時間（分）
    InstanceCpuLimit: string         // CPU制限 (例: "0.5")
    InstanceMemoryLimit: string      // メモリ制限 (例: "256m")
  }

// 新規テーブル: ChallengeInstance
type ChallengeInstance =
  { Id: Guid
    ChallengeId: Guid
    UserId: Guid                     // インスタンスを起動したユーザー
    ContainerId: string              // Docker Container ID
    HostPort: int                    // ホスト側のポート番号
    Url: string                      // アクセスURL (例: http://localhost:8080)
    Flag: string                     // このインスタンス専用のフラグ
    CreatedAt: DateTimeOffset
    ExpiresAt: DateTimeOffset        // 自動削除時刻
    Status: InstanceStatus }         // Running | Stopped | Expired

type InstanceStatus =
    | Running
    | Stopped
    | Expired
```

### 2. インスタンス管理サービス

```fsharp
module InstanceManager =
    // インスタンス起動
    let createInstance (challenge: Challenge) (userId: Guid) : Task<ChallengeInstance>

    // インスタンス停止
    let stopInstance (instanceId: Guid) : Task<unit>

    // 期限切れインスタンスのクリーンアップ（バックグラウンドタスク）
    let cleanupExpiredInstances () : Task<unit>

    // 利用可能なポート番号を取得
    let findAvailablePort () : int

    // フラグ生成（インスタンスごとにユニーク）
    let generateInstanceFlag (challengeId: Guid) (userId: Guid) : string
```

### 3. Docker統合

#### docker-compose.ymlに追加
動的コンテナ用のネットワークを作成：
```yaml
networks:
  shinogi-instances:
    driver: bridge
```

#### Dockerコマンド実行
```bash
# インスタンス起動例
docker run -d \
  --name ctf-instance-{userId}-{challengeId} \
  --network shinogi-instances \
  -p {hostPort}:80 \
  --cpus="0.5" \
  --memory="256m" \
  -e FLAG="flag{unique_generated_flag}" \
  {challenge.InstanceImage}

# インスタンス停止・削除
docker stop ctf-instance-{userId}-{challengeId}
docker rm ctf-instance-{userId}-{challengeId}
```

### 4. UI変更

#### チャレンジ一覧画面
```html
@if (challenge.RequiresInstance)
{
    @if (userInstance != null && userInstance.Status == Running)
    {
        <div class="instance-info">
            <strong>インスタンスURL:</strong>
            <a href="@userInstance.Url" target="_blank">@userInstance.Url</a>
            <br>
            <strong>残り時間:</strong> <span class="countdown">@remainingTime</span>
            <button asp-action="StopInstance" asp-route-id="@challenge.Id">停止</button>
        </div>
    }
    else
    {
        <button asp-action="StartInstance" asp-route-id="@challenge.Id">
            インスタンスを起動
        </button>
    }
}
```

#### 管理画面
- チャレンジ作成時に「インスタンス設定」セクション追加
- Dockerイメージ名、ポート、生存時間、リソース制限を設定

### 5. セキュリティ考慮事項

1. **リソース制限**
   - CPU: 0.5コアまで
   - メモリ: 256MBまで
   - ディスク: 1GBまで
   - 同時起動数: ユーザーあたり3インスタンスまで

2. **ネットワーク分離**
   - インスタンス用専用ネットワーク
   - インターネットアクセス制限（必要に応じて）

3. **ポートレンジ**
   - 動的ポート範囲: 30000-32000
   - ファイアウォールで外部公開を制限

4. **フラグユニーク化**
   - インスタンスごとに異なるフラグを生成
   - `flag{challenge-id}_{user-id}_{timestamp-hash}`

### 6. バックグラウンドタスク

```fsharp
// Program.fs に追加
let startInstanceCleanupTask (serviceProvider: IServiceProvider) =
    Task.Run(fun () ->
        task {
            while true do
                try
                    use scope = serviceProvider.CreateScope()
                    let db = scope.ServiceProvider.GetRequiredService<CtfdDbContext>()
                    do! InstanceManager.cleanupExpiredInstances db
                with ex ->
                    Console.WriteLine($"Cleanup error: {ex.Message}")
                do! Task.Delay(TimeSpan.FromMinutes(5.0))
        } :> Task)
```

### 7. API拡張

```
POST /api/v1/challenges/{id}/instance/start    - インスタンス起動
POST /api/v1/challenges/{id}/instance/stop     - インスタンス停止
GET  /api/v1/challenges/{id}/instance          - インスタンス情報取得
```

## 実装フェーズ

### Phase 1: ドメインモデル拡張
- [ ] Domain.fs に ChallengeInstance, InstanceStatus 追加
- [ ] Data.fs に DbSet 追加
- [ ] DB初期化SQL追加

### Phase 2: Docker統合サービス
- [ ] Services/InstanceManager.fs 作成
- [ ] Docker CLI経由でのコンテナ操作実装
- [ ] ポート管理機能

### Phase 3: コントローラー実装
- [ ] ChallengesController にインスタンス起動/停止アクション追加
- [ ] AdminController にインスタンス管理画面追加

### Phase 4: UI実装
- [ ] チャレンジ一覧画面にインスタンスボタン追加
- [ ] 管理画面にインスタンス設定フォーム追加

### Phase 5: バックグラウンドタスク
- [ ] 期限切れインスタンスの自動削除
- [ ] リソース監視

## サンプルチャレンジ設定

```json
{
  "name": "Web Login Bypass",
  "category": "Web",
  "requiresInstance": true,
  "instanceImage": "vulnerableapp:v1",
  "instancePort": 80,
  "instanceLifetimeMinutes": 30,
  "instanceCpuLimit": "0.5",
  "instanceMemoryLimit": "256m"
}
```

## 備考

- Dockerデーモンへのアクセスが必要（Unix socket or TCP）
- プロダクション環境ではKubernetesへの移行も検討
- フラグの動的注入はDockerコンテナの環境変数経由
