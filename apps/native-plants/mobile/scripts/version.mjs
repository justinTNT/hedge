// The app's version is the mobile package version; Android's integer versionCode is derived from it
// (major*10000 + minor*100 + patch), so it rises with every bump and never needs editing by hand.
// Bump the version in mobile/package.json before building any APK that is handed out.
import {readFileSync} from 'node:fs';
export function versionCode(version) {
  const parts=String(version).split('.').map(Number);
  if(parts.length!==3 || parts.some(n=>!Number.isInteger(n) || n<0 || n>99)) throw new Error('Version must be major.minor.patch with each part 0–99: '+version);
  const [major,minor,patch]=parts;
  return major*10000+minor*100+patch;
}
export function appVersion(packageJson=new URL('../package.json',import.meta.url)) {
  return JSON.parse(readFileSync(packageJson,'utf8')).version;
}
