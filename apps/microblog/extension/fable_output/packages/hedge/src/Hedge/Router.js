import { Record, Union } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { list_type, class_type, anonRecord_type, option_type, lambda_type, obj_type, record_type, bool_type, union_type, string_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { join, printf, toText, substring } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { handleBlobServe, handleBlobUpload, parseCookie } from "./Workers.js";
import { list as list_4, object, toString } from "../../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";
import { tryFind, empty, filter, map } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { value as value_3, map as map_1, bind } from "../../../../fable_modules/fable-library-js.4.29.0/Option.js";
import { equals, curry3 } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { FSharpMap__TryFind, toList, FSharpMap__ContainsKey } from "../../../../fable_modules/fable-library-js.4.29.0/Map.js";
import { fetchUserinfo, exchangeCode, verifyState, generateAuthUrl, generateState, providers } from "./OAuth.js";

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

export class MountOn extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["OnHost", "OnPath"];
    }
}

export function MountOn_$reflection() {
    return union_type("Hedge.Router.MountOn", [], MountOn, () => [[["Item", string_type]], [["Item", string_type]]]);
}

export class Mount extends Record {
    constructor(On, Shell, When) {
        super();
        this.On = On;
        this.Shell = Shell;
        this.When = When;
    }
}

export function Mount_$reflection() {
    return record_type("Hedge.Router.Mount", [], Mount, () => [["On", MountOn_$reflection()], ["Shell", string_type], ["When", lambda_type(obj_type, bool_type)]]);
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

/**
 * GET /admin/logout — clears the browser's stored admin key, then returns to
 * the key prompt. The admin SPA is a static asset and the key lives in
 * localStorage, so the worker can't clear it directly; instead it serves a
 * tiny page that does, then redirects to /admin (which now shows the login
 * field, the key being empty). This is the sign-out for tenants without the
 * browser extension (articles, music) — there's no other way to swap keys.
 */
export function logoutResponse() {
    const body = "<!doctype html><meta charset=\"utf-8\"><title>Signed out</title><script>try{localStorage.removeItem(\'adminKey\')}catch(e){}location.replace(\'/admin\')</script><noscript>Signed out. <a href=\"/admin\">Continue</a></noscript>";
    const options = {
        status: 200,
        headers: {
            "Content-Type": "text/html; charset=utf-8",
            "Cache-Control": "no-store",
        },
    };
    return new Response(body, options);
}

export function redirectResponse(url, cookie) {
    const options = {
        status: 302,
        headers: {
            Location: url,
            "Set-Cookie": cookie,
        },
    };
    return new Response("", options);
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
    return jsonResponse(toString(0, object([["errors", list_4(map((e) => object([["field", e.Field], ["message", e.Message]]), errors))]])), 422);
}

export class OAuthComplete extends Record {
    constructor(RedirectUrl, AdoptGuestId) {
        super();
        this.RedirectUrl = RedirectUrl;
        this.AdoptGuestId = AdoptGuestId;
    }
}

export function OAuthComplete_$reflection() {
    return record_type("Hedge.Router.OAuthComplete", [], OAuthComplete, () => [["RedirectUrl", string_type], ["AdoptGuestId", option_type(string_type)]]);
}

export class OAuthConfig extends Record {
    constructor(Secret, Providers, ResolveIdentity, OnOAuthComplete) {
        super();
        this.Secret = Secret;
        this.Providers = Providers;
        this.ResolveIdentity = ResolveIdentity;
        this.OnOAuthComplete = OnOAuthComplete;
    }
}

export function OAuthConfig_$reflection() {
    return record_type("Hedge.Router.OAuthConfig", [], OAuthConfig, () => [["Secret", string_type], ["Providers", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, anonRecord_type(["ClientId", string_type], ["ClientSecret", string_type])])], ["ResolveIdentity", lambda_type(class_type("Hedge.Workers.D1Database"), lambda_type(string_type, class_type("Fable.Core.JS.Promise`1", [option_type(string_type)])))], ["OnOAuthComplete", lambda_type(class_type("Hedge.Workers.D1Database"), lambda_type(class_type("Hedge.Workers.R2Bucket"), lambda_type(string_type, lambda_type(obj_type, lambda_type(string_type, class_type("Fable.Core.JS.Promise`1", [OAuthComplete_$reflection()]))))))]]);
}

export class WorkerConfig extends Record {
    constructor(Routes, Admin, OAuth, Mounts) {
        super();
        this.Routes = Routes;
        this.Admin = Admin;
        this.OAuth = OAuth;
        this.Mounts = Mounts;
    }
}

