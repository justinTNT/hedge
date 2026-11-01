import {readFileSync,writeFileSync,existsSync,mkdirSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {homedir} from 'node:os';
import {appVersion,versionCode} from './version.mjs';
// The Android project is generated and untracked, so its version is stamped here from the tracked
// mobile/package.json on every sync; see version.mjs.
const gradle='android/app/build.gradle';
const version=appVersion();
writeFileSync(gradle,readFileSync(gradle,'utf8')
  .replace(/versionCode \d+/,'versionCode '+versionCode(version))
  .replace(/versionName "[^"]*"/,'versionName "'+version+'"'));
const manifest='android/app/src/main/AndroidManifest.xml';
let xml=readFileSync(manifest,'utf8');
if(!xml.includes('android:scheme="nativeplants"')) {
  const start=xml.indexOf('.MainActivity'), end=xml.indexOf('</activity>',start);
  if(start<0 || end<0) throw new Error('Cannot find MainActivity');
  xml=xml.slice(0,end)+`<intent-filter>
                <action android:name="android.intent.action.VIEW" />
                <category android:name="android.intent.category.DEFAULT" />
                <category android:name="android.intent.category.BROWSABLE" />
                <data android:scheme="nativeplants" android:host="auth" />
            </intent-filter>
        `+xml.slice(end);
}
// Do not restore encrypted credentials onto another device from Android backup.
xml=xml.replace('android:allowBackup="true"','android:allowBackup="false"');
writeFileSync(manifest,xml);
if(process.platform==='darwin') {
  const jdk=execFileSync('/usr/libexec/java_home',['-v','21'],{encoding:'utf8'}).trim();
  const path='android/gradle.properties';let text=readFileSync(path,'utf8').replace(/^org\.gradle\.java\.home=.*\n?/mg,'');
  writeFileSync(path,text+'\norg.gradle.java.home='+jdk+'\n');
}
const sdk=process.env.ANDROID_HOME || homedir()+'/Library/Android/sdk';
if(!existsSync('android/local.properties') && existsSync(sdk)) writeFileSync('android/local.properties','sdk.dir='+sdk+'\n');
const drawable='android/app/src/main/res/drawable';mkdirSync(drawable,{recursive:true});
writeFileSync(drawable+'/ic_launcher_foreground.xml',`<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="108dp" android:height="108dp" android:viewportWidth="108" android:viewportHeight="108"><path android:fillColor="#234a3b" android:pathData="M0,0h108v108h-108z"/><path android:fillColor="#e9ebcd" android:pathData="M32,72C25,46 46,29 77,30C80,56 62,80 38,74L31,83L27,80L63,43L32,72Z"/></vector>`);
