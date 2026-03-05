import { Record } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, obj_type, bool_type, array_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { map } from "../../../../fable_modules/fable-library-js.4.29.0/Option.js";
import { printf, toText, substring } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { map as map_1, tryFind } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
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

const allowedImageTypes = ofSeq(["image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml"], {
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
        else if (!FSharpSet__Contains(allowedImageTypes, file.type)) {
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
            const name = file.name;
            let key;
            const arg = crypto.randomUUID();
            key = toText(printf("%s/%s"))(arg)(name);
            return blobs.put(key, file).then((_arg_1) => {
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
                    "Content-Type": (contentType == null) ? "application/octet-stream" : contentType,
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

