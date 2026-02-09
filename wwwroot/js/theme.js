// Shinogi テーマプリセットシステム
// <head>内で同期読み込みし、FOUC（Flash of Unstyled Content）を防止する
(function () {
  "use strict";

  var PRESETS = {
    "purple-network": {
      name: "パープルネットワーク",
      bg: "#000000",
      surface: "#180530",
      surfaceInput: "#1E0A3C",
      primary: "#855CF9",
      primaryHover: "#A78BFA",
      primaryMuted: "#C4B5FD",
      border: "#2D0B4E",
      tableHeader: "#6D28D9",
      text: "#e4e7ef",
      textWhite: "#FFFFFF",
      animation: "particle-network"
    },
    "cyber-green": {
      name: "サイバーグリーン",
      bg: "#0a0a0a",
      surface: "#0d1a0d",
      surfaceInput: "#112211",
      primary: "#00FF41",
      primaryHover: "#33FF66",
      primaryMuted: "#80FF9F",
      border: "#1a3a1a",
      tableHeader: "#0d6b0d",
      text: "#d0e8d0",
      textWhite: "#FFFFFF",
      animation: "matrix-rain"
    },
    "red-hacker": {
      name: "レッドハッカー",
      bg: "#000000",
      surface: "#1a0510",
      surfaceInput: "#220818",
      primary: "#FF0055",
      primaryHover: "#FF3377",
      primaryMuted: "#FF80AA",
      border: "#3a0520",
      tableHeader: "#990033",
      text: "#e8d0d8",
      textWhite: "#FFFFFF",
      animation: "glitch-scanline"
    },
    "blue-security": {
      name: "ブルーセキュリティ",
      bg: "#000810",
      surface: "#051525",
      surfaceInput: "#081E30",
      primary: "#00D9FF",
      primaryHover: "#33E0FF",
      primaryMuted: "#80ECFF",
      border: "#0A3050",
      tableHeader: "#006B80",
      text: "#D0E8F0",
      textWhite: "#FFFFFF",
      animation: "hexagon-grid"
    },
    "orange-fire": {
      name: "オレンジファイア",
      bg: "#0a0500",
      surface: "#1a0f05",
      surfaceInput: "#22150A",
      primary: "#FF6B35",
      primaryHover: "#FF8855",
      primaryMuted: "#FFAA80",
      border: "#3A2010",
      tableHeader: "#994020",
      text: "#E8DDD0",
      textWhite: "#FFFFFF",
      animation: "rising-embers"
    },
    "dark-minimal": {
      name: "ダークミニマル",
      bg: "#000000",
      surface: "#111111",
      surfaceInput: "#1a1a1a",
      primary: "#CCCCCC",
      primaryHover: "#E0E0E0",
      primaryMuted: "#999999",
      border: "#2a2a2a",
      tableHeader: "#444444",
      text: "#d0d0d0",
      textWhite: "#FFFFFF",
      animation: "dot-grid"
    }
  };

  function hexToRgb(hex) {
    var r = parseInt(hex.slice(1, 3), 16);
    var g = parseInt(hex.slice(3, 5), 16);
    var b = parseInt(hex.slice(5, 7), 16);
    return r + "," + g + "," + b;
  }

  function applyTheme(presetId) {
    var preset = PRESETS[presetId];
    if (!preset) preset = PRESETS["purple-network"];
    var root = document.documentElement;
    root.style.setProperty("--bg", preset.bg);
    root.style.setProperty("--surface", preset.surface);
    root.style.setProperty("--surface-input", preset.surfaceInput);
    root.style.setProperty("--primary", preset.primary);
    root.style.setProperty("--primary-hover", preset.primaryHover);
    root.style.setProperty("--primary-muted", preset.primaryMuted);
    root.style.setProperty("--border", preset.border);
    root.style.setProperty("--table-header", preset.tableHeader);
    root.style.setProperty("--text", preset.text);
    root.style.setProperty("--text-white", preset.textWhite);
    root.style.setProperty("--primary-rgb", hexToRgb(preset.primary));
    root.style.setProperty("--border-rgb", hexToRgb(preset.border));
    root.style.setProperty("--table-header-rgb", hexToRgb(preset.tableHeader));
  }

  // data-theme属性からプリセットを読み取り即時適用
  var body = document.body || document.querySelector("body");
  var themeAttr = body ? body.getAttribute("data-theme") : null;
  if (!themeAttr) {
    // bodyがまだ無い場合、documentElementから取得を試みる
    var scripts = document.getElementsByTagName("script");
    for (var i = 0; i < scripts.length; i++) {
      var src = scripts[i].getAttribute("src") || "";
      if (src.indexOf("theme.js") !== -1) {
        var parentBody = scripts[i].closest("body");
        if (parentBody) themeAttr = parentBody.getAttribute("data-theme");
        break;
      }
    }
  }
  if (!themeAttr) themeAttr = "purple-network";
  applyTheme(themeAttr);

  // グローバルにエクスポート
  window.THEME_PRESETS = PRESETS;
  window.applyTheme = applyTheme;
  window.getThemeAnimation = function (presetId) {
    var preset = PRESETS[presetId];
    return preset ? preset.animation : "particle-network";
  };
  window.currentThemePreset = themeAttr;
})();
