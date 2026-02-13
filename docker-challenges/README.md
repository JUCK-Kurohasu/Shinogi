# CTF Challenge Docker Images

このディレクトリには、Shinogiプラットフォーム用のCTFチャレンジDockerイメージが格納されています。

## イメージ一覧

### 1. Web - SQL Injection (`web-sqli`)

**カテゴリ**: Web
**難易度**: Easy
**説明**: ログイン画面のSQLインジェクション脆弱性を突いてフラグを取得してください。

**ビルド方法**:
```bash
cd docker-challenges/web-sqli
docker build -t ctf-web-sqli:latest .
```

**テスト実行**:
```bash
docker run -d -p 8080:80 -e FLAG="flag{test_sql}" ctf-web-sqli:latest
# ブラウザで http://localhost:8080 にアクセス
```

**解法ヒント**:
- Username: `' OR '1'='1`
- Password: `' OR '1'='1`

---

### 2. Pwn - Buffer Overflow (`pwn-bufoverflow`)

**カテゴリ**: Pwn
**難易度**: Medium
**説明**: バッファオーバーフロー脆弱性を利用してフラグを取得してください。

**ビルド方法**:
```bash
cd docker-challenges/pwn-bufoverflow
docker build -t ctf-pwn-bufoverflow:latest .
```

**テスト実行**:
```bash
docker run -d -p 9999:9999 -e FLAG="flag{test_pwn}" ctf-pwn-bufoverflow:latest
# nc localhost 9999 で接続
```

**解法ヒント**:
- `print_flag()` 関数のアドレスを特定
- リターンアドレスを上書きして `print_flag()` にジャンプ

---

## Shinogi への登録方法

### 1. イメージをビルド

```bash
cd docker-challenges/web-sqli
docker build -t ctf-web-sqli:latest .

cd ../pwn-bufoverflow
docker build -t ctf-pwn-bufoverflow:latest .
```

### 2. 管理画面でチャレンジを作成

**Web SQL Injection の例**:
- Name: `Web Login Bypass`
- Category: `Web`
- Difficulty: `Easy`
- Description: `ログイン画面の脆弱性を見つけてフラグを取得してください。`
- Value Initial: `300`
- Value Minimum: `50`
- Decay: `15`
- Function: `Log`
- **Requires Instance**: `✓` (チェック)
- **Instance Image**: `ctf-web-sqli:latest`
- **Instance Port**: `80`
- **Instance Lifetime**: `30` 分
- **CPU Limit**: `0.5`
- **Memory Limit**: `256m`

**Pwn Buffer Overflow の例**:
- Name: `Buffer Overflow 101`
- Category: `Pwn`
- Difficulty: `Medium`
- Description: `バッファオーバーフロー脆弱性を利用してシェルを取得してください。`
- Value Initial: `500`
- Value Minimum: `100`
- Decay: `20`
- Function: `Linear`
- **Requires Instance**: `✓` (チェック)
- **Instance Image**: `ctf-pwn-bufoverflow:latest`
- **Instance Port**: `9999`
- **Instance Lifetime**: `45` 分
- **CPU Limit**: `0.5`
- **Memory Limit**: `256m`

### 3. フラグの自動注入

インスタンス起動時、システムが自動的に以下の環境変数を設定します：
```bash
FLAG="flag{ユニークなハッシュ値}"
```

各ユーザー・各インスタンスごとに異なるフラグが生成されるため、フラグの共有を防止できます。

## セキュリティ上の注意

- **リソース制限**: CPU/メモリ制限を必ず設定してください
- **ネットワーク分離**: インスタンスは `shinogi-instances` ネットワークで隔離されます
- **自動削除**: 指定時間経過後、インスタンスは自動的に削除されます
- **同時起動数制限**: ユーザーあたり3インスタンスまで

## トラブルシューティング

### イメージが起動しない
```bash
docker logs <container_id>
```

### ポートが使用中
```bash
docker ps -a
docker stop <container_id>
docker rm <container_id>
```

### インスタンスネットワーク作成
```bash
docker network create shinogi-instances
```
