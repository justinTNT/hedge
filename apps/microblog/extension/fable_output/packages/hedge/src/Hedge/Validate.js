import { Record } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, string_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { isEmpty, map, empty, length, tryPick, contains } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { comparePrimitives, safeHash, equals } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { FieldAttr } from "./Schema.js";
import { printf, toText } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { toList } from "../../../../fable_modules/fable-library-js.4.29.0/Seq.js";
import { tryFind, ofList } from "../../../../fable_modules/fable-library-js.4.29.0/Map.js";
import { addRangeInPlace, map as map_1 } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { FSharpResult$2 } from "../../../../fable_modules/fable-library-js.4.29.0/Result.js";

export class ValidationError extends Record {
    constructor(Field, Message) {
        super();
        this.Field = Field;
        this.Message = Message;
    }
}

export function ValidationError_$reflection() {
    return record_type("Hedge.Validate.ValidationError", [], ValidationError, () => [["Field", string_type], ["Message", string_type]]);
}

function hasAttr(attr, fs) {
    return contains(attr, fs.Attrs, {
        Equals: equals,
        GetHashCode: safeHash,
    });
}

function getMinLength(fs) {
    return tryPick((_arg) => {
        if (_arg.tag === 11) {
            return _arg.fields[0];
        }
        else {
            return undefined;
        }
    }, fs.Attrs);
}

function getMaxLength(fs) {
    return tryPick((_arg) => {
        if (_arg.tag === 12) {
            return _arg.fields[0];
        }
        else {
            return undefined;
        }
    }, fs.Attrs);
}

function validateField(fieldName, fs, value) {
    let n, n_2, n_4, n_6;
    const errors = [];
    const addError = (msg) => {
        void (errors.push(new ValidationError(fieldName, msg)));
    };
    const matchValue = fs.Type;
    let matchResult;
    switch (matchValue.tag) {
        case 0: {
            matchResult = 0;
            break;
        }
        case 3: {
            if (matchValue.fields[0].tag === 0) {
                matchResult = 1;
            }
            else {
                matchResult = 3;
            }
            break;
        }
        case 4: {
            matchResult = 2;
            break;
        }
        default:
            matchResult = 3;
    }
    switch (matchResult) {
        case 0: {
            let s = value;
            if (hasAttr(new FieldAttr(8, []), fs)) {
                s = s.trim();
            }
            if (hasAttr(new FieldAttr(7, []), fs) && (s.length === 0)) {
                addError("is required");
            }
            const matchValue_1 = getMinLength(fs);
            let matchResult_1, n_1;
            if (matchValue_1 != null) {
                if ((n = (matchValue_1 | 0), s.length < n)) {
                    matchResult_1 = 0;
                    n_1 = matchValue_1;
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
                    addError(toText(printf("must be at least %d characters"))(n_1));
                    break;
                }
            }
            const matchValue_2 = getMaxLength(fs);
            let matchResult_2, n_3;
            if (matchValue_2 != null) {
                if ((n_2 = (matchValue_2 | 0), s.length > n_2)) {
                    matchResult_2 = 0;
                    n_3 = matchValue_2;
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
                    addError(toText(printf("must be at most %d characters"))(n_3));
                    break;
                }
            }
            return [s, toList(errors)];
        }
        case 1:
            if ((value == null)) {
                return [value, toList(errors)];
            }
            else {
                let s_1 = value;
                if (hasAttr(new FieldAttr(8, []), fs)) {
                    s_1 = s_1.trim();
                }
                const matchValue_3 = getMinLength(fs);
                let matchResult_3, n_5;
                if (matchValue_3 != null) {
                    if ((n_4 = (matchValue_3 | 0), s_1.length < n_4)) {
                        matchResult_3 = 0;
                        n_5 = matchValue_3;
                    }
                    else {
                        matchResult_3 = 1;
                    }
                }
                else {
                    matchResult_3 = 1;
                }
                switch (matchResult_3) {
                    case 0: {
                        addError(toText(printf("must be at least %d characters"))(n_5));
                        break;
                    }
                }
                const matchValue_4 = getMaxLength(fs);
                let matchResult_4, n_7;
                if (matchValue_4 != null) {
                    if ((n_6 = (matchValue_4 | 0), s_1.length > n_6)) {
                        matchResult_4 = 0;
                        n_7 = matchValue_4;
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
                        addError(toText(printf("must be at most %d characters"))(n_7));
                        break;
                    }
                }
                return [s_1, toList(errors)];
            }
        case 2: {
            const count = length(value) | 0;
            if (hasAttr(new FieldAttr(7, []), fs) && (count === 0)) {
                addError("is required");
            }
            const matchValue_5 = getMaxLength(fs);
            let matchResult_5, n_9;
            if (matchValue_5 != null) {
                if (count > matchValue_5) {
                    matchResult_5 = 0;
                    n_9 = matchValue_5;
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
                    addError(toText(printf("must have at most %d items"))(n_9));
                    break;
                }
            }
            return [value, toList(errors)];
        }
        default:
            return [value, empty()];
    }
}

/**
 * Walk a record's fields, validate against a TypeSchema, and
 * return either a sanitized record or a list of errors.
 */
export function validateRecord(typeInfo, ts, value) {
    const fieldSchemaMap = ofList(map((f) => [f.Name, f], ts.Fields), {
        Compare: comparePrimitives,
    });
    const allErrors = [];
    const values = map_1((tupledArg) => {
        const name = tupledArg[0];
        const rawValue = value[name];
        const matchValue = tryFind(name, fieldSchemaMap);
        if (matchValue == null) {
            return rawValue;
        }
        else {
            const patternInput = validateField(name, matchValue, rawValue);
            addRangeInPlace(patternInput[1], allErrors);
            return patternInput[0];
        }
    }, typeInfo.fields ? typeInfo.fields() : []);
    const errorList = toList(allErrors);
    if (isEmpty(errorList)) {
        return new FSharpResult$2(0, [new (typeInfo.construct)(...values)]);
    }
    else {
        return new FSharpResult$2(1, [errorList]);
    }
}

