import type { RendererContext } from "vscode-notebook-renderer";

interface MermaidApi {
  initialize: (config: Record<string, unknown>) => void;
  render: (id: string, definition: string) => Promise<{ svg: string }>;
}

let mermaidApi: MermaidApi | undefined;
let renderCounter = 0;

async function ensureMermaid(): Promise<MermaidApi> {
  if (mermaidApi) {
    return mermaidApi;
  }

  // Dynamic import from the bundled mermaid module
  const mod = await import("mermaid");
  const mermaid = mod.default ?? mod;

  mermaid.initialize({
    startOnLoad: false,
    theme: document.body.classList.contains("vscode-light") ? "default" : "dark",
    securityLevel: "strict",
  });

  mermaidApi = mermaid as MermaidApi;
  return mermaidApi;
}

export async function activate(context: RendererContext<void>) {
  context.onDidCreateOutput(async (event) => {
    const element = event.element;
    const data = event.output.data;
    const mermaidSource =
      data["text/x-verso-mermaid"] ??
      new TextDecoder().decode(
        data["text/x-verso-mermaid"] as unknown as Uint8Array
      );

    if (typeof mermaidSource !== "string" || !mermaidSource.trim()) {
      element.textContent = "Empty mermaid diagram";
      return;
    }

    element.style.padding = "8px";

    try {
      const mermaid = await ensureMermaid();
      const id = `verso-mermaid-${++renderCounter}`;
      const { svg } = await mermaid.render(id, mermaidSource);
      element.innerHTML = svg;
    } catch (err) {
      element.innerHTML = `<pre style="color: var(--vscode-errorForeground, #f44);">${escapeHtml(
        String(err)
      )}</pre><pre>${escapeHtml(mermaidSource)}</pre>`;
    }
  });
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
