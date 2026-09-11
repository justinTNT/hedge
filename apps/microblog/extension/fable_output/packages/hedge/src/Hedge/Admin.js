import { Record } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { lambda_type, class_type, record_type, list_type, bool_type, string_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { TypeSchema_$reflection } from "./Schema.js";
import { printf, toText, join } from "../../../../fable_modules/fable-library-js.4.29.0/String.js";
import { item, map as map_1, mapIndexed } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { isUpper } from "../../../../fable_modules/fable-library-js.4.29.0/Char.js";
import { toString, list as list_1, nil, object } from "../../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";
import { tryFind as tryFind_1, singleton, append, toArray, ofArray, empty, map } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../../../fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../../../fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { ofList, tryFind } from "../../../../fable_modules/fable-library-js.4.29.0/Map.js";
import { defaultArg, value as value_4 } from "../../../../fable_modules/fable-library-js.4.29.0/Option.js";
import { value as value_5, keyValuePairs, fromString, string, fromValue } from "../../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { equals, comparePrimitives } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { unauthorized, RouteMatch, matchPath, badRequest, notFound, okJson } from "./Router.js";
import { encodeTypeSchema } from "./SchemaCodec.js";

export class AdminTable extends Record {
    constructor(Name, Table, Schema, SelectAll, SelectOne, Insert, HasCreateTs, HasUpdateTs, Update, Delete, MutableFields) {
        super();
        this.Name = Name;
        this.Table = Table;
        this.Schema = Schema;
        this.SelectAll = SelectAll;
        this.SelectOne = SelectOne;
        this.Insert = Insert;
        this.HasCreateTs = HasCreateTs;
        this.HasUpdateTs = HasUpdateTs;
        this.Update = Update;
        this.Delete = Delete;
        this.MutableFields = MutableFields;
    }
}

export function AdminTable_$reflection() {
    return record_type("Hedge.Admin.AdminTable", [], AdminTable, () => [["Name", string_type], ["Table", string_type], ["Schema", TypeSchema_$reflection()], ["SelectAll", string_type], ["SelectOne", string_type], ["Insert", string_type], ["HasCreateTs", bool_type], ["HasUpdateTs", bool_type], ["Update", string_type], ["Delete", string_type], ["MutableFields", list_type(string_type)]]);
}

export class AdminConfig$1 extends Record {
    constructor(Tables, GetDb, CheckKey) {
        super();
        this.Tables = Tables;
        this.GetDb = GetDb;
        this.CheckKey = CheckKey;
    }
}

export function AdminConfig$1_$reflection(gen0) {
    return record_type("Hedge.Admin.AdminConfig`1", [gen0], AdminConfig$1, () => [["Tables", list_type(AdminTable_$reflection())], ["GetDb", lambda_type(gen0, class_type("Hedge.Workers.D1Database"))], ["CheckKey", lambda_type(class_type("Hedge.Workers.WorkerRequest"), lambda_type(gen0, bool_type))]]);
}

function camelCase(s) {
    if (s.length === 0) {
        return s;
    }
    else {
        return s[0].toLowerCase() + s.slice(1, s.length);
    }
}

function toSnakeCase(s) {
    return join("", mapIndexed((i, c) => {
        if ((i > 0) && isUpper(c)) {
            const arg = c.toLocaleLowerCase();
            return toText(printf("_%c"))(arg);
        }
        else {
            return c.toLocaleLowerCase();
        }
    }, s.split("")));
}

function rowToJson(schema, row) {
    return object(map((field) => {
        let matchValue;
        const col = toSnakeCase(field.Name);
        const jsonKey = camelCase(field.Name);
        const v = row[col];
        return [jsonKey, (matchValue = field.Type, (matchValue.tag === 1) ? ((v == null) ? nil : v) : ((matchValue.tag === 2) ? ((v == null) ? nil : v) : ((matchValue.tag === 3) ? ((v == null) ? nil : v) : ((matchValue.tag === 4) ? ((matchValue.fields[0].tag === 0) ? list_1(empty()) : ((v == null) ? nil : v)) : ((v == null) ? nil : v)))))];
    }, schema.Fields));
}

function genericList(db, table) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (db.prepare(table.SelectAll).all().then((_arg) => {
        const items = ofArray(map_1((row) => rowToJson(table.Schema, row), _arg.results));
        return Promise.resolve(toString(0, list_1(items)));
    }))));
}

