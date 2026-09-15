import { decodeRecordObj } from "../../../packages/hedge/src/Hedge/Codec.js";
import { Identity_$reflection, Guest_$reflection } from "../../Models/Domain.js";
import { map } from "../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { uncurry2 } from "../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { class_type } from "../../../fable_modules/fable-library-js.4.29.0/Reflection.js";

export const Decode_guest = (() => {
    const d = decodeRecordObj(Guest_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_identity = (() => {
    const d = decodeRecordObj(Identity_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export class Validate {
    constructor() {
    }
}

export function Validate_$reflection() {
    return class_type("Codecs.Validate", undefined, Validate);
}

