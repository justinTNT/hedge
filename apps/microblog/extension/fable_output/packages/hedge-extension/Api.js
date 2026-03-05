import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { fromString } from "../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { toString } from "../../fable_modules/fable-library-js.4.29.0/Types.js";
import { FSharpResult$2 } from "../../fable_modules/fable-library-js.4.29.0/Result.js";

function sendMessage(msg) {
    return chrome.runtime.sendMessage(msg);
}

export function fetchJson(url, decoder) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (sendMessage({
        type: "api",
        method: "GET",
        path: url,
    }).then((_arg) => {
        const raw = _arg;
        if (raw.ok) {
            const data = raw.data;
            const json = JSON.stringify(data);
            return Promise.resolve(fromString(decoder, json));
        }
        else {
            const err = raw.error;
            const msg = (err == null) ? "Request failed" : toString(err);
            return Promise.resolve(new FSharpResult$2(1, [msg]));
        }
    }))));
}

export function postJson(url, body, decoder) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const parsed = JSON.parse(body);
        return sendMessage({
            type: "api",
            method: "POST",
            path: url,
            body: parsed,
        }).then((_arg) => {
            const raw = _arg;
            if (raw.ok) {
                const data = raw.data;
                const json = JSON.stringify(data);
                return Promise.resolve(fromString(decoder, json));
            }
            else {
                const err = raw.error;
                const msg = (err == null) ? "Request failed" : toString(err);
                return Promise.resolve(new FSharpResult$2(1, [msg]));
            }
        });
    }));
}

