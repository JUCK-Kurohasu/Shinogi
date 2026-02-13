FROM ubuntu:22.04

# 必要なパッケージをインストール
RUN apt-get update && apt-get install -y \
    gcc \
    socat \
    && rm -rf /var/lib/apt/lists/*

# 脆弱なバイナリをコンパイル
COPY vuln.c /home/ctf/
WORKDIR /home/ctf
RUN gcc -o vuln vuln.c -fno-stack-protector -z execstack -no-pie -g

# 権限設定
RUN chmod +x vuln

# ポート公開（socatでネットワーク経由でアクセス可能にする）
EXPOSE 9999

# socatでバイナリを公開
CMD ["socat", "TCP-LISTEN:9999,reuseaddr,fork", "EXEC:/home/ctf/vuln"]
