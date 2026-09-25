import { cpSync, mkdirSync } from 'node:fs';
mkdirSync('public', {recursive:true});
cpSync('../../packages/hedge/src/Admin/admin.css','public/admin.css');
mkdirSync('public/lib', {recursive:true});
cpSync('../../packages/hedge/lib/guest-session.js','public/lib/guest-session.js');
