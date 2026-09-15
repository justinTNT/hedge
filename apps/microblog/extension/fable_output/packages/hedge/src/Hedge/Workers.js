import { Record } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, obj_type, bool_type, array_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { some, map } from "../../../../fable_modules/fable-library-js.4.29.0/Option.js";
import { printf, toText, substring } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { item, map as map_1, tryFind } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { FSharpSet__Contains, ofSeq } from "../../../../fable_modules/fable-library-js.4.29.0/Set.js";
import { comparePrimitives } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";

export class D1Result$1 extends Record {
    constructor(results, success, meta) {
        super();
        this.results = results;
        this.success = success;
        this.meta = meta;
    }
}

export function D1Result$1_$reflection(gen0) {
    return record_type("Hedge.Workers.D1Result`1", [gen0], D1Result$1, () => [["results", array_type(gen0)], ["success", bool_type], ["meta", obj_type]]);
}

export function parseCookie(name, cookieHeader) {
    return map((s_2) => substring(s_2, name.length + 1), tryFind((s_1) => s_1.startsWith(name + "="), map_1((s) => s.trim(), cookieHeader.split(";"))));
}

export function optToDb(v) {
    if (v == null) {
        return null;
    }
    else {
        return v;
    }
}

export function rowStr(row, key) {
    return row[key];
}

export function rowInt(row, key) {
    return row[key];
}

export function rowStrOpt(row, key) {
    const v = row[key];
    if (v == null) {
        return undefined;
    }
    else {
        return v;
    }
}

export function rowIntOpt(row, key) {
    const v = row[key];
    if (v == null) {
        return undefined;
    }
    else {
        return v;
    }
}

export function rowBool(row, key) {
    return rowInt(row, key) !== 0;
}

export function optIntToDb(v) {
    if (v == null) {
        return null;
    }
    else {
        return v;
    }
}

export const allowedImageTypes = ofSeq(["image/jpeg", "image/png", "image/gif", "image/webp", "image/avif", "image/svg+xml"], {
    Compare: comparePrimitives,
});

export function handleBlobUpload(request, blobs) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (request.formData().then((_arg) => {
        const file = _arg.get("file");
        if (file == null) {
            const options = {
                status: 400,
                headers: {
                    "Content-Type": "application/json",
                    "Access-Control-Allow-Origin": "*",
                },
            };
            return Promise.resolve(new Response("{\"error\":\"Missing file field\"}", options));
        }
        else {
            const mime = file.type;
            if (!FSharpSet__Contains(allowedImageTypes, mime)) {
                const options_1 = {
                    status: 400,
                    headers: {
                        "Content-Type": "application/json",
                        "Access-Control-Allow-Origin": "*",
                    },
                };
                return Promise.resolve(new Response("{\"error\":\"Unsupported image type\"}", options_1));
            }
            else {
                const name = (file.name).replace(/[^A-Za-z0-9._-]/g, '-');
                let key;
                const arg = crypto.randomUUID();
                key = toText(printf("%s/%s"))(arg)(name);
                return (blobs.put(key, file, { httpMetadata: { contentType: mime } })).then((_arg_1) => {
                    const body = toText(printf("{\"url\":\"/blobs/%s\"}"))(key);
                    const options_2 = {
                        status: 200,
                        headers: {
                            "Content-Type": "application/json",
                            "Access-Control-Allow-Origin": "*",
                        },
                    };
                    return Promise.resolve(new Response(body, options_2));
                });
            }
        }
    }))));
}

export function handleBlobServe(key, blobs) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (blobs.get(key).then((_arg) => {
        const objOpt = _arg;
        if (objOpt != null) {
            const obj = objOpt;
            const contentType = obj.httpMetadata["contentType"];
            const options_1 = {
                status: 200,
                headers: {
                    "Content-Type": (contentType == null) ? ((function(k){var e=(k.split('.').pop()||'').toLowerCase();return ({png:'image/png',jpg:'image/jpeg',jpeg:'image/jpeg',gif:'image/gif',webp:'image/webp',avif:'image/avif',svg:'image/svg+xml'})[e]||'application/octet-stream';})(key)) : contentType,
                    "Cache-Control": "public, max-age=31536000, immutable",
                },
            };
            return Promise.resolve(new Response(obj.body, options_1));
        }
        else {
            const options = {
                status: 404,
                headers: {
                    "Content-Type": "application/json",
                    "Access-Control-Allow-Origin": "*",
                },
            };
            return Promise.resolve(new Response("{\"error\":\"Not found\"}", options));
        }
    }))));
}

/**
 * Sign a message with HMAC-SHA256, returning a hex string.
 */
export function hmacSha256(secret, message) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const keyData = new TextEncoder().encode(secret);
        return (crypto.subtle.importKey('raw', keyData, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign', 'verify'])).then((_arg) => ((crypto.subtle.sign('HMAC', _arg, (new TextEncoder().encode(message)))).then((_arg_1) => (Promise.resolve(Array.from(new Uint8Array(_arg_1)).map(b => b.toString(16).padStart(2, '0')).join(''))))));
    }));
}

/**
 * Fetch a remote image and copy it into R2, returning a local "/blobs/<key>" path (or the
 * original url on any failure). Content-addressed by source URL (`keyPrefix/<hash>`), so
 * re-hosting the same URL is idempotent and dedup'd — which keeps handleBlobServe's immutable
 * cache header honest. Best-effort: a bad/non-image/unreachable URL returns the url unchanged,
 * so a flaky third-party host never breaks the caller. `allowed` gates the content type.
 * The shared primitive behind avatar caching and item-image rehosting.
 */
export function rehostRemoteImage(blobs, keyPrefix, allowed, url) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((((url == null) ? true : (url === "")) ? true : !url.startsWith("https://")) ? (Promise.resolve(url)) : (PromiseBuilder__Delay_62FBFDE1(promise, () => (hmacSha256("hedge-avatar", url).then((_arg) => {
        let key;
        const arg_1 = substring(_arg, 0, 32);
        key = toText(printf("%s/%s"))(keyPrefix)(arg_1);
        return blobs.get(key).then((_arg_1) => ((_arg_1 == null) ? ((fetch(url, {})).then((_arg_2) => {
            const response = _arg_2;
            if (!response.ok) {
                return Promise.resolve(url);
            }
            else {
                const raw = response.headers.get("content-type");
                const contentType = (raw == null) ? "" : item(0, raw.split(";")).trim().toLowerCase();
                return !FSharpSet__Contains(allowed, contentType) ? (Promise.resolve(url)) : ((response.arrayBuffer()).then((_arg_3) => ((blobs.put(key, _arg_3, { httpMetadata: { contentType: contentType } })).then((_arg_4) => (Promise.resolve(toText(printf("/blobs/%s"))(key)))))));
            }
        })) : (Promise.resolve(toText(printf("/blobs/%s"))(key)))));
    }))).catch((_arg_5) => {
        console.error(some("rehost image failed: " + _arg_5.message));
        return Promise.resolve(url);
    })))));
}

