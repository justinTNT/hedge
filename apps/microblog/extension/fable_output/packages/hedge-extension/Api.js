import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { fromString } from "../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
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
            const msg = (function(e){ if (e == null) return 'Request failed'; if (typeof e === 'string') return e; if (e.error) return String(e.error); if (Array.isArray(e.errors)) return e.errors.map(function(x){ return (x.field ? x.field + ': ' : '') + x.message; }).join('; '); try { return JSON.stringify(e); } catch (_) { return String(e); } })(raw.error);
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
                const msg = (function(e){ if (e == null) return 'Request failed'; if (typeof e === 'string') return e; if (e.error) return String(e.error); if (Array.isArray(e.errors)) return e.errors.map(function(x){ return (x.field ? x.field + ': ' : '') + x.message; }).join('; '); try { return JSON.stringify(e); } catch (_) { return String(e); } })(raw.error);
                return Promise.resolve(new FSharpResult$2(1, [msg]));
            }
        });
    }));
}

/**
 * Like postJson but pins the request to an explicit site ({url, key}) instead of letting
 * the background resolve the current active site. A multi-request submission (image upload,
 * item POST, archive POST) passes one pinned site so it can't be split across tenants if
 * the active site changes mid-flight.
 */
export function postJsonPinned(site, url, body, decoder) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const parsed = JSON.parse(body);
        return sendMessage({
            type: "api",
            method: "POST",
            path: url,
            body: parsed,
            site: site,
        }).then((_arg) => {
            const raw = _arg;
            if (raw.ok) {
                const data = raw.data;
                const json = JSON.stringify(data);
                return Promise.resolve(fromString(decoder, json));
            }
            else {
                const msg = (function(e){ if (e == null) return 'Request failed'; if (typeof e === 'string') return e; if (e.error) return String(e.error); if (Array.isArray(e.errors)) return e.errors.map(function(x){ return (x.field ? x.field + ': ' : '') + x.message; }).join('; '); try { return JSON.stringify(e); } catch (_) { return String(e); } })(raw.error);
                return Promise.resolve(new FSharpResult$2(1, [msg]));
            }
        });
    }));
}

