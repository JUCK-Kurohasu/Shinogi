// Shinogi 背景アニメーションシステム
// 6種類のCanvas背景アニメーションを提供
(function () {
  "use strict";

  var canvas = document.getElementById("bg-canvas");
  if (!canvas) return;
  var ctx = canvas.getContext("2d");
  var w, h;
  var animationId = null;

  function resize() {
    w = canvas.width = window.innerWidth;
    h = canvas.height = window.innerHeight;
  }

  window.addEventListener("resize", resize);
  resize();

  // ユーティリティ: CSSカラーからRGB取得
  function parseColor(color) {
    var m = color.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
    if (m) return { r: parseInt(m[1], 16), g: parseInt(m[2], 16), b: parseInt(m[3], 16) };
    return { r: 255, g: 255, b: 255 };
  }

  // ========================================
  // 1. particle-network (パーティクルネットワーク)
  // ========================================
  function particleNetwork(primary) {
    var c = parseColor(primary);
    var PARTICLE_COUNT = 80;
    var MAX_DIST = 140;
    var SPEED = 0.3;
    var particles = [];

    for (var i = 0; i < PARTICLE_COUNT; i++) {
      particles.push({
        x: Math.random() * w, y: Math.random() * h,
        vx: (Math.random() - 0.5) * SPEED, vy: (Math.random() - 0.5) * SPEED,
        r: Math.random() * 1.5 + 0.5
      });
    }

    return function () {
      ctx.clearRect(0, 0, w, h);
      for (var i = 0; i < particles.length; i++) {
        var p = particles[i];
        p.x += p.vx; p.y += p.vy;
        if (p.x < 0 || p.x > w) p.vx *= -1;
        if (p.y < 0 || p.y > h) p.vy *= -1;
        for (var j = i + 1; j < particles.length; j++) {
          var q = particles[j];
          var dx = p.x - q.x, dy = p.y - q.y;
          var dist = Math.sqrt(dx * dx + dy * dy);
          if (dist < MAX_DIST) {
            var alpha = (1 - dist / MAX_DIST) * 0.15;
            ctx.beginPath();
            ctx.moveTo(p.x, p.y);
            ctx.lineTo(q.x, q.y);
            ctx.strokeStyle = "rgba(" + c.r + "," + c.g + "," + c.b + "," + alpha + ")";
            ctx.lineWidth = 0.5;
            ctx.stroke();
          }
        }
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
        ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + ",0.6)";
        ctx.fill();
      }
    };
  }

  // ========================================
  // 2. matrix-rain (マトリックスコードレイン)
  // ========================================
  function matrixRain(primary) {
    var c = parseColor(primary);
    var FONT_SIZE = 14;
    var columns = Math.floor(w / FONT_SIZE);
    var drops = [];
    for (var i = 0; i < columns; i++) {
      drops[i] = Math.random() * -100;
    }
    var chars = "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲンABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    return function () {
      ctx.fillStyle = "rgba(0, 0, 0, 0.05)";
      ctx.fillRect(0, 0, w, h);
      ctx.font = FONT_SIZE + "px monospace";
      for (var i = 0; i < drops.length; i++) {
        var ch = chars[Math.floor(Math.random() * chars.length)];
        var alpha = 0.6 + Math.random() * 0.4;
        ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + "," + alpha + ")";
        ctx.fillText(ch, i * FONT_SIZE, drops[i] * FONT_SIZE);
        if (drops[i] * FONT_SIZE > h && Math.random() > 0.975) {
          drops[i] = 0;
        }
        drops[i] += 0.5;
      }
    };
  }

  // ========================================
  // 3. glitch-scanline (グリッチスキャンライン)
  // ========================================
  function glitchScanline(primary) {
    var c = parseColor(primary);
    var scanY = 0;
    var glitchBlocks = [];
    var glitchTimer = 0;

    return function () {
      ctx.clearRect(0, 0, w, h);

      // スキャンライン
      scanY = (scanY + 1.5) % h;
      ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + ",0.03)";
      for (var y = 0; y < h; y += 4) {
        ctx.fillRect(0, y, w, 1);
      }
      // 主スキャンライン
      ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + ",0.15)";
      ctx.fillRect(0, scanY, w, 2);
      ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + ",0.05)";
      ctx.fillRect(0, scanY - 20, w, 40);

      // グリッチブロック
      glitchTimer++;
      if (glitchTimer > 60 && Math.random() > 0.95) {
        glitchTimer = 0;
        glitchBlocks = [];
        var count = Math.floor(Math.random() * 5) + 1;
        for (var i = 0; i < count; i++) {
          glitchBlocks.push({
            x: Math.random() * w,
            y: Math.random() * h,
            w: Math.random() * 200 + 20,
            h: Math.random() * 10 + 2,
            life: Math.floor(Math.random() * 10) + 5
          });
        }
      }
      for (var i = glitchBlocks.length - 1; i >= 0; i--) {
        var b = glitchBlocks[i];
        ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + "," + (0.08 + Math.random() * 0.06) + ")";
        ctx.fillRect(b.x + (Math.random() - 0.5) * 4, b.y, b.w, b.h);
        b.life--;
        if (b.life <= 0) glitchBlocks.splice(i, 1);
      }
    };
  }

  // ========================================
  // 4. hexagon-grid (ヘキサゴングリッド)
  // ========================================
  function hexagonGrid(primary) {
    var c = parseColor(primary);
    var HEX_SIZE = 40;
    var time = 0;

    return function () {
      ctx.clearRect(0, 0, w, h);
      time += 0.01;
      var hexW = HEX_SIZE * Math.sqrt(3);
      var hexH = HEX_SIZE * 1.5;
      var cols = Math.ceil(w / hexW) + 1;
      var rows = Math.ceil(h / hexH) + 1;

      for (var row = 0; row < rows; row++) {
        for (var col = 0; col < cols; col++) {
          var cx = col * hexW + (row % 2 ? hexW / 2 : 0);
          var cy = row * hexH;
          var pulse = Math.sin(time * 2 + col * 0.3 + row * 0.5) * 0.5 + 0.5;
          var alpha = 0.03 + pulse * 0.07;

          ctx.beginPath();
          for (var s = 0; s < 6; s++) {
            var angle = Math.PI / 3 * s - Math.PI / 6;
            var px = cx + HEX_SIZE * 0.8 * Math.cos(angle);
            var py = cy + HEX_SIZE * 0.8 * Math.sin(angle);
            if (s === 0) ctx.moveTo(px, py);
            else ctx.lineTo(px, py);
          }
          ctx.closePath();
          ctx.strokeStyle = "rgba(" + c.r + "," + c.g + "," + c.b + "," + alpha + ")";
          ctx.lineWidth = 1;
          ctx.stroke();
        }
      }
    };
  }

  // ========================================
  // 5. rising-embers (上昇する火の粉)
  // ========================================
  function risingEmbers(primary) {
    var c = parseColor(primary);
    var EMBER_COUNT = 60;
    var embers = [];

    function createEmber() {
      return {
        x: Math.random() * w,
        y: h + Math.random() * 20,
        vx: (Math.random() - 0.5) * 0.5,
        vy: -(Math.random() * 1.5 + 0.5),
        size: Math.random() * 3 + 1,
        alpha: Math.random() * 0.6 + 0.2,
        decay: Math.random() * 0.003 + 0.001,
        flicker: Math.random() * Math.PI * 2
      };
    }

    for (var i = 0; i < EMBER_COUNT; i++) {
      var e = createEmber();
      e.y = Math.random() * h;
      embers.push(e);
    }

    return function () {
      ctx.clearRect(0, 0, w, h);
      for (var i = 0; i < embers.length; i++) {
        var e = embers[i];
        e.x += e.vx + Math.sin(e.flicker) * 0.3;
        e.y += e.vy;
        e.flicker += 0.05;
        e.alpha -= e.decay;

        if (e.alpha <= 0 || e.y < -10) {
          embers[i] = createEmber();
          continue;
        }

        var flick = Math.sin(e.flicker) * 0.15;
        var a = Math.max(0, e.alpha + flick);
        ctx.beginPath();
        ctx.arc(e.x, e.y, e.size, 0, Math.PI * 2);
        ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + "," + a + ")";
        ctx.fill();

        // グロー効果
        ctx.beginPath();
        ctx.arc(e.x, e.y, e.size * 3, 0, Math.PI * 2);
        ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + "," + (a * 0.15) + ")";
        ctx.fill();
      }
    };
  }

  // ========================================
  // 6. dot-grid (微細ドット明滅)
  // ========================================
  function dotGrid(primary) {
    var c = parseColor(primary);
    var SPACING = 30;
    var time = 0;

    return function () {
      ctx.clearRect(0, 0, w, h);
      time += 0.015;
      var cols = Math.ceil(w / SPACING);
      var rows = Math.ceil(h / SPACING);

      for (var row = 0; row < rows; row++) {
        for (var col = 0; col < cols; col++) {
          var x = col * SPACING + SPACING / 2;
          var y = row * SPACING + SPACING / 2;
          var wave = Math.sin(time + col * 0.15 + row * 0.15) * 0.5 + 0.5;
          var alpha = 0.05 + wave * 0.12;
          var radius = 0.8 + wave * 0.6;

          ctx.beginPath();
          ctx.arc(x, y, radius, 0, Math.PI * 2);
          ctx.fillStyle = "rgba(" + c.r + "," + c.g + "," + c.b + "," + alpha + ")";
          ctx.fill();
        }
      }
    };
  }

  // ========================================
  // アニメーション初期化
  // ========================================
  var animationFactories = {
    "particle-network": particleNetwork,
    "matrix-rain": matrixRain,
    "glitch-scanline": glitchScanline,
    "hexagon-grid": hexagonGrid,
    "rising-embers": risingEmbers,
    "dot-grid": dotGrid
  };

  function initBgAnimation(type, preset) {
    if (animationId) {
      cancelAnimationFrame(animationId);
      animationId = null;
    }
    ctx.clearRect(0, 0, w, h);
    resize();

    var primary = "#855CF9";
    if (preset && window.THEME_PRESETS && window.THEME_PRESETS[preset]) {
      primary = window.THEME_PRESETS[preset].primary;
    }

    var factory = animationFactories[type] || animationFactories["particle-network"];
    var drawFrame = factory(primary);

    function loop() {
      drawFrame();
      animationId = requestAnimationFrame(loop);
    }
    loop();
  }

  window.initBgAnimation = initBgAnimation;

  // 初期化: data-theme属性からアニメーション種別を決定
  var themeId = window.currentThemePreset || "purple-network";
  var animationType = window.getThemeAnimation ? window.getThemeAnimation(themeId) : "particle-network";
  initBgAnimation(animationType, themeId);
})();
