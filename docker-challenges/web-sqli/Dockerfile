FROM php:8.2-apache

# Apacheの設定
RUN a2enmod rewrite

# アプリケーションファイルをコピー
COPY index.php /var/www/html/

# 権限設定
RUN chown -R www-data:www-data /var/www/html

# ポート公開
EXPOSE 80

# Apache起動
CMD ["apache2-foreground"]
