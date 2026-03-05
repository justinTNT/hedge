import { Record, Union } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { lambda_type, option_type, obj_type, class_type, record_type, bool_type, union_type, string_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { printf, toText, substring } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { handleBlobServe, handleBlobUpload, parseCookie } from "./Workers.js";
import { list as list_1, object, toString } from "../../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";
import { map } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { map as map_1, bind } from "../../../../fable_modules/fable-library-js.4.29.0/Option.js";
import { equals, curry3 } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { item } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";

export class Route extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["GET", "POST", "PUT", "DELETE", "OPTIONS"];
    }
}

export function Route_$reflection() {
    return union_type("Hedge.Router.Route", [], Route, () => [[["Item", string_type]], [["Item", string_type]], [["Item", string_type]], [["Item", string_type]], [["Item", string_type]]]);
}

export class RouteMatch extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["Exact", "WithParam"];
    }
}

export function RouteMatch_$reflection() {
    return union_type("Hedge.Router.RouteMatch", [], RouteMatch, () => [[["Item", string_type]], [["prefix", string_type], ["param", string_type]]]);
}

export function parseRoute(request) {
    const url = new URL(request.url);
    const path = url.pathname;
    const matchValue = request.method;
    switch (matchValue) {
        case "GET":
            return new Route(0, [path]);
        case "POST":
            return new Route(1, [path]);
        case "PUT":
            return new Route(2, [path]);
        case "DELETE":
            return new Route(3, [path]);
        case "OPTIONS":
            return new Route(4, [path]);
        default:
            return new Route(0, [path]);
    }
}

export function matchPath(pattern, path) {
    if (pattern.indexOf(":id") >= 0) {
        const idIdx = pattern.indexOf(":id") | 0;
        const prefix = substring(pattern, 0, idIdx);
        const suffix = substring(pattern, idIdx + 3);
        if (path.startsWith(prefix) && path.endsWith(suffix)) {
            const paramLen = ((path.length - prefix.length) - suffix.length) | 0;
            if (paramLen > 0) {
                return new RouteMatch(1, [prefix, substring(path, prefix.length, paramLen)]);
            }
            else {
                return undefined;
            }
        }
        else {
            return undefined;
        }
    }
    else if (pattern === path) {
        return new RouteMatch(0, [path]);
    }
    else {
        return undefined;
    }
}

export class GuestContext extends Record {
    constructor(GuestId, IsNew) {
        super();
        this.GuestId = GuestId;
        this.IsNew = IsNew;
    }
}

export function GuestContext_$reflection() {
    return record_type("Hedge.Router.GuestContext", [], GuestContext, () => [["GuestId", string_type], ["IsNew", bool_type]]);
}

const guestCookieName = "hedge_guest";

const guestCookieMaxAge = 31536000;

export function resolveGuest(request) {
    const matchValue = parseCookie(guestCookieName, (request.headers.get('Cookie') || ''));
    if (matchValue == null) {
        return new GuestContext(crypto.randomUUID(), true);
    }
    else {
        return new GuestContext(matchValue, false);
    }
}

export function guestCookieValue(guest) {
    return toText(printf("%s=%s; Path=/; HttpOnly; SameSite=Lax; Max-Age=%d"))(guestCookieName)(guest.GuestId)(guestCookieMaxAge);
}

/**
 * Response helpers
 */
export function jsonResponse(body, status) {
    const options = {
        status: status,
        headers: {
            "Content-Type": "application/json",
            "Access-Control-Allow-Origin": "*",
        },
    };
    return new Response(body, options);
}

export function okJson(body) {
    return jsonResponse(body, 200);
}

export function jsonResponseWithCookie(body, status, cookie) {
    const options = {
        status: status,
        headers: {
            "Content-Type": "application/json",
            "Access-Control-Allow-Origin": "*",
            "Set-Cookie": cookie,
        },
    };
    return new Response(body, options);
}

export function okJsonWithCookie(body, cookie) {
    return jsonResponseWithCookie(body, 200, cookie);
}

export function unauthorized() {
    return jsonResponse("{\"error\":\"Unauthorized\"}", 401);
}

export function notFound() {
    return jsonResponse("{\"error\":\"Not found\"}", 404);
}

export function badRequest(msg) {
    return jsonResponse(toString(0, object([["error", msg]])), 400);
}

export function serverError(msg) {
    return jsonResponse(toString(0, object([["error", msg]])), 500);
}

export function corsPreflightResponse() {
    const options = {
        status: 204,
        headers: {
            "Access-Control-Allow-Origin": "*",
            "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
            "Access-Control-Allow-Headers": "Content-Type, X-Admin-Key",
        },
    };
    return new Response("", options);
}

export function validationErrorResponse(errors) {
    return jsonResponse(toString(0, object([["errors", list_1(map((e) => object([["field", e.Field], ["message", e.Message]]), errors))]])), 422);
}

export class WorkerConfig extends Record {
    constructor(Routes, Admin) {
        super();
        this.Routes = Routes;
        this.Admin = Admin;
    }
}

