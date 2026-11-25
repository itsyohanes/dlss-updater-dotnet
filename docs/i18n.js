// Language picker for the site.
//
// The English text lives in index.html. Every other language has a file in
// i18n/<code>.json whose keys match the page's data-i18n attributes:
//   data-i18n="key"          replaces the element's content
//   data-i18n-alt="key"      replaces the alt attribute
//   data-i18n-content="key"  replaces the content attribute (meta tags)
// Translations can use <a>, <code> and <strong>. Each <a> takes its href and
// other attributes from the matching link in the English text, in order, so
// links are only written once. {name} is filled from the element's
// data-name attribute, and {date} from data-date as a date in that language.
//
// The language is picked from ?lang=, then the visitor's last choice, then
// the browser's languages, then English. The list matches the app's, in the
// same order: alphabetical by each language's own name, Latin script first.
(function () {
  "use strict";

  var LANGUAGES = [
    { code: "id", name: "Bahasa Indonesia", tag: "id" },
    { code: "cs", name: "Čeština", tag: "cs" },
    { code: "de", name: "Deutsch", tag: "de" },
    { code: "en", name: "English", tag: "en-AU" },
    { code: "es", name: "Español", tag: "es" },
    { code: "fr", name: "Français", tag: "fr" },
    { code: "it", name: "Italiano", tag: "it" },
    { code: "nl", name: "Nederlands", tag: "nl" },
    { code: "pl", name: "Polski", tag: "pl" },
    { code: "pt-BR", name: "Português (Brasil)", tag: "pt-BR" },
    { code: "pt", name: "Português (Portugal)", tag: "pt-PT" },
    { code: "vi", name: "Tiếng Việt", tag: "vi" },
    { code: "tr", name: "Türkçe", tag: "tr" },
    { code: "ru", name: "Русский", tag: "ru" },
    { code: "uk", name: "Українська", tag: "uk" },
    { code: "th", name: "ไทย", tag: "th" },
    { code: "ja", name: "日本語", tag: "ja" },
    { code: "zh", name: "简体中文", tag: "zh-CN" },
    { code: "zh-Hant", name: "繁體中文", tag: "zh-Hant" },
    { code: "ko", name: "한국어", tag: "ko" }
  ];
  var ENGLISH = LANGUAGES.filter(function (language) {
    return language.code === "en";
  })[0];

  var STORAGE_KEY = "lang";
  var baseUrl = new URL("i18n/", document.currentScript.src);
  var root = document.documentElement;
  var cache = {};
  var originals = new WeakMap();

  function find(code) {
    code = String(code || "").toLowerCase();
    for (var i = 0; i < LANGUAGES.length; i++) {
      if (LANGUAGES[i].code.toLowerCase() === code) return LANGUAGES[i];
    }
    return null;
  }

  // A browser language such as "de-AT" to one of ours, the same way the app
  // reads the Windows language: Traditional Chinese for Taiwan, Hong Kong,
  // Macau and "Hant", Brazilian Portuguese for Brazil.
  function match(tag) {
    var parts = String(tag || "").toLowerCase().replace(/_/g, "-").split("-");
    if (parts[0] === "zh") {
      var traditional = parts.some(function (part) {
        return ["hant", "cht", "tw", "hk", "mo"].indexOf(part) !== -1;
      });
      return find(traditional ? "zh-Hant" : "zh");
    }
    if (parts[0] === "pt") return find(parts.indexOf("br") > 0 ? "pt-BR" : "pt");
    return find(parts[0]);
  }

  function initialLanguage() {
    var fromUrl = find(new URLSearchParams(location.search).get("lang"));
    if (fromUrl) return fromUrl.code;

    try {
      var saved = find(localStorage.getItem(STORAGE_KEY));
      if (saved) return saved.code;
    } catch (e) {
      // Storage can be blocked; fall through to the browser languages.
    }

    var preferred = navigator.languages || [navigator.language];
    for (var i = 0; i < preferred.length; i++) {
      var found = match(preferred[i]);
      if (found) return found.code;
    }
    return "en";
  }

  function load(code) {
    if (code === "en") return Promise.resolve({});
    if (!cache[code]) {
      cache[code] = fetch(new URL(code + ".json", baseUrl)).then(function (response) {
        if (!response.ok) throw new Error("HTTP " + response.status);
        return response.json();
      });
      cache[code].catch(function () {
        delete cache[code];
      });
    }
    return cache[code];
  }

  // ---- Applying a language ----

  function remember(el, slot, value) {
    var saved = originals.get(el);
    if (!saved) {
      saved = {};
      originals.set(el, saved);
    }
    if (!(slot in saved)) saved[slot] = value;
    return saved[slot];
  }

  function fill(text, el, tag) {
    return text.replace(/\{(\w+)\}/g, function (whole, name) {
      if (name === "date" && el.dataset.date) {
        return new Intl.DateTimeFormat(tag, { dateStyle: "long", timeZone: "UTC" })
          .format(new Date(el.dataset.date + "T00:00:00Z"));
      }
      return name in el.dataset ? el.dataset[name] : whole;
    });
  }

  function setContent(el, html, englishHtml) {
    if (el.tagName === "TITLE") {
      el.textContent = html;
      return;
    }

    var english = document.createElement("template");
    english.innerHTML = englishHtml;
    var links = english.content.querySelectorAll("a");

    var translated = document.createElement("template");
    translated.innerHTML = html;
    translated.content.querySelectorAll("a").forEach(function (link, index) {
      var source = links[index];
      if (!source) return;
      Array.prototype.forEach.call(source.attributes, function (attr) {
        link.setAttribute(attr.name, attr.value);
      });
    });

    el.replaceChildren(translated.content);
  }

  function apply(language, strings) {
    root.lang = language.tag;

    document.querySelectorAll("[data-i18n]").forEach(function (el) {
      var english = remember(el, "html", el.tagName === "TITLE" ? el.textContent : el.innerHTML);
      var text = strings[el.dataset.i18n];
      if (language.code === "en" || !text) {
        if (el.tagName === "TITLE") el.textContent = english;
        else el.innerHTML = english;
      } else {
        setContent(el, fill(text, el, language.tag), english);
      }
    });

    [["alt", "i18nAlt"], ["content", "i18nContent"]].forEach(function (pair) {
      var attr = pair[0];
      var dataKey = pair[1];
      document.querySelectorAll("[data-" + dataKey.replace(/[A-Z]/g, "-$&").toLowerCase() + "]")
        .forEach(function (el) {
          var english = remember(el, attr, el.getAttribute(attr));
          var text = strings[el.dataset[dataKey]];
          el.setAttribute(attr, language.code !== "en" && text ? fill(text, el, language.tag) : english);
        });
    });
  }

  // ---- Flags, drawn the same way as the app's FlagIcons.cs (30 x 20) ----

  function rect(color, x, y, w, h) {
    return '<rect fill="' + color + '" x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '"/>';
  }

  function stripes(horizontal, colors) {
    var size = (horizontal ? 20 : 30) / colors.length;
    return colors.map(function (color, i) {
      return horizontal ? rect(color, 0, i * size, 30, size) : rect(color, i * size, 0, size, 20);
    }).join("");
  }

  function polygon(color, points) {
    return '<polygon fill="' + color + '" points="' + points.map(function (p) {
      return p[0].toFixed(2) + "," + p[1].toFixed(2);
    }).join(" ") + '"/>';
  }

  function star(cx, cy, r, color, tips, inner, rotation) {
    tips = tips || 5;
    inner = inner || 0.382;
    rotation = rotation === undefined ? -Math.PI / 2 : rotation;
    var points = [];
    for (var i = 0; i < tips * 2; i++) {
      var length = i % 2 === 0 ? r : r * inner;
      var angle = rotation + i * Math.PI / tips;
      points.push([cx + length * Math.cos(angle), cy + length * Math.sin(angle)]);
    }
    return polygon(color || "#FFFF00", points);
  }

  function circle(color, cx, cy, r) {
    return '<circle fill="' + color + '" cx="' + cx + '" cy="' + cy + '" r="' + r + '"/>';
  }

  function koreaBar(x, y) {
    var angle = (x < 15) === (y < 10) ? 34 : -34;
    return '<rect fill="#000000" x="' + (x - 2.5) + '" y="' + (y - 1.5) + '" width="5" height="3" transform="rotate(' +
      angle + " " + x + " " + y + ')"/>';
  }

  var FLAGS = {
    en: rect("#012169", 0, 0, 30, 20) +
      '<path d="M0 0L30 20M0 20L30 0" stroke="#FFFFFF" stroke-width="4"/>' +
      '<path d="M0 0L30 20M0 20L30 0" stroke="#C8102E" stroke-width="1.4"/>' +
      rect("#FFFFFF", 11.7, 0, 6.6, 20) + rect("#FFFFFF", 0, 6.7, 30, 6.6) +
      rect("#C8102E", 13, 0, 4, 20) + rect("#C8102E", 0, 8, 30, 4),
    de: stripes(true, ["#000000", "#DD0000", "#FFCE00"]),
    fr: stripes(false, ["#002395", "#FFFFFF", "#ED2939"]),
    es: rect("#AA151B", 0, 0, 30, 20) + rect("#F1BF00", 0, 5, 30, 10),
    it: stripes(false, ["#009246", "#FFFFFF", "#CE2B37"]),
    pl: stripes(true, ["#FFFFFF", "#DC143C"]),
    uk: stripes(true, ["#0057B7", "#FFD700"]),
    zh: rect("#EE1C25", 0, 0, 30, 20) + star(5, 5, 3) + star(10, 2, 1) + star(12, 4, 1) +
      star(12, 7, 1) + star(10, 9, 1),
    ru: stripes(true, ["#FFFFFF", "#0039A6", "#D52B1E"]),
    ja: rect("#FFFFFF", 0, 0, 30, 20) + '<circle fill="#BC002D" cx="15" cy="10" r="6"/>',
    ko: rect("#FFFFFF", 0, 0, 30, 20) +
      '<path fill="#CD2E3A" d="M10 10A5 5 0 0 1 20 10Z"/>' +
      '<path fill="#0047A0" d="M10 10A5 5 0 0 0 20 10Z"/>' +
      koreaBar(5, 3.5) + koreaBar(25, 3.5) + koreaBar(5, 16.5) + koreaBar(25, 16.5),
    cs: stripes(true, ["#FFFFFF", "#D7141A"]) + polygon("#11457E", [[0, 0], [15, 10], [0, 20]]),
    nl: stripes(true, ["#AE1C28", "#FFFFFF", "#21468B"]),
    pt: rect("#006600", 0, 0, 12, 20) + rect("#FF0000", 12, 0, 18, 20) +
      '<circle fill="#FFCC00" cx="12" cy="10" r="4.5"/>',
    id: stripes(true, ["#CE1126", "#FFFFFF"]),
    th: stripes(true, ["#A51931", "#F4F5F8", "#2D2A4A", "#2D2A4A", "#F4F5F8", "#A51931"]),
    tr: rect("#E30A17", 0, 0, 30, 20) + circle("#FFFFFF", 10, 10, 5) + circle("#E30A17", 11.25, 10, 4) +
      star(17.4, 10, 2.5, "#FFFFFF", 5, 0.382, Math.PI),
    vi: rect("#DA251D", 0, 0, 30, 20) + star(15, 10.5, 6),
    "pt-BR": rect("#009C3B", 0, 0, 30, 20) + polygon("#FFDF00", [[2.5, 10], [15, 1.7], [27.5, 10], [15, 18.3]]) +
      circle("#002776", 15, 10, 5),
    "zh-Hant": rect("#FE0000", 0, 0, 30, 20) + rect("#000095", 0, 0, 15, 10) +
      star(7.5, 5, 3.75, "#FFFFFF", 12, 0.55) + circle("#000095", 7.5, 5, 2.1) + circle("#FFFFFF", 7.5, 5, 1.8)
  };

  function flagSvg(code) {
    return '<svg viewBox="0 0 30 20" width="21" height="14" focusable="false">' + (FLAGS[code] || "") + "</svg>";
  }

  // ---- The app demo ----
  //
  // assets/app-demo.svg is drawn in English, which is also what the README
  // shows. For other languages the page loads that SVG, puts in the app's own
  // strings from the language file's "demo" object (on elements marked
  // data-k), shows that language's flag on the menu bar, sizes the buttons to
  // fit their text the way the app does, and shows the result instead.

  var XML_NS = "http://www.w3.org/XML/1998/namespace";
  var DEMO_FONT = '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';

  // Each button's right edge (or the button it sits 8px left of) and its
  // MinWidth from MainWindow.xaml. Buttons get 16px padding on each side.
  var DEMO_BUTTONS = [
    { name: "browse", right: 688, min: 88 },
    { name: "details", right: 688, min: 90 },
    { name: "delete", right: 688, min: 130 },
    { name: "restore", before: "delete", min: 135 },
    { name: "update", right: 707, min: 110 }
  ];

  var demoImg = null;
  var demoSrc = null;
  var demoSource = null;
  var demoUrl = null;
  var measureContext = null;

  function measure(text, size, weight) {
    if (!measureContext) measureContext = document.createElement("canvas").getContext("2d");
    measureContext.font = weight + " " + size + "px " + DEMO_FONT;
    return measureContext.measureText(text).width;
  }

  function round(value) {
    return String(Math.round(value * 10) / 10);
  }

  function loadDemo() {
    if (!demoSource) {
      demoSource = fetch(demoSrc).then(function (response) {
        if (!response.ok) throw new Error("HTTP " + response.status);
        return response.text();
      });
      demoSource.catch(function () {
        demoSource = null;
      });
    }
    return demoSource;
  }

  function translateDemo(source, language, text, title) {
    var doc = new DOMParser().parseFromString(source, "image/svg+xml");
    var svg = doc.documentElement;
    svg.setAttribute("lang", language.tag);
    svg.setAttributeNS(XML_NS, "xml:lang", language.tag);

    var titleEl = svg.querySelector("title");
    if (titleEl && title) titleEl.textContent = title;

    svg.querySelectorAll("text[data-k], tspan[data-k]").forEach(function (el) {
      var value = text[el.getAttribute("data-k")];
      if (typeof value !== "string") return;
      var node = el.firstChild;
      if (node && node.nodeType === 3) node.nodeValue = value;
      else el.insertBefore(doc.createTextNode(value), node);
    });

    // Menu bar: File, then the flag and Language, then Help, 20px apart.
    var flag = svg.querySelector('svg[data-k="flag"]');
    var languageMenu = svg.querySelector('text[data-k="menuLanguage"]');
    var helpMenu = svg.querySelector('text[data-k="menuHelp"]');
    var flagX = 18 + measure(text.menuFile, 13, 400) + 20;
    var languageX = flagX + 25;
    flag.setAttribute("x", round(flagX));
    flag.replaceChildren.apply(flag, Array.prototype.map.call(
      new DOMParser().parseFromString('<svg xmlns="http://www.w3.org/2000/svg">' + FLAGS[language.code] + "</svg>",
        "image/svg+xml").documentElement.childNodes,
      function (node) {
        return doc.importNode(node, true);
      }));
    languageMenu.setAttribute("x", round(languageX));
    helpMenu.setAttribute("x", round(languageX + measure(text.menuLanguage, 13, 400) + 20));

    // Buttons, right-aligned and as wide as their longest label needs.
    var lefts = {};
    DEMO_BUTTONS.forEach(function (button) {
      var rects = svg.querySelectorAll('rect[data-btn="' + button.name + '"]');
      var labels = svg.querySelectorAll('text[data-btn="' + button.name + '"]');
      var width = button.min;
      labels.forEach(function (label) {
        var weight = /font-weight:\s*600/.test(label.getAttribute("style") || "") ? 600 : 400;
        width = Math.max(width, Math.ceil(measure(label.textContent, 14, weight) + 32));
      });
      var right = button.before ? lefts[button.before] - 8 : button.right;
      var left = right - width;
      lefts[button.name] = left;

      rects.forEach(function (rect) {
        // Outlined buttons sit on half pixels so their 1px border is sharp.
        var half = parseFloat(rect.getAttribute("x")) % 1;
        rect.setAttribute("x", round(left + half));
        rect.setAttribute("width", round(width - 2 * half));
      });
      labels.forEach(function (label) {
        label.setAttribute("x", round(left + width / 2));
      });
      if (button.name === "update") {
        var press = svg.querySelector(".press");
        if (press) press.setAttribute("style", "transform-origin: " + round(left + width / 2) + "px 653.5px");
      }
    });

    // The folder box ends 8px before Browse.
    svg.querySelectorAll("rect[data-field]").forEach(function (rect) {
      rect.setAttribute("width", round(lefts.browse - 8 - 49));
    });

    return new XMLSerializer().serializeToString(doc);
  }

  function setDemo(src) {
    var old = demoUrl;
    demoUrl = src.indexOf("blob:") === 0 ? src : null;
    if (demoImg.getAttribute("src") !== src) demoImg.setAttribute("src", src);
    if (old && old !== src) URL.revokeObjectURL(old);
  }

  function showDemo(language, strings) {
    if (!demoImg) return;
    if (language.code === "en" || !strings.demo) {
      setDemo(demoSrc);
      return;
    }
    loadDemo().then(function (source) {
      if (current !== language) return;
      var svg = translateDemo(source, language, strings.demo, strings.demoAlt);
      setDemo(URL.createObjectURL(new Blob([svg], { type: "image/svg+xml" })));
    }).catch(function () {
      if (current === language) setDemo(demoSrc);
    });
  }

  // ---- Start-up ----

  var current = find(initialLanguage());
  var pending = load(current.code);

  // Hide the page briefly while a translation loads, so English doesn't
  // flash first. Never longer than 1.5 seconds.
  if (current.code !== "en") {
    root.classList.add("i18n-pending");
    setTimeout(function () {
      root.classList.remove("i18n-pending");
    }, 1500);
  }

  function updateUrl(code) {
    var url = new URL(location.href);
    if (!url.searchParams.has("lang")) return;
    url.searchParams.set("lang", code);
    history.replaceState(history.state, "", url);
  }

  document.addEventListener("DOMContentLoaded", function () {
    var picker = document.querySelector(".lang-picker");
    var select = document.getElementById("lang-select");
    var flag = picker.querySelector(".lang-flag");

    demoImg = document.querySelector("img[data-demo]");
    if (demoImg) {
      demoSrc = demoImg.getAttribute("src");
      if (current.code !== "en") loadDemo().catch(function () {});
    }

    LANGUAGES.forEach(function (language) {
      var option = document.createElement("option");
      option.value = language.code;
      option.lang = language.tag;
      option.textContent = language.name;
      select.appendChild(option);
    });

    function show(language, strings) {
      apply(language, strings);
      select.value = language.code;
      flag.innerHTML = flagSvg(language.code);
      showDemo(language, strings);
      root.classList.remove("i18n-pending");
    }

    picker.hidden = false;
    flag.innerHTML = flagSvg(current.code);
    select.value = current.code;

    pending.then(function (strings) {
      show(current, strings);
    }, function () {
      current = ENGLISH;
      show(current, {});
    });

    select.addEventListener("change", function () {
      var language = find(select.value);
      if (!language) return;
      try {
        localStorage.setItem(STORAGE_KEY, language.code);
      } catch (e) {
        // Choice just won't be remembered.
      }
      updateUrl(language.code);
      current = language;
      load(language.code).then(function (strings) {
        if (current === language) show(language, strings);
      }, function () {
        var shown = LANGUAGES.filter(function (language) {
          return language.tag === root.lang;
        })[0];
        select.value = (shown || ENGLISH).code;
      });
    });
  });
})();