function genericGet(db, table, id) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const stmt = db.prepare(table.SelectOne).bind(...[id]);
        return stmt.all().then((_arg) => {
            const result = _arg;
            if (result.results.length === 0) {
                return Promise.resolve(undefined);
            }
            else {
                const json = rowToJson(table.Schema, item(0, result.results));
                return Promise.resolve(toString(0, json));
            }
        });
    }));
}

function mutableArgs(table, pairMap) {
    return map((fieldName) => {
        const matchValue = tryFind(camelCase(fieldName), pairMap);
        if (matchValue == null) {
            return null;
        }
        else {
            const v = value_4(matchValue);
            const matchValue_1 = fromValue("", string, v);
            if (matchValue_1.tag === 0) {
                return matchValue_1.fields[0];
            }
            else {
                const s_1 = toString(0, v);
                if (s_1 === "null") {
                    return null;
                }
                else {
                    return s_1;
                }
            }
        }
    }, table.MutableFields);
}

function genericCreate(db, table, body) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const matchValue = fromString((path, value) => keyValuePairs(value_5, path, value), body);
        if (matchValue.tag === 0) {
            const id = crypto.randomUUID();
            const now = (Math.floor(Date.now() / 1000)) | 0;
            const allArgs = toArray(append(singleton(id), append(mutableArgs(table, ofList(matchValue.fields[0], {
                Compare: comparePrimitives,
            })), table.HasCreateTs ? singleton(now) : empty())));
            const stmt = db.prepare(table.Insert).bind(...allArgs);
            return stmt.run().then((_arg) => (genericGet(db, table, id).then((_arg_1) => (Promise.resolve(defaultArg(_arg_1, "{\"error\":\"Not found after create\"}"))))));
        }
        else {
            return Promise.resolve(toText(printf("{\"error\":\"%s\"}"))(matchValue.fields[0]));
        }
    }));
}

function genericUpdate(db, table, id, body) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const matchValue = fromString((path, value) => keyValuePairs(value_5, path, value), body);
        if (matchValue.tag === 0) {
            const allArgs = toArray(append(mutableArgs(table, ofList(matchValue.fields[0], {
                Compare: comparePrimitives,
            })), append(table.HasUpdateTs ? singleton(Math.floor(Date.now() / 1000)) : empty(), singleton(id))));
            const stmt = db.prepare(table.Update).bind(...allArgs);
            return stmt.run().then((_arg) => (genericGet(db, table, id).then((_arg_1) => (Promise.resolve(defaultArg(_arg_1, "{\"error\":\"Not found after update\"}"))))));
        }
        else {
            return Promise.resolve(toText(printf("{\"error\":\"%s\"}"))(matchValue.fields[0]));
        }
    }));
}

function genericDelete(db, table, id) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const stmt = db.prepare(table.Delete).bind(...[id]);
        return stmt.run().then((_arg) => {
            return Promise.resolve();
        });
    }));
}

function typesResponse(config) {
    return okJson(toString(0, object([["types", list_1(map((t) => object([["name", t.Name], ["schema", encodeTypeSchema(t.Schema)]]), config.Tables))]])));
}

function listResponse(db, table) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (genericList(db, table).then((_arg) => (Promise.resolve(okJson(toText(printf("{\"records\":%s}"))(_arg))))))));
}

function getResponse(db, table, id) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (genericGet(db, table, id).then((_arg) => {
        const result = _arg;
        if (result != null) {
            const json = result;
            return Promise.resolve(okJson(toText(printf("{\"record\":%s}"))(json)));
        }
        else {
            return Promise.resolve(notFound());
        }
    }))));
}

function createResponse(db, table, request) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((table.Insert === "") ? (Promise.resolve(badRequest(toText(printf("%s cannot be created from the admin (no primary key)"))(table.Name)))) : (request.text().then((_arg) => (genericCreate(db, table, _arg).then((_arg_1) => (Promise.resolve(okJson(toText(printf("{\"record\":%s}"))(_arg_1)))))))))));
}

function updateResponse(db, table, id, request) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (request.text().then((_arg) => (genericUpdate(db, table, id, _arg).then((_arg_1) => (Promise.resolve(okJson(toText(printf("{\"record\":%s}"))(_arg_1))))))))));
}

function deleteResponse(db, table, id) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (genericDelete(db, table, id).then(() => (Promise.resolve(okJson("{\"ok\":true}")))))));
}

