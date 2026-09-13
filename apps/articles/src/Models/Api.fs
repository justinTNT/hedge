module Models.Api

// The identity layer exposes no *reflected* HTTP endpoints — auth rides the
// framework's hand-written /api/auth/* routes (see Server/Worker.fs), not gen.
// Content endpoints (feed/post/comment) come from the composed `articles` module
// (Articles.Api), and on justat also the `blog` module (Blog.Api).
