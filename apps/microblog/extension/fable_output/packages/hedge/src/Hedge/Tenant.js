import { Record } from "../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, class_type, string_type } from "../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { FSharpSet__Contains, ofArray } from "../../../../fable_modules/fable-library-js.4.29.0/Set.js";
import { map } from "../../../../fable_modules/fable-library-js.4.29.0/Array.js";
import { comparePrimitives } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";

export class TenantConfig extends Record {
    constructor(Slug, Title, Logo, Features) {
        super();
        this.Slug = Slug;
        this.Title = Title;
        this.Logo = Logo;
        this.Features = Features;
    }
}

export function TenantConfig_$reflection() {
    return record_type("Hedge.Tenant.TenantConfig", [], TenantConfig, () => [["Slug", string_type], ["Title", string_type], ["Logo", string_type], ["Features", class_type("Microsoft.FSharp.Collections.FSharpSet`1", [string_type])]]);
}

export const config = new TenantConfig((window.SITE_SLUG || ''), (window.SITE_TITLE || ''), (window.SITE_LOGO || ''), ofArray((() => {
    const array_1 = map((s) => s.trim(), ((window.SITE_FEATURES || '')).split(","));
    return array_1.filter((s_1) => (s_1 !== ""));
})(), {
    Compare: comparePrimitives,
}));

/**
 * Is a per-tenant feature enabled? (from window.SITE_FEATURES, a comma list)
 */
export function hasFeature(feature) {
    return FSharpSet__Contains(config.Features, feature);
}