export function WorkerConfig_$reflection() {
    return record_type("Hedge.Router.WorkerConfig", [], WorkerConfig, () => [["Routes", lambda_type(class_type("Hedge.Workers.WorkerRequest"), lambda_type(obj_type, lambda_type(class_type("Hedge.Workers.ExecutionContext"), option_type(class_type("Fable.Core.JS.Promise`1", [class_type("Hedge.Workers.WorkerResponse", undefined, WorkerResponse)])))))], ["Admin", option_type(lambda_type(class_type("Hedge.Workers.WorkerRequest"), lambda_type(obj_type, lambda_type(Route_$reflection(), option_type(class_type("Fable.Core.JS.Promise`1", [class_type("Hedge.Workers.WorkerResponse", undefined, WorkerResponse)]))))))]]);
}

export function createWorker(config) {
    return {
        fetch: (request, env, ctx) => PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
            const route = parseRoute(request);
            if (route.tag === 4) {
                return Promise.resolve(corsPreflightResponse());
            }
            else {
                const matchValue = bind((f) => f(request)(env)(route), map_1(curry3, config.Admin));
                if (matchValue == null) {
                    let matchResult, path_1;
                    if (route.tag === 0) {
                        if (equals(matchPath("/api/me", route.fields[0]), new RouteMatch(0, ["/api/me"]))) {
                            matchResult = 0;
                            path_1 = route.fields[0];
                        }
                        else {
                            matchResult = 1;
                        }
                    }
                    else {
                        matchResult = 1;
                    }
                    switch (matchResult) {
                        case 0: {
                            const guest = resolveGuest(request);
                            if (guest.IsNew) {
                                return Promise.resolve(okJsonWithCookie("{\"guest\":null}", guestCookieValue(guest)));
                            }
                            else {
                                const db = env.DB;
                                const stmt = db.prepare("SELECT display_name FROM guest_sessions WHERE guest_id = ?").bind(...[guest.GuestId]);
                                return stmt.all().then((_arg) => {
                                    const result = _arg;
                                    if (result.results.length > 0) {
                                        const body = toString(0, object([["guest", object([["guestId", guest.GuestId], ["displayName", item(0, result.results).display_name]])]]));
                                        return Promise.resolve(okJsonWithCookie(body, guestCookieValue(guest)));
                                    }
                                    else {
                                        return Promise.resolve(okJsonWithCookie("{\"guest\":null}", guestCookieValue(guest)));
                                    }
                                });
                            }
                        }
                        default: {
                            let matchResult_1, path_3;
                            if (route.tag === 0) {
                                if (equals(matchPath("/api/events", route.fields[0]), new RouteMatch(0, ["/api/events"])) && ((request.headers.get('Upgrade') === 'websocket'))) {
                                    matchResult_1 = 0;
                                    path_3 = route.fields[0];
                                }
                                else {
                                    matchResult_1 = 1;
                                }
                            }
                            else {
                                matchResult_1 = 1;
                            }
                            switch (matchResult_1) {
                                case 0: {
                                    const events = env.EVENTS;
                                    const itemId = new URL(request.url).searchParams.get("itemId");
                                    if ((itemId == null) ? true : (itemId === "")) {
                                        return Promise.resolve(badRequest("Missing itemId query parameter"));
                                    }
                                    else {
                                        const doId = events.idFromName(itemId);
                                        return events.get(doId).fetch(request);
                                    }
                                }
                                default: {
                                    let matchResult_2, path_6, path_7;
                                    switch (route.tag) {
                                        case 1: {
                                            if (equals(matchPath("/api/blobs", route.fields[0]), new RouteMatch(0, ["/api/blobs"]))) {
                                                matchResult_2 = 0;
                                                path_6 = route.fields[0];
                                            }
                                            else {
                                                matchResult_2 = 2;
                                            }
                                            break;
                                        }
                                        case 0: {
                                            if (route.fields[0].startsWith("/blobs/")) {
                                                matchResult_2 = 1;
                                                path_7 = route.fields[0];
                                            }
                                            else {
                                                matchResult_2 = 2;
                                            }
                                            break;
                                        }
                                        default:
                                            matchResult_2 = 2;
                                    }
                                    switch (matchResult_2) {
                                        case 0: {
                                            const blobs = env.BLOBS;
                                            return handleBlobUpload(request, blobs);
                                        }
                                        case 1: {
                                            const blobs_1 = env.BLOBS;
                                            return handleBlobServe(substring(path_7, 7), blobs_1);
                                        }
                                        default: {
                                            const matchValue_1 = config.Routes(request, env, ctx);
                                            if (matchValue_1 == null) {
                                                return (route.tag === 0) ? ((env.ASSETS.fetch(request))) : (Promise.resolve(notFound()));
                                            }
                                            else {
                                                const p_1 = matchValue_1;
                                                return p_1;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else {
                    const p = matchValue;
                    return p;
                }
            }
        })),
    };
}

