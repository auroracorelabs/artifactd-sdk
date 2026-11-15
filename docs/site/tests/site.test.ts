import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile,readdir,access,stat} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const dist=fileURLToPath(new URL('../../../dist/docs/',import.meta.url)),base=process.env.DOCS_BASE_PATH??'/artifactd-sdk-client/';
const read=(p: string)=>readFile(path.join(dist,p),'utf8');
async function walk(dir: string): Promise<string[]>{const files: string[]=[];for(const e of await readdir(dir,{withFileTypes:true})){const p=path.join(dir,e.name);if(e.isDirectory())files.push(...await walk(p));else files.push(p);}return files;}
test('all guides and generated language references are present',async()=>{
 for(const file of ['index.html','getting-started/index.html','dotnet/index.html','dotnet/integration/index.html','python/index.html','node/index.html','rust/index.html','php/index.html','reference/php/index.html','delivery/index.html','examples/index.html','protocol/index.html','releasing/index.html','reference/dotnet/ArtifactD.ClientFeedback.ClientFeedbackClient.html','reference/python/artifactd_client_feedback.html','reference/node/classes/ClientFeedbackClient.html','reference/rust/artifactd_client_feedback/struct.ClientFeedbackClient.html','.nojekyll'])await access(path.join(dist,file));
});
test('guide links, fragments and assets resolve under the GitHub Pages repository path',async()=>{
 const files=(await walk(dist)).filter(f=>f.endsWith('.html')&&!f.includes(path.join(dist,'reference')+path.sep));
 for(const file of files){
  const html=await readFile(file,'utf8');
  const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);assert.equal(new Set(ids).size,ids.length,file);
  for(const m of html.matchAll(/(?:href|src)="([^"]+)"/g)){
   if(/^(https?:|mailto:|data:)/.test(m[1]))continue;
   const relative=path.relative(dist,file).replaceAll(path.sep,'/');
   const url=new URL(m[1],'https://example.test'+base+relative);
   assert.ok(url.pathname.startsWith(base),file+': '+m[1]);
   let target=path.join(dist,decodeURIComponent(url.pathname.slice(base.length)));
   if((await stat(target)).isDirectory())target=path.join(target,'index.html');
   await access(target);
   if(url.hash&&target.endsWith('.html'))assert.ok((await readFile(target,'utf8')).includes('id="'+url.hash.slice(1)+'"'),file+': '+m[1]);
  }
 }
});
test('search and AI discovery URLs use the configured project base',async()=>{
 const index=JSON.parse(await read('search.json'));assert.equal(index.length,12);
 for(const p of index){assert.ok(p.url.startsWith(base));assert.ok(p.text.length);}
 const ai=await read('llms.txt');assert.ok(ai.includes('https://github.com/auroracorelabs/artifactd-sdk-client'));
 for(const lang of ['dotnet','python','node','rust','php'])assert.ok(ai.includes(base+'reference/'+lang+'/'));
 assert.ok((await read('assets/docs.js')).includes("dataset.base + 'search.json'"));
});

test('PHP reference includes generated public configuration and reporting methods',async()=>{
 const php=await read('reference/php/index.html');
 for(const name of ['ClientFeedbackClient','reportFeatureRequest','flushPending','resolveCredentials','UploadResult','uploaded'])assert.ok(php.includes(name),name);
});
