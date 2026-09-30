import {spawnSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {networkInterfaces} from 'node:os';
const host=process.env.DEV_HOST || Object.values(networkInterfaces()).flat().find(a=>a.family==='IPv4'&&!a.internal)?.address;
if(!host)throw new Error('Set DEV_HOST to this computer’s LAN address.');
const path='capacitor.config.json',original=readFileSync(path);
try {
  const result=spawnSync('npx',['cap','run','android','--live-reload','--host',host,'--port','3038'],{stdio:'inherit'});
  process.exitCode=result.status ?? 1;
} finally {writeFileSync(path,original);}
