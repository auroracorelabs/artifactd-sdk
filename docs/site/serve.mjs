import http from 'node:http';
import {readFile,stat} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../../dist/docs/',import.meta.url));
const base=process.env.DOCS_BASE_PATH??'/artifactd-sdk/';
if(!/^\/(?:[A-Za-z0-9._-]+\/)*$/.test(base))throw new Error('Invalid DOCS_BASE_PATH');
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.json':'application/json; charset=utf-8','.svg':'image/svg+xml','.woff2':'font/woff2','.txt':'text/plain; charset=utf-8','.xml':'application/xml','.yml':'text/yaml; charset=utf-8'};
const server=http.createServer(async(req,res)=>{
 if(!['GET','HEAD'].includes(req.method)){res.writeHead(405).end();return;}
 try{
  const url=new URL(req.url,'http://localhost');
  if(base!=='/'&&url.pathname==='/'){res.writeHead(302,{Location:base}).end();return;}
  if(!url.pathname.startsWith(base))throw new Error('Outside project path');
  let file=path.resolve(root,decodeURIComponent(url.pathname.slice(base.length)));
  if(file!==path.resolve(root)&&!file.startsWith(path.resolve(root)+path.sep))throw new Error('Outside static root');
  if((await stat(file)).isDirectory())file=path.join(file,'index.html');
  const body=await readFile(file);res.writeHead(200,{'Content-Type':types[path.extname(file)]??'application/octet-stream','X-Content-Type-Options':'nosniff','Cache-Control':'no-store'});res.end(req.method==='HEAD'?undefined:body);
 }catch{
  res.writeHead(404,{'Content-Type':'text/html; charset=utf-8'});
  res.end(req.method==='HEAD'?undefined:await readFile(path.join(root,'404.html')).catch(()=>'Run the documentation build first.'));
 }
});
server.listen(Number(process.env.PORT??4322),'127.0.0.1',()=>console.log('Local: http://127.0.0.1:'+server.address().port+base));
