<!DOCTYPE html>
<html lang="ja">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Login Challenge - SQL Injection</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .container {
            background: white;
            padding: 40px;
            border-radius: 10px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
            width: 90%;
            max-width: 400px;
        }
        h1 { color: #333; margin-bottom: 30px; text-align: center; }
        .form-group { margin-bottom: 20px; }
        label { display: block; margin-bottom: 5px; color: #555; font-weight: 500; }
        input[type="text"], input[type="password"] {
            width: 100%;
            padding: 12px;
            border: 2px solid #ddd;
            border-radius: 5px;
            font-size: 14px;
            transition: border 0.3s;
        }
        input:focus { outline: none; border-color: #667eea; }
        button {
            width: 100%;
            padding: 12px;
            background: #667eea;
            color: white;
            border: none;
            border-radius: 5px;
            font-size: 16px;
            cursor: pointer;
            transition: background 0.3s;
        }
        button:hover { background: #5568d3; }
        .error { color: #e74c3c; margin-top: 15px; text-align: center; }
        .success { color: #27ae60; margin-top: 15px; text-align: center; }
        .hint { font-size: 12px; color: #888; margin-top: 20px; text-align: center; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔐 Admin Login</h1>
        <form method="POST">
            <div class="form-group">
                <label for="username">Username</label>
                <input type="text" id="username" name="username" required>
            </div>
            <div class="form-group">
                <label for="password">Password</label>
                <input type="password" id="password" name="password" required>
            </div>
            <button type="submit">Login</button>
        </form>

        <?php
        if ($_SERVER['REQUEST_METHOD'] === 'POST') {
            $username = $_POST['username'] ?? '';
            $password = $_POST['password'] ?? '';

            // 脆弱なSQL（SQLインジェクション可能）
            $query = "SELECT * FROM users WHERE username = '$username' AND password = '$password'";

            // シミュレーション：実際にはDBは使わず、パターンマッチで判定
            // ' OR '1'='1 などでバイパス可能
            if (strpos($query, "' OR '1'='1") !== false || strpos($query, "' OR 1=1") !== false) {
                $flag = getenv('FLAG') ?: 'flag{sql_injection_success}';
                echo "<div class='success'>✅ Login successful!<br><strong>Flag: $flag</strong></div>";
            } else {
                echo "<div class='error'>❌ Invalid credentials</div>";
            }
        }
        ?>

        <div class="hint">💡 Hint: Can you bypass the login?</div>
    </div>
</body>
</html>
