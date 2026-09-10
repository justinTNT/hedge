module Models.Api

// The identity layer exposes no *reflected* HTTP endpoints — auth rides the
// framework's hand-written /api/auth/* routes (see Server/Worker.fs), not gen.
// Content endpoints (feed/item/comment/tags) come from the composed `blog` module
// (Blog.Api); rhyming is a bespoke darwin.news route (also hand-written), so it is
// deliberately NOT a reflected endpoint either.
