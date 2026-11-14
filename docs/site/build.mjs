import {readFile,writeFile,mkdir,rm,cp,access,readdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Marked} from 'marked';
const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(here,'../..'),dist=path.join(root,'dist/docs');
const base=process.env.DOCS_BASE_PATH ?? '/artifactd-sdk/';
if(!/^\/(?:[A-Za-z0-9._-]+\/)*$/.test(base))throw new Error('DOCS_BASE_PATH must be a slash-delimited URL path');
const origin=process.env.DOCS_ORIGIN ?? 'https://auroracorelabs.github.io';
const repo='https://github.com/auroracorelabs/artifactd-sdk';
const version=(await readFile(path.join(root,'VERSION'),'utf8')).trim();
const esc=s=>s.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
const url=route=>base+route.replace(/^\//,'');
const entries=[
 ['','docs/site/content/index.md','Start'],['getting-started/','docs/site/content/getting-started.md','Start'],['delivery/','docs/site/content/delivery.md','Start'],
 ['dotnet/','packages/dotnet/README.md','Language guides'],
 ['python/','packages/python/README.md','Language guides'],['node/','packages/node/README.md','Language guides'],['rust/','packages/rust/README.md','Language guides'],
 ['php/','packages/php/README.md','Language guides'],
 ['protocol/','spec/README.md','Reference'],['releasing/','docs/RELEASING.md','Contributing'],['documentation/','docs/PAGES.md','Contributing'],
];
const pages=await Promise.all(entries.map(async([route,source,group])=>({route,source,group,text:await readFile(path.join(root,source),'utf8')})));
const fence=String.fromCharCode(96).repeat(3);
const rootReadme=await readFile(path.join(root,'README.md'),'utf8');
const environmentTable=rootReadme.match(/\| Variable \| Value \|[\s\S]*?(?=\n\n)/)?.[0];
if(!environmentTable)throw new Error('Missing shared SDK environment table in README.md');
const gettingStarted=pages.find(p=>p.route==='getting-started/');
const sdkNotice=rootReadme.match(/^> \[!IMPORTANT\]\n(?:>.*\n?)+/m)?.[0]??'';
gettingStarted.text=gettingStarted.text.replace('{{ sdk-notice }}',sdkNotice.trim());
gettingStarted.text=gettingStarted.text.replace('{{ sdk-environment }}',environmentTable.replace('defaults are separate for each language below','see the runnable example for your language'));
const dotnetIntegrationSource='packages/dotnet/ArtifactD.ClientFeedback/README.md';
const dotnetPage=pages.find(p=>p.route==='dotnet/');
const compatibility=dotnetPage.text.split('\nSee the [integration guide]')[0].trimEnd();
dotnetPage.text=compatibility+'\n\n## Integration guide\n\n'+(await readFile(path.join(root,dotnetIntegrationSource),'utf8')).replace(/^# .+\n\n[^\n]+\n\n/, '');
const languageExamples=[
 ['python/','python','Python','python'],
 ['node/','js','Node.js','node-js'],
 ['rust/','rust,no_run','Rust','rust'],
 ['php/','php','PHP','php'],
];
for(const [route,language,label,anchor] of languageExamples){
 const page=pages.find(p=>p.route===route);
 const sample=new RegExp(fence+language+'\\n[\\s\\S]*?'+fence);
 if(!sample.test(page.text))throw new Error('Missing '+label+' quick-start example in '+page.source);
 const heading=page.text.includes('## Quick start')?'':'## Quick start\n\n';
 page.text=page.text.replace(sample,heading+'Follow the [shared setup](/getting-started/) and open the [runnable '+label+' example](/examples/#'+anchor+') for the complete configuration, reporting and flush code. The generated API reference lists all supported options.');
}
// Installation commands remain in language guides; downloading is explained once.
for(const page of pages.filter(p=>['dotnet/','php/'].includes(p.route))){
 page.text=page.text.replace(/Download \*\*(?:dotnet-packages|php-packages)\*\*[^\n]*\n/, 'Install the package as described in [Install the SDK](/getting-started/#install-the-sdk).\n');
}
const exampleCommands={
 '.NET':'dotnet run --project packages/dotnet/samples/ArtifactD.ClientFeedback.Sample',
 'Python':'python -m pip install ./packages/python\npython examples/python/feedback.py',
 'Node.js':'npm install --prefix examples/node\nnode examples/node/feedback.mjs',
 'Rust':'cargo run --manifest-path examples/rust/Cargo.toml',
 'PHP':'composer install --working-dir=examples/php\nphp examples/php/feedback.php',
};
const examples=[
 ['.NET','csharp','packages/dotnet/samples/ArtifactD.ClientFeedback.Sample/Program.cs'],
 ['Python','python','examples/python/feedback.py'],['Node.js','js','examples/node/feedback.mjs'],['Rust','rust','examples/rust/src/main.rs'],['PHP','php','examples/php/feedback.php'],
];
let exampleText='# Runnable examples\n\nSet the environment variables in the [quick start](/getting-started/) and install your language package before running these examples. These are the actual source files in this repository, included on every documentation build.\n\n';
for(const [label,language,file] of examples)exampleText+='## '+label+'\n\n[Source on GitHub]('+repo+'/blob/main/'+file+')\n\n'+fence+language+'\n'+await readFile(path.join(root,file),'utf8')+'\n'+fence+'\n\nRun from the repository checkout after setting the shared environment variables:\n\n'+fence+'sh\n'+exampleCommands[label]+'\n'+fence+'\n\n';
pages.splice(3,0,{route:'examples/',source:'docs/site/content/examples.md',group:'Start',text:exampleText});
const navigationTitles={
 '':'Overview','getting-started/':'Getting started','delivery/':'Queues and delivery','examples/':'Runnable examples',
 'dotnet/':'.NET','python/':'Python','node/':'Node.js / TypeScript','rust/':'Rust','php/':'PHP',
 'protocol/':'Protocol contract','releasing/':'Build and release','documentation/':'Documentation maintenance',
};
for(const p of pages)p.title=p.text.match(/^# (.+)$/m)?.[1]??p.route;
const references=[
 ['.NET API','reference/dotnet/index.html'],
 ['Python API','reference/python/artifactd_client_feedback.html'],
 ['Node.js / TypeScript API','reference/node/index.html'],
 ['Rust API','reference/rust/artifactd_client_feedback/index.html'],
 ['PHP API','reference/php/index.html'],
];
function resolveLink(href,page){
 if(/^(?:https?:|mailto:|data:|#)/.test(href))return href;
 if(href.startsWith('/'))return url(href);
 const hash=href.indexOf('#'),fragment=hash>=0?href.slice(hash):'',file=hash>=0?href.slice(0,hash):href;
 const source=path.posix.normalize(path.posix.join(path.posix.dirname(page.source),file));
 const target=pages.find(p=>p.source===source||(p.route==='dotnet/'&&source===dotnetIntegrationSource)||(p.route==='getting-started/'&&source==='README.md'));
 return target?url(target.route)+(fragment||(source===dotnetIntegrationSource?'#integration-guide':'')):repo+'/blob/main/'+source+fragment;
}
await rm(dist,{recursive:true,force:true});await mkdir(dist,{recursive:true});
await cp(path.join(here,'public'),dist,{recursive:true});
await mkdir(path.join(dist,'reference'),{recursive:true});
for(const language of ['dotnet','python','node','php'])await cp(path.join(root,'build/docs-api',language),path.join(dist,'reference',language),{recursive:true});
await cp(path.join(root,'build/docs-rust/doc'),path.join(dist,'reference/rust'),{recursive:true});
// DocFX's build manifest contains workstation paths and is not a serving asset.
await rm(path.join(dist,'reference/dotnet/manifest.json'),{force:true});
async function adjustReferenceLinks(directory) {
 for(const entry of await readdir(directory,{withFileTypes:true})) {
  const file=path.join(directory,entry.name);
  if(entry.isDirectory())await adjustReferenceLinks(file);
  else if(file.endsWith('.html')) {
   let html=await readFile(file,'utf8');
   html=html.replace(/href="\.\.\/\.\.\/index\.html"([^>]*>SDK guides)/g,'href="'+base+'"$1');
   html=html.replaceAll('href="https://auroracorelabs.github.io/artifactd-sdk/"','href="'+base+'"');
   await writeFile(file,html);
  }
 }
}
await adjustReferenceLinks(path.join(dist,'reference'));
for(const [,file] of references)await access(path.join(dist,file));
for(const page of pages){
 const toc=[],counts=new Map();
 const markdown=new Marked({gfm:true,renderer:{
  link({href,title,tokens}){return '<a href="'+esc(resolveLink(href,page))+'"'+(title?' title="'+esc(title)+'"':'')+'>'+this.parser.parseInline(tokens)+'</a>';},
  heading({tokens,depth}){
   const html=this.parser.parseInline(tokens),text=html.replace(/<[^>]+>/g,'');
   const slug=text.toLowerCase().replace(/&[^;]+;/g,'-').replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,''),n=counts.get(slug)??0;counts.set(slug,n+1);
   const id=slug+(n?'-'+n:'');if(depth>1&&depth<4)toc.push({id,text,depth});
   return '<h'+depth+' id="'+id+'">'+html+'<a class="anchor" href="#'+id+'" aria-label="Link to '+esc(text)+'">#</a></h'+depth+'>';
  },
 }});
 const content=markdown.parse(page.text);
 let nav='';
 for(const group of [...new Set(pages.map(p=>p.group))])nav+='<section><p class="nav-label">'+group+'</p>'+pages.filter(p=>p.group===group).map(p=>'<a href="'+url(p.route)+'"'+(p===page?' aria-current="page"':'')+'>'+esc(navigationTitles[p.route]??p.title)+'</a>').join('')+'</section>';
 nav+='<section><p class="nav-label">Generated API</p>'+references.map(([title,route])=>'<a href="'+url(route)+'">'+title+' ↗</a>').join('')+'</section>';
 const brand='<img src="'+url('assets/favicon.svg')+'" width="36" height="36" alt=""><span>Artifact <span class="brand-product">Repository Manager</span></span><span class="badge">SDKs</span>';
 const html='<!doctype html><html lang="en" data-base="'+base+'"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="theme-color" content="#0b1f2a"><title>'+esc(page.title)+' · Artifactd SDK</title><meta name="description" content="Artifactd Client Feedback SDK documentation: installation, examples and API references for .NET, Python, Node.js, Rust and PHP."><link rel="canonical" href="'+origin+url(page.route)+'"><link rel="icon" href="'+url('assets/favicon.svg')+'"><link rel="stylesheet" href="'+url('assets/docs.css')+'"><script>try{const t=localStorage.getItem("artifactd-theme");document.documentElement.dataset.theme=t==="light"||t==="dark"?t:matchMedia("(prefers-color-scheme:light)").matches?"light":"dark"}catch{}</script><script src="'+url('assets/docs.js')+'" defer></script></head><body><a class="skip" href="#content">Skip to content</a><header><a class="brand" href="'+base+'">'+brand+'</a><nav aria-label="Primary"><a href="https://docs.artifactd.net/">Server docs ↗</a><a href="'+repo+'">GitHub ↗</a></nav><a class="download" href="'+repo+'/actions/workflows/ci.yml">Packages</a><button class="menu-toggle" type="button" aria-label="Toggle documentation navigation" aria-controls="documentation-navigation" aria-expanded="false">☰</button><button class="theme" type="button" aria-label="Toggle color theme">◐</button></header><div class="layout"><aside class="sidebar" id="documentation-navigation"><div class="search"><label for="search">Search SDK documentation</label><input id="search" type="search" placeholder="Find a guide or example…" autocomplete="off"><div id="search-results" role="status" aria-live="polite" hidden></div></div><p class="nav-label">SDK version '+esc(version)+'</p><nav aria-label="Documentation">'+nav+'</nav></aside><main id="content"><div class="eyebrow">'+esc(page.group)+'</div><article>'+content+'</article><footer>Artifactd Client Feedback SDK '+esc(version)+' · <a href="'+(page.route==='examples/'?repo+'/tree/main/examples':repo+'/blob/main/'+page.source)+'">View source on GitHub</a></footer></main><aside class="toc"><p class="nav-label">On this page</p>'+toc.map(h=>'<a class="depth-'+h.depth+'" href="#'+h.id+'">'+esc(h.text)+'</a>').join('')+'</aside></div><footer class="site-footer"><div class="footer-grid"><div><a class="brand" href="'+base+'">'+brand+'</a><p>Client Feedback for .NET, Python, Node.js, Rust and PHP.</p></div><nav aria-label="Developer resources"><strong>Developers</strong><a href="https://docs.artifactd.net/api">REST API and OpenAPI</a><a href="https://docs.artifactd.net/ai">AI integration</a><a href="'+repo+'">SDK repository ↗</a></nav><nav aria-label="Product resources"><strong>Product</strong><a href="https://artifactd.net/">Official website ↗</a><a href="https://artifactd.net/download">Download Artifactd</a></nav></div><div class="copyright">© '+new Date().getFullYear()+' Aurora. All rights reserved.</div></footer></body></html>';
 const out=path.join(dist,page.route);await mkdir(out,{recursive:true});await writeFile(path.join(out,'index.html'),html);
}
// Preserve existing links while exposing one .NET guide in navigation and search.
await mkdir(path.join(dist,'dotnet/integration'),{recursive:true});
const dotnetUrl=url('dotnet/')+'#integration-guide';
await writeFile(path.join(dist,'dotnet/integration/index.html'),'<!doctype html><html lang="en"><head><meta charset="utf-8"><title>.NET integration guide</title><link rel="canonical" href="'+url('dotnet/')+'"><meta http-equiv="refresh" content="0;url='+dotnetUrl+'"><script>location.replace('+JSON.stringify(url('dotnet/'))+'+(location.hash||"#integration-guide"))</script></head><body><a href="'+dotnetUrl+'">Continue to the .NET guide</a></body></html>');
await writeFile(path.join(dist,'search.json'),JSON.stringify(pages.map(p=>({title:p.title,url:url(p.route),text:p.text.replace(/\s+/g,' ')}))));
await writeFile(path.join(dist,'.nojekyll'),'');
await writeFile(path.join(dist,'404.html'),'<!doctype html><html lang="en"><meta charset="utf-8"><title>Page not found</title><h1>Page not found</h1><p><a href="'+base+'">Artifactd SDK documentation</a></p></html>');
await writeFile(path.join(dist,'llms.txt'),'# Artifactd Client Feedback SDKs\n\n> Official SDK documentation for .NET, Python, Node.js/TypeScript, Rust and PHP.\n\nServer API and OpenAPI: https://docs.artifactd.net/api\nSource: '+repo+'\nSDK version: '+version+'\n\n## Guides\n\n'+pages.map(p=>'- ['+p.title+']('+origin+url(p.route)+')').join('\n')+'\n\n## Generated API references\n\n'+references.map(([title,route])=>'- ['+title+']('+origin+url(route)+')').join('\n')+'\n');
await writeFile(path.join(dist,'sitemap.xml'),'<?xml version="1.0" encoding="utf-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">'+pages.map(p=>'<url><loc>'+origin+url(p.route)+'</loc></url>').join('')+'</urlset>');
console.log('Built '+pages.length+' SDK guides and all five generated API references at '+dist+' (base '+base+')');
