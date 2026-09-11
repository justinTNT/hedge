import { PromiseBuilder__For_1565554B, PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { item } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { promise } from "../../../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { class_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";

export class EventHub {
    constructor(state, _env) {
        this.state = state;
    }
    fetch(request) {
        const _ = this;
        return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
            if ((request.headers.get('Upgrade') === 'websocket')) {
                const pair = Object.values(new WebSocketPair());
                _.state.acceptWebSocket(item(1, pair));
                return Promise.resolve(new Response(null, { status: 101, webSocket: item(0, pair) }));
            }
            else {
                return request.text().then((_arg) => (PromiseBuilder__For_1565554B(promise, _.state.getWebSockets(), (_arg_1) => (PromiseBuilder__Delay_62FBFDE1(promise, () => {
                    _arg_1.send(_arg);
                    return Promise.resolve();
                }).catch((_arg_2) => {
                    return Promise.resolve();
                }))).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                    const options = {
                        status: 200,
                    };
                    return Promise.resolve(new Response("{\"ok\":true}", options));
                }))));
            }
        }));
    }
    webSocketMessage(_ws, _msg) {
    }
    webSocketClose(_ws, _code, _reason, _wasClean) {
    }
}

export function EventHub_$reflection() {
    return class_type("Hedge.EventHub.EventHub", undefined, EventHub);
}

export function EventHub_$ctor_1496597B(state, _env) {
    return new EventHub(state, _env);
}

