import Worker from "./dist/server/Worker.js";
export { EventHub } from "./dist/server/EventHub.js";

// When the deployment is mounted under a sub-path (BASE_PATH, e.g. "/st"),
// strip it before dispatch so the app's routes stay root-relative.
// Spreading Worker keeps any other handlers it exports (e.g. `scheduled`).
export default {
    ...Worker,
    fetch(request, env, ctx) {
        var basePath = env.BASE_PATH || "";
        if (basePath) {
            var url = new URL(request.url);
            if (url.pathname.startsWith(basePath)) {
                url.pathname = url.pathname.slice(basePath.length) || "/";
                request = new Request(url.toString(), request);
            }
        }
        return Worker.fetch(request, env, ctx);
    }
};