/**
 * Try to handle an admin route. Returns Some promise if matched, None otherwise.
 * Wire from Worker.fs: `Admin = Some (fun req env route ->
 * Hedge.Admin.handleRequest AdminConfig.adminConfig req (env :?> Env) route)`.
 */
export function handleRequest(config, request, env, route) {
    let entityName;
    const db = config.GetDb(env);
    const findTable = (name) => tryFind_1((t) => (t.Name === name), config.Tables);
    const authed = () => config.CheckKey(request, env);
    switch (route.tag) {
        case 0:
            if (equals(matchPath("/api/admin/types", route.fields[0]), new RouteMatch(0, ["/api/admin/types"]))) {
                return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (Promise.resolve(typesResponse(config)))));
            }
            else {
                const matchValue = matchPath("/api/admin/:id", route.fields[0]);
                let matchResult, typeName;
                if (matchValue != null) {
                    if (matchValue.tag === 1) {
                        matchResult = 0;
                        typeName = matchValue.fields[1];
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
                        const parts = typeName.split("/");
                        switch (parts.length) {
                            case 1: {
                                const matchValue_1 = findTable(typeName);
                                if (matchValue_1 == null) {
                                    return undefined;
                                }
                                else {
                                    const table = matchValue_1;
                                    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (!authed() ? (Promise.resolve(unauthorized())) : (listResponse(db, table)))));
                                }
                            }
                            case 2: {
                                const matchValue_2 = findTable(item(0, parts));
                                if (matchValue_2 == null) {
                                    return undefined;
                                }
                                else {
                                    const table_1 = matchValue_2;
                                    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (!authed() ? (Promise.resolve(unauthorized())) : (getResponse(db, table_1, item(1, parts))))));
                                }
                            }
                            default:
                                return undefined;
                        }
                    }
                    default:
                        return undefined;
                }
            }
        case 1: {
            const matchValue_3 = matchPath("/api/admin/:id", route.fields[0]);
            let matchResult_1, entityName_1;
            if (matchValue_3 != null) {
                if (matchValue_3.tag === 1) {
                    if ((entityName = matchValue_3.fields[1], !(entityName.indexOf("/") >= 0))) {
                        matchResult_1 = 0;
                        entityName_1 = matchValue_3.fields[1];
                    }
                    else {
                        matchResult_1 = 1;
                    }
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
                    const matchValue_4 = findTable(entityName_1);
                    if (matchValue_4 == null) {
                        return undefined;
                    }
                    else {
                        const table_2 = matchValue_4;
                        return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (!authed() ? (Promise.resolve(unauthorized())) : (createResponse(db, table_2, request)))));
                    }
                }
                default:
                    return undefined;
            }
        }
        case 2: {
            const matchValue_5 = matchPath("/api/admin/:id", route.fields[0]);
            let matchResult_2, rest;
            if (matchValue_5 != null) {
                if (matchValue_5.tag === 1) {
                    matchResult_2 = 0;
                    rest = matchValue_5.fields[1];
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
                    const parts_1 = rest.split("/");
                    if (parts_1.length === 2) {
                        const matchValue_6 = findTable(item(0, parts_1));
                        if (matchValue_6 == null) {
                            return undefined;
                        }
                        else {
                            const table_3 = matchValue_6;
                            return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (!authed() ? (Promise.resolve(unauthorized())) : (updateResponse(db, table_3, item(1, parts_1), request)))));
                        }
                    }
                    else {
                        return undefined;
                    }
                }
                default:
                    return undefined;
            }
        }
        case 3: {
            const matchValue_7 = matchPath("/api/admin/:id", route.fields[0]);
            let matchResult_3, rest_1;
            if (matchValue_7 != null) {
                if (matchValue_7.tag === 1) {
                    matchResult_3 = 0;
                    rest_1 = matchValue_7.fields[1];
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
                    const parts_2 = rest_1.split("/");
                    if (parts_2.length === 2) {
                        const matchValue_8 = findTable(item(0, parts_2));
                        if (matchValue_8 == null) {
                            return undefined;
                        }
                        else {
                            const table_4 = matchValue_8;
                            return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (!authed() ? (Promise.resolve(unauthorized())) : (deleteResponse(db, table_4, item(1, parts_2))))));
                        }
                    }
                    else {
                        return undefined;
                    }
                }
                default:
                    return undefined;
            }
        }
        default:
            return undefined;
    }
}