export function WorkerConfig_$reflection() {
    return record_type("Hedge.Router.WorkerConfig", [], WorkerConfig, () => [["Routes", lambda_type(class_type("Hedge.Workers.WorkerRequest"), lambda_type(obj_type, lambda_type(class_type("Hedge.Workers.ExecutionContext"), option_type(class_type("Fable.Core.JS.Promise`1", [class_type("Hedge.Workers.WorkerResponse", undefined, WorkerResponse)])))))], ["Admin", option_type(lambda_type(class_type("Hedge.Workers.WorkerRequest"), lambda_type(obj_type, lambda_type(Route_$reflection(), option_type(class_type("Fable.Core.JS.Promise`1", [class_type("Hedge.Workers.WorkerResponse", undefined, WorkerResponse)]))))))], ["OAuth", option_type(lambda_type(obj_type, OAuthConfig_$reflection()))], ["Mounts", list_type(Mount_$reflection())]]);
}

export function createWorker(config) {
    return {
        fetch: (request, env, ctx) => PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
            let clo_2, oauth_2, oauth_3;
            const route = parseRoute(request);
            if (route.tag === 4) {
                return Promise.resolve(corsPreflightResponse());
            }
            else {
                let matchResult, path_1;
                if (route.tag === 0) {
                    if (equals(matchPath("/admin/logout", route.fields[0]), new RouteMatch(0, ["/admin/logout"]))) {
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
                    case 0:
                        return Promise.resolve(logoutResponse());
                    default: {
                        const matchValue = bind((f) => f(request)(env)(route), map_1(curry3, config.Admin));
                        if (matchValue == null) {
                            const oauthCfg = map_1((f_1) => f_1(env), config.OAuth);
                            let matchResult_1, path_3;
                            if (route.tag === 0) {
                                if (equals(matchPath("/api/auth/me", route.fields[0]), new RouteMatch(0, ["/api/auth/me"]))) {
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
                                    const guest = resolveGuest(request);
                                    if (guest.IsNew) {
                                        return Promise.resolve(okJsonWithCookie("{\"guest\":null}", guestCookieValue(guest)));
                                    }
                                    else if (oauthCfg == null) {
                                        return Promise.resolve(okJsonWithCookie("{\"guest\":null}", guestCookieValue(guest)));
                                    }
                                    else {
                                        const oauth = oauthCfg;
                                        const db = env.DB;
                                        return oauth.ResolveIdentity(db, guest.GuestId).then((_arg) => {
                                            const identityJson = _arg;
                                            if (identityJson == null) {
                                                return Promise.resolve(okJsonWithCookie("{\"guest\":null}", guestCookieValue(guest)));
                                            }
                                            else {
                                                const json = identityJson;
                                                const body = toText(printf("{\"guest\":{\"guestId\":\"%s\",\"identity\":%s}}"))(guest.GuestId)(json);
                                                return Promise.resolve(okJsonWithCookie(body, guestCookieValue(guest)));
                                            }
                                        });
                                    }
                                }
                                default: {
                                    let matchResult_2, path_5;
                                    if (route.tag === 0) {
                                        if (equals(matchPath("/api/auth/providers", route.fields[0]), new RouteMatch(0, ["/api/auth/providers"]))) {
                                            matchResult_2 = 0;
                                            path_5 = route.fields[0];
                                        }
                                        else {
                                            matchResult_2 = 1;
                                        }
                                    }
                                    else {
                                        matchResult_2 = 1;
                                    }
                                    switch (matchResult_2) {
                                        case 0: {
                                            const configured = (oauthCfg != null) ? map((tuple) => tuple[0], filter((tupledArg) => {
                                                const creds = tupledArg[1];
                                                if (((FSharpMap__ContainsKey(providers, tupledArg[0]) && !(creds.ClientId == null)) && (creds.ClientId !== "")) && !(creds.ClientSecret == null)) {
                                                    return creds.ClientSecret !== "";
                                                }
                                                else {
                                                    return false;
                                                }
                                            }, toList(oauthCfg.Providers))) : empty();
                                            let body_1;
                                            const arg_3 = join(",", map((clo_2 = toText(printf("\"%s\"")), clo_2), configured));
                                            body_1 = toText(printf("{\"providers\":[%s]}"))(arg_3);
                                            return Promise.resolve(okJson(body_1));
                                        }
                                        default: {
                                            let matchResult_3, oauth_4, path_8, oauth_5, path_9;
                                            if (route.tag === 0) {
                                                if (oauthCfg != null) {
                                                    if ((oauth_2 = oauthCfg, matchPath("/api/auth/:id/login", route.fields[0]) != null)) {
                                                        matchResult_3 = 0;
                                                        oauth_4 = oauthCfg;
                                                        path_8 = route.fields[0];
                                                    }
                                                    else if ((oauth_3 = oauthCfg, matchPath("/api/auth/:id/callback", route.fields[0]) != null)) {
                                                        matchResult_3 = 1;
                                                        oauth_5 = oauthCfg;
                                                        path_9 = route.fields[0];
                                                    }
                                                    else {
                                                        matchResult_3 = 2;
                                                    }
                                                }
                                                else {
                                                    matchResult_3 = 2;
                                                }
                                            }
                                            else {
                                                matchResult_3 = 2;
                                            }
                                            switch (matchResult_3) {
                                                case 0: {
                                                    let providerName;
                                                    const matchValue_2 = value_3(matchPath("/api/auth/:id/login", path_8));
                                                    providerName = ((matchValue_2.tag === 0) ? "" : matchValue_2.fields[1]);
                                                    const matchValue_3 = FSharpMap__TryFind(providers, providerName);
                                                    const matchValue_4 = FSharpMap__TryFind(oauth_4.Providers, providerName);
                                                    let matchResult_4, creds_1, providerCfg;
                                                    if (matchValue_3 != null) {
                                                        if (matchValue_4 != null) {
                                                            matchResult_4 = 0;
                                                            creds_1 = matchValue_4;
                                                            providerCfg = matchValue_3;
                                                        }
                                                        else {
                                                            matchResult_4 = 1;
                                                        }
                                                    }
                                                    else {
                                                        matchResult_4 = 1;
                                                    }
                                                    switch (matchResult_4) {
                                                        case 0: {
                                                            const guest_1 = resolveGuest(request);
                                                            const returnTo = new URL(request.url).searchParams.get("returnTo");
                                                            const returnTo_1 = ((returnTo == null) ? true : (returnTo === "")) ? "/" : returnTo;
                                                            return generateState(oauth_4.Secret, guest_1.GuestId, returnTo_1).then((_arg_1) => {
                                                                let url, origin;
                                                                const authUrl = generateAuthUrl(providerCfg, creds_1.ClientId, (url = (new URL(request.url)), (origin = url.origin, toText(printf("%s/api/auth/%s/callback"))(origin)(providerName))), _arg_1);
                                                                return Promise.resolve(redirectResponse(authUrl, guestCookieValue(guest_1)));
                                                            });
                                                        }
                                                        default:
                                                            return Promise.resolve(badRequest(toText(printf("Unknown provider: %s"))(providerName)));
                                                    }
                                                }
                                                case 1: {
                                                    let providerName_1;
                                                    const matchValue_6 = value_3(matchPath("/api/auth/:id/callback", path_9));
                                                    providerName_1 = ((matchValue_6.tag === 0) ? "" : matchValue_6.fields[1]);
                                                    const matchValue_7 = FSharpMap__TryFind(providers, providerName_1);
                                                    const matchValue_8 = FSharpMap__TryFind(oauth_5.Providers, providerName_1);
                                                    let matchResult_5, creds_2, providerCfg_1;
                                                    if (matchValue_7 != null) {
                                                        if (matchValue_8 != null) {
                                                            matchResult_5 = 0;
                                                            creds_2 = matchValue_8;
                                                            providerCfg_1 = matchValue_7;
                                                        }
                                                        else {
                                                            matchResult_5 = 1;
                                                        }
                                                    }
                                                    else {
                                                        matchResult_5 = 1;
                                                    }
                                                    switch (matchResult_5) {
                                                        case 0: {
                                                            const guest_2 = resolveGuest(request);
                                                            const code = new URL(request.url).searchParams.get("code");
                                                            const stateParam = new URL(request.url).searchParams.get("state");
                                                            return ((code == null) ? true : (code === "")) ? (Promise.resolve(badRequest("Missing code parameter"))) : (((stateParam == null) ? true : (stateParam === "")) ? (Promise.resolve(badRequest("Missing state parameter"))) : (verifyState(oauth_5.Secret, stateParam).then((_arg_2) => {
                                                                const stateResult = _arg_2;
                                                                if (stateResult.tag === 0) {
                                                                    if (stateResult.fields[0][0] !== guest_2.GuestId) {
                                                                        return Promise.resolve(badRequest("State mismatch"));
                                                                    }
                                                                    else {
                                                                        let redirectUri_1;
                                                                        const url_1 = new URL(request.url);
                                                                        const origin_1 = url_1.origin;
                                                                        redirectUri_1 = toText(printf("%s/api/auth/%s/callback"))(origin_1)(providerName_1);
                                                                        return exchangeCode(providerCfg_1, code, redirectUri_1, creds_2.ClientId, creds_2.ClientSecret).then((_arg_3) => (fetchUserinfo(providerCfg_1, _arg_3).then((_arg_4) => {
                                                                            const db_1 = env.DB;
                                                                            const blobs = env.BLOBS;
                                                                            return oauth_5.OnOAuthComplete(db_1, blobs, guest_2.GuestId, _arg_4, stateResult.fields[0][1]).then((_arg_5) => {
                                                                                const completion = _arg_5;
                                                                                let cookieGuest;
                                                                                const matchValue_10 = completion.AdoptGuestId;
                                                                                cookieGuest = ((matchValue_10 == null) ? guest_2 : (new GuestContext(matchValue_10, guest_2.IsNew)));
                                                                                return Promise.resolve(redirectResponse(completion.RedirectUrl, guestCookieValue(cookieGuest)));
                                                                            });
                                                                        })));
                                                                    }
                                                                }
                                                                else {
                                                                    return Promise.resolve(badRequest(toText(printf("Invalid state: %s"))(stateResult.fields[0])));
                                                                }
                                                            })));
                                                        }
                                                        default:
                                                            return Promise.resolve(badRequest(toText(printf("Unknown provider: %s"))(providerName_1)));
                                                    }
                                                }
                                                default: {
                                                    let matchResult_6, path_11;
                                                    if (route.tag === 0) {
                                                        if (equals(matchPath("/api/events", route.fields[0]), new RouteMatch(0, ["/api/events"])) && ((request.headers.get('Upgrade') === 'websocket'))) {
                                                            matchResult_6 = 0;
                                                            path_11 = route.fields[0];
                                                        }
                                                        else {
                                                            matchResult_6 = 1;
                                                        }
                                                    }
                                                    else {
                                                        matchResult_6 = 1;
                                                    }
                                                    switch (matchResult_6) {
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
                                                            let matchResult_7, path_14, path_15;
                                                            switch (route.tag) {
                                                                case 1: {
                                                                    if (equals(matchPath("/api/blobs", route.fields[0]), new RouteMatch(0, ["/api/blobs"]))) {
                                                                        matchResult_7 = 0;
                                                                        path_14 = route.fields[0];
                                                                    }
                                                                    else {
                                                                        matchResult_7 = 2;
                                                                    }
                                                                    break;
                                                                }
                                                                case 0: {
                                                                    if (route.fields[0].startsWith("/blobs/")) {
                                                                        matchResult_7 = 1;
                                                                        path_15 = route.fields[0];
                                                                    }
                                                                    else {
                                                                        matchResult_7 = 2;
                                                                    }
                                                                    break;
                                                                }
                                                                default:
                                                                    matchResult_7 = 2;
                                                            }
                                                            switch (matchResult_7) {
                                                                case 0: {
                                                                    const adminKey = env.ADMIN_KEY;
                                                                    const provided = (request.headers.get("X-Admin-Key") || '');
                                                                    if ((provided !== "") && (provided === adminKey)) {
                                                                        const blobs_1 = env.BLOBS;
                                                                        return handleBlobUpload(request, blobs_1);
                                                                    }
                                                                    else {
                                                                        return Promise.resolve(unauthorized());
                                                                    }
                                                                }
                                                                case 1: {
                                                                    const blobs_2 = env.BLOBS;
                                                                    return handleBlobServe(decodeURIComponent(substring(path_15, 7)), blobs_2);
                                                                }
                                                                default: {
                                                                    const matchValue_11 = config.Routes(request, env, ctx);
                                                                    if (matchValue_11 == null) {
                                                                        if (route.tag === 0) {
                                                                            const path_16 = route.fields[0];
                                                                            const matchValue_13 = tryFind((m) => {
                                                                                if (m.When(env)) {
                                                                                    const matchValue_12 = m.On;
                                                                                    if (matchValue_12.tag === 1) {
                                                                                        const p_4 = matchValue_12.fields[0];
                                                                                        if (path_16 === p_4) {
                                                                                            return true;
                                                                                        }
                                                                                        else {
                                                                                            return path_16.startsWith(p_4 + "/");
                                                                                        }
                                                                                    }
                                                                                    else {
                                                                                        return (new URL(request.url).hostname) === matchValue_12.fields[0];
                                                                                    }
                                                                                }
                                                                                else {
                                                                                    return false;
                                                                                }
                                                                            }, config.Mounts);
                                                                            if (matchValue_13 == null) {
                                                                                return (env.ASSETS.fetch(request));
                                                                            }
                                                                            else {
                                                                                const m_1 = matchValue_13;
                                                                                return (env.ASSETS.fetch(new Request(new URL(m_1.Shell, request.url))));
                                                                            }
                                                                        }
                                                                        else {
                                                                            return Promise.resolve(notFound());
                                                                        }
                                                                    }
                                                                    else {
                                                                        const p_3 = matchValue_11;
                                                                        return p_3;
                                                                    }
                                                                }
                                                            }
                                                        }
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
                }
            }
        })),
    };
}

