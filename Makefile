# ============================================================
#  Shinogi Makefile
#  使い方: make help
# ============================================================

.DEFAULT_GOAL := help

# ---------- 設定 ----------
COMPOSE      := docker compose
DOTNET       := dotnet
PROJECT      := Shinogi.fsproj
PUBLISH_DIR  := ./publish
ENV_FILE     := .env
ENV_EXAMPLE  := .env.example

# ---------- ヘルプ ----------
.PHONY: help
help: ## コマンド一覧を表示
	@echo.
	@echo   Shinogi Makefile
	@echo   ==============
	@echo.
	@echo   make setup        ... 初回セットアップ (init + up + build)
	@echo   make init         ... .env を生成（JWT鍵を自動生成）
	@echo   make up           ... Docker コンテナ起動 (PostgreSQL + MailHog)
	@echo   make down         ... Docker コンテナ停止
	@echo   make build        ... dotnet build
	@echo   make run          ... 開発サーバー起動 (HTTP)
	@echo   make run-https    ... 開発サーバー起動 (HTTPS)
	@echo   make publish      ... Release ビルド
	@echo   make clean        ... ビルド成果物を削除
	@echo   make db-reset     ... DB を完全リセット（ボリューム削除 + 再作成）
	@echo   make db-seed      ... シードデータ付きで起動
	@echo   make logs         ... Docker コンテナのログを表示
	@echo   make status       ... Docker コンテナの状態を表示
	@echo.

# ---------- 初回セットアップ ----------
.PHONY: setup
setup: init up build ## 初回セットアップ (init + up + build)
	@echo [OK] セットアップ完了。 make run で起動できます。

# ---------- .env 生成 ----------
.PHONY: init
init: ## .env を生成（既存の場合はスキップ）
	@if not exist $(ENV_FILE) ( \
		copy $(ENV_EXAMPLE) $(ENV_FILE) >nul && \
		echo [OK] .env を生成しました。必要に応じて編集してください。 \
	) else ( \
		echo [SKIP] .env は既に存在します。 \
	)

.PHONY: init-force
init-force: ## .env を強制再生成（既存ファイルを上書き）
	@copy $(ENV_EXAMPLE) $(ENV_FILE) >nul
	@echo [OK] .env を再生成しました。

# ---------- Docker ----------
.PHONY: up
up: ## Docker コンテナ起動 (PostgreSQL + MailHog)
	$(COMPOSE) up -d
	@echo [OK] コンテナ起動完了。

.PHONY: down
down: ## Docker コンテナ停止
	$(COMPOSE) down
	@echo [OK] コンテナ停止完了。

.PHONY: logs
logs: ## Docker コンテナのログを表示
	$(COMPOSE) logs -f

.PHONY: status
status: ## Docker コンテナの状態を表示
	$(COMPOSE) ps

# ---------- ビルド・実行 ----------
.PHONY: build
build: ## dotnet build
	$(DOTNET) build $(PROJECT)

.PHONY: run
run: ## 開発サーバー起動 (HTTP: localhost:5299)
	$(DOTNET) run --project $(PROJECT)

.PHONY: run-https
run-https: ## 開発サーバー起動 (HTTPS: localhost:7262)
	$(DOTNET) run --project $(PROJECT) --launch-profile https

.PHONY: publish
publish: ## Release ビルド → ./publish に出力
	$(DOTNET) publish $(PROJECT) -c Release -o $(PUBLISH_DIR)
	@echo [OK] $(PUBLISH_DIR) にビルド成果物を出力しました。

# ---------- クリーン ----------
.PHONY: clean
clean: ## ビルド成果物を削除
	$(DOTNET) clean $(PROJECT)
	@if exist $(PUBLISH_DIR) rmdir /s /q $(PUBLISH_DIR)
	@echo [OK] クリーン完了。

# ---------- DB 操作 ----------
.PHONY: db-reset
db-reset: ## DB を完全リセット（コンテナ + ボリューム削除 → 再作成）
	$(COMPOSE) down -v
	$(COMPOSE) up -d
	@echo [OK] DB をリセットしました。make run で再初期化されます。

.PHONY: db-seed
db-seed: ## シードデータ付きで起動（SHINOGI_SEED_DATA=true で一時実行）
	set SHINOGI_SEED_DATA=true && $(DOTNET) run --project $(PROJECT)
