import Worker from "./dist/server/Worker.js";
export { EventHub } from "./dist/server/EventHub.js";

export default {
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
