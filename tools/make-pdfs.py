"""Render the user-facing documentation to PDF, for review.

The documentation stays Markdown in the repository; the PDFs are written outside it, by default
to C:\\GIT\\RelativeIlluminationPDFs. Each document is converted to HTML with the `markdown`
package and printed to PDF by a headless Chrome or Edge. Links between the documents are pointed
at the corresponding PDFs.

    python-embed\\python.exe tools\\make-pdfs.py [OUTPUT_DIR]

The LensHH-LT working documents (docs/lenshh-lt-*.md) are deliberately left out.
"""
import html
import os
import re
import subprocess
import sys
import tempfile

import markdown

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Source (relative to the repository), and the PDF it becomes.
DOCUMENTS = [
    ("README.md", "01 Relative Illumination Calculator.pdf"),
    ("docs/method.md", "02 Method.pdf"),
    ("macros/README.md", "03 RELILLUM macro for OpticStudio.pdf"),
    ("docs/references.md", "04 References.pdf"),
    ("docs/optiland-0.6.2.md", "05 Optiland 0.6.2 notes.pdf"),
]

BROWSERS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

CSS = """
@page { size: A4; margin: 18mm 16mm; }
body { font-family: 'Segoe UI', Calibri, Arial, sans-serif; font-size: 10.5pt; line-height: 1.45;
       color: #1a1a1a; }
h1 { font-size: 20pt; border-bottom: 2px solid #444; padding-bottom: 4px; margin-top: 0; }
h2 { font-size: 14.5pt; border-bottom: 1px solid #bbb; padding-bottom: 2px; margin-top: 22px; }
h3 { font-size: 12pt; margin-top: 18px; }
h1, h2, h3 { page-break-after: avoid; }
table { border-collapse: collapse; margin: 8px 0 12px; font-size: 9.5pt; page-break-inside: avoid; }
th, td { border: 1px solid #aaa; padding: 3px 7px; vertical-align: top; }
th { background: #eee; }
code { font-family: Consolas, 'Courier New', monospace; font-size: 9.2pt; background: #f3f3f3;
       padding: 0 2px; }
pre { background: #f5f5f5; border: 1px solid #ddd; padding: 6px 8px; font-size: 8.8pt;
      white-space: pre-wrap; page-break-inside: avoid; }
pre code { background: none; padding: 0; }
a { color: #1a4f9c; text-decoration: none; }
.source { color: #777; font-size: 8.5pt; margin-bottom: 10px; }
"""


def find_browser():
    for b in BROWSERS:
        if os.path.exists(b):
            return b
    sys.exit("Neither Chrome nor Edge was found; install one, or add its path to BROWSERS.")


def relink(text, source):
    """Point links to other documents in the set at their PDFs; leave other links alone."""
    here = os.path.dirname(source)
    targets = {os.path.normpath(src): pdf for src, pdf in DOCUMENTS}

    def fix(m):
        label, href = m.group(1), m.group(2)
        if re.match(r"^[a-z]+:", href) or href.startswith("#"):
            return m.group(0)
        path = os.path.normpath(os.path.join(here, href.split("#")[0]))
        if path in targets:
            return f"[{label}]({targets[path].replace(' ', '%20')})"
        return m.group(0)

    return re.sub(r"\[([^\]]+)\]\(([^)\s]+)\)", fix, text)


def render(source, pdf, outdir, browser, work):
    with open(os.path.join(ROOT, source), encoding="utf-8") as f:
        text = relink(f.read(), source)
    body = markdown.markdown(text, extensions=["tables", "fenced_code", "sane_lists"])
    title = re.search(r"^#\s+(.+)$", text, re.M)
    title = title.group(1) if title else os.path.basename(source)
    page = (f"<!DOCTYPE html><html><head><meta charset='utf-8'><title>{html.escape(title)}</title>"
            f"<style>{CSS}</style></head><body>"
            f"<div class='source'>{html.escape(source.replace('/', os.sep))} - RelativeIlluminationCalculator</div>"
            f"{body}</body></html>")
    page_path = os.path.join(work, os.path.splitext(pdf)[0] + ".html")
    with open(page_path, "w", encoding="utf-8") as f:
        f.write(page)
    out = os.path.join(outdir, pdf)
    subprocess.run([browser, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                    f"--print-to-pdf={out}", "file:///" + page_path.replace(os.sep, "/")],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120)
    return out


def main():
    outdir = sys.argv[1] if len(sys.argv) > 1 else r"C:\GIT\RelativeIlluminationPDFs"
    os.makedirs(outdir, exist_ok=True)
    browser = find_browser()
    with tempfile.TemporaryDirectory() as work:
        for source, pdf in DOCUMENTS:
            out = render(source, pdf, outdir, browser, work)
            print(f"{source:28s} -> {out}")


if __name__ == "__main__":
    main()
