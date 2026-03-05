import { Record } from "../../fable_modules/fable-library-js.4.29.0/Types.js";
import { bool_type, record_type, string_type } from "../../fable_modules/fable-library-js.4.29.0/Reflection.js";

export class GlobalConfig extends Record {
    constructor(SiteName, Features) {
        super();
        this.SiteName = SiteName;
        this.Features = Features;
    }
}

export function GlobalConfig_$reflection() {
    return record_type("Models.Config.GlobalConfig", [], GlobalConfig, () => [["SiteName", string_type], ["Features", FeatureFlags_$reflection()]]);
}

export class FeatureFlags extends Record {
    constructor(Comments, Submissions, Tags) {
        super();
        this.Comments = Comments;
        this.Submissions = Submissions;
        this.Tags = Tags;
    }
}

export function FeatureFlags_$reflection() {
    return record_type("Models.Config.FeatureFlags", [], FeatureFlags, () => [["Comments", bool_type], ["Submissions", bool_type], ["Tags", bool_type]]);
}

