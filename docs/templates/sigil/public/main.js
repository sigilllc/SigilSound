// Adds an "Open in REPL" link under every doc example.
// The REPL reads #code=<base64url UTF-8> from its address (the format its Share button writes), so the link is built
// from the example's own text and cannot drift from what the page shows.

function toBase64Url(text) {
  const bytes = new TextEncoder().encode(text);
  let binary = "";
  bytes.forEach(b => (binary += String.fromCharCode(b)));
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

// docfx writes the path back to the site root in this meta tag; the REPL is published at <root>/repl/.
function replBase() {
  const rel = document.querySelector('meta[name="docfx:rel"]')?.content ?? "";
  return new URL(rel + "repl/", document.baseURI).href;
}

function addReplLinks() {
  const base = replBase();
  document.querySelectorAll("article pre code").forEach(code => {
    const pre = code.parentElement;
    if (pre.dataset.replLinked || !isExample(pre)) return;
    pre.dataset.replLinked = "true";

    const link = document.createElement("a");
    link.className = "repl-link";
    link.textContent = "Open in REPL";
    link.target = "_blank";
    link.rel = "noopener";
    link.href = base + "#code=" + toBase64Url(code.textContent.replace(/\n+$/, ""));
    pre.after(link);
  });
}

// Runnable means a snippet the REPL can take as it stands. In the API reference that is a block under an "Examples"
// heading (signatures are not); in the guides it is any block, except one that starts with a using directive or an XML
// doc comment, which shows code that belongs in a project or in a source file.
function isExample(pre) {
  const text = pre.textContent.trimStart();
  if (text.startsWith("using ") || text.startsWith("///")) return false;
  if (!location.pathname.includes("/api/")) return true;
  for (let node = pre.previousElementSibling; node; node = node.previousElementSibling) {
    if (/^H[1-6]$/.test(node.tagName)) return /^examples?$/i.test(node.textContent.trim());
  }
  return false;
}

// Links to the REPL itself (the landing page's button) carry the public address in the markup, because docfx rejects a link
// to a folder it does not build. Here they are pointed at the REPL of the same site, so a local preview opens the local one.
function pointReplHomeLinks() {
  const base = replBase();
  document.querySelectorAll("a[data-repl-home]").forEach(a => (a.href = base));
}

export default {
  start() {
    pointReplHomeLinks();
    addReplLinks();
    new MutationObserver(addReplLinks).observe(document.body, { childList: true, subtree: true });
  },
};
