#!/usr/bin/env node
// Pin the Android Gradle build to JDK 21.
//
// Capacitor 8's `capacitor-android` compiles with `sourceCompatibility = 21`, so Gradle must run on
// JDK 21 or `javac` fails with "invalid source release: 21". Gradle picks its JDK from
// `org.gradle.java.home` (if set), else `JAVA_HOME`, else PATH — so a machine whose global JAVA_HOME is
// an older JDK (kept for other projects) breaks the build unless we override it here.
//
// The android/ tree is generated + gitignored and its gradle.properties resets on every `cap add`, so
// this script — like register-deeplink.mjs — is the committed source of truth, re-applied idempotently
// after a scaffold/sync (wired into add:android / sync via `patch:android`). It pins the JDK
// PROJECT-LOCALLY, leaving the global JAVA_HOME untouched, and resolves the JDK home dynamically
// (macOS `/usr/libexec/java_home`) so the path isn't tied to one machine or one patch release.

import { readFileSync, writeFileSync, existsSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const REQUIRED_JDK = '21' // bump alongside the @capacitor/* majors (Cap 7+ = JDK 21)
const KEY = 'org.gradle.java.home'

const here = dirname(fileURLToPath(import.meta.url))
const propsPath = resolve(here, '..', 'android', 'gradle.properties')

if (!existsSync(propsPath)) {
    console.error(`[gradle-jdk] No ${propsPath}. Run "npx cap add android" first, then re-run "npm run gradle-jdk".`)
    process.exit(1)
}

let jdkHome
try {
    // macOS: resolve the newest installed JDK of the required major.
    jdkHome = execFileSync('/usr/libexec/java_home', ['-v', REQUIRED_JDK], { encoding: 'utf8' }).trim()
} catch {
    console.error(`[gradle-jdk] JDK ${REQUIRED_JDK} not found (/usr/libexec/java_home -v ${REQUIRED_JDK} failed).`)
    console.error(`[gradle-jdk] Install a JDK ${REQUIRED_JDK} (Temurin/Zulu/Oracle), then re-run "npm run gradle-jdk".`)
    process.exit(1)
}

let props = readFileSync(propsPath, 'utf8')
const line = `${KEY}=${jdkHome}`
const re = new RegExp(`^${KEY.replace(/\./g, '\\.')}=.*$`, 'm')

if (re.test(props)) {
    if (props.match(re)[0] === line) {
        console.log(`[gradle-jdk] Already pinned to JDK ${REQUIRED_JDK} (${jdkHome}).`)
        process.exit(0)
    }
    props = props.replace(re, line)
    console.log(`[gradle-jdk] Updated ${KEY} -> JDK ${REQUIRED_JDK} (${jdkHome}).`)
} else {
    const comment =
        `\n# Capacitor ${REQUIRED_JDK === '21' ? '8' : REQUIRED_JDK} needs JDK ${REQUIRED_JDK}; pinned project-locally so a machine whose global\n` +
        `# JAVA_HOME is an older JDK (kept for other projects) still builds this app. Managed by npm run gradle-jdk.\n`
    props = props.replace(/\s*$/, '\n') + comment + line + '\n'
    console.log(`[gradle-jdk] Pinned ${KEY} -> JDK ${REQUIRED_JDK} (${jdkHome}).`)
}
writeFileSync(propsPath, props)
