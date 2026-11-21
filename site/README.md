# PdfNet marketing site

This self-contained vanilla HTML/CSS/JavaScript site introduces the browser,
WPF, WinForms, WinUI, terminal, and folder-merge apps, plus the .NET library. Each has
a usage guide with commands and relevant limits. All images, styles, and
scripts are local to this directory; no build, CDN, or package install is needed.

Open `index.html` directly, or preview from the repository root:

```bash
python3 -m http.server 8000 --directory site
```

Visit `http://localhost:8000`. Navigation and guides work without JavaScript;
JavaScript enhances the mobile menu, opens linked guides, and adds code-copy
buttons when a secure-context clipboard is available. Clipboard failures show
an explicit message and select the code for manual copying.

## Hosting

Copy this entire directory to any static host, at its root or a subpath. Asset
URLs are relative; the browser-app and documentation links point to the
existing GitHub Pages deployment. Update those links and the Open Graph image
URL in `index.html` if hosting the apps elsewhere.

CI includes this directory at `site/` in the published website artifact, so
Pages serves the marketing site at `https://alexhedley.github.io/pdfnet/site/`.
The Blazor app remains at `https://alexhedley.github.io/pdfnet/`, and API docs
remain at `https://alexhedley.github.io/pdfnet/docs/`. The downloadable website ZIP
includes all three. No changes to the Blazor base path or SPA fallback are needed.

The copied brand assets come from `assets/brand/`; the app screenshots come
from `docs/screenshots/`. Refresh the copies in `site/assets/` when originals
change.

Keep the usage guides aligned with the app controls: the browser supports
repeated picker selections and file drops, every interactive app supports
reordering, and the TUI places **2. Move PDF** after **1. Add PDF**, with
**6. Exit** last.
Browser screenshots should show the picker hint and per-file up/down controls;
the terminal preview should show all six options.
