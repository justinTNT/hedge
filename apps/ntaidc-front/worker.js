// Cloudflare serves the static .html assets as `text/html` with NO charset, and the source HTML
// carries no <meta charset> — so browsers fall back to Latin-1 and mangle UTF-8 (→ renders as â†').
// Run the worker first, serve the asset via env.ASSETS, and pin charset=utf-8 on HTML responses.
// Content-agnostic, so it keeps working when the consultation HTML is regenerated.
export default {
  async fetch(request, env) {
    const res = await env.ASSETS.fetch(request);
    const ct = res.headers.get("content-type") || "";
    if (ct.startsWith("text/html") && !/charset/i.test(ct)) {
      const headers = new Headers(res.headers);
      headers.set("content-type", "text/html; charset=utf-8");
      return new Response(res.body, { status: res.status, statusText: res.statusText, headers });
    }
    return res;
  },
};
