import http from 'k6/http';
import crypto from 'k6/crypto';
import {check,sleep} from 'k6';
import {Trend} from 'k6/metrics';
const f=JSON.parse(open('/results/context.json')),base='http://cfis-perf-api:8080/api/v1';
const write=new Trend('write_latency',true),settle=new Trend('settlement_latency',true),post=new Trend('posting_latency',true),exportTime=new Trend('export_latency',true);
const duration=__ENV.DURATION||'120s';
export const options={scenarios:{writes:{executor:'constant-vus',vus:4,duration,exec:'writes'},settlements:{executor:'constant-vus',vus:2,duration,exec:'settlements'},journals:{executor:'constant-vus',vus:1,duration,exec:'journals'},reports:{executor:'constant-vus',vus:1,duration,exec:'reports'}},summaryTrendStats:['avg','p(50)','p(95)','max','count'],thresholds:{http_req_failed:['rate==0'],checks:['rate==1'],write_latency:['p(95)<2000'],settlement_latency:['p(95)<2000'],posting_latency:['p(95)<2000'],export_latency:['p(95)<5000']}};
function id(){const h=crypto.md5(crypto.randomBytes(16),'hex');return h.slice(0,8)+'-'+h.slice(8,12)+'-'+h.slice(12,16)+'-'+h.slice(16,20)+'-'+h.slice(20)}
function send(path,body,h){return http.post(path,JSON.stringify(body),{headers:h});}
function ok(r,label){return check(r,{[label]:r=>r.status===200||r.status===201||r.status===204});}
export function setup(){
 const login=send(base+'/auth/login',{email:f.Email,password:f.Password},{'Content-Type':'application/json'});if(!ok(login,'登录'))throw new Error('login failed');
 const h={Authorization:'Bearer '+login.json('accessToken'),'Content-Type':'application/json'},root=base+'/organizations/'+f.OrganizationId;
 const categories=http.get(root+'/categories?pageSize=100',{headers:h}).json('items'),category=categories.find(x=>x.kind==='Income').id;
 const second=send(root+'/categories',{name:'V1压力分摊收入',kind:'Income'},h);if(!ok(second,'分摊分类'))throw new Error('category failed');
 const party=send(root+'/counterparties',{requestId:id(),name:'Phase6混合压力客户',kind:'Customer'},h);if(!ok(party,'客户创建'))throw new Error('party failed');
 const doc=send(root+'/receivables',{requestId:id(),number:'MIXED-'+id(),counterpartyId:party.json('id'),categoryId:category,currency:'CNY',amount:'1000000',issueDate:'2026-10-02',dueDate:'2026-12-31'},h);if(!ok(doc,'应收创建'))throw new Error('doc failed');
 const asset=send(root+'/accounting/accounts',{code:'MIXA'+Date.now(),name:'压力非资金资产',kind:'Asset',currency:'CNY'},h),equity=send(root+'/accounting/accounts',{code:'MIXE'+Date.now(),name:'压力权益',kind:'Equity',currency:'CNY'},h);if(!ok(asset,'资产科目')||!ok(equity,'权益科目'))throw new Error('ledger failed');
 return {h,root,category,secondCategory:second.json('id'),doc:doc.json('id'),asset:asset.json('id'),equity:equity.json('id')};
}
export function writes(d){const body={requestId:id(),accountId:f.AccountId,kind:'Income',categoryId:d.category,amount:'0.0001',businessDate:'2026-10-02',description:'Phase6Mixed'};if(__ITER%2===0){body.amount='0.0002';body.allocations=[{categoryId:d.category,amount:'0.0001'},{categoryId:d.secondCategory,amount:'0.0001'}]}let r=send(d.root+'/transactions',body,d.h);write.add(r.timings.duration);if(ok(r,'同账户收入成功')){const original=r.json('id');r=send(d.root+'/transactions',body,d.h);check(r,{'同请求重试仅一笔':r=>(r.status===200||r.status===201)&&r.json('id')===original});}sleep(.05)}
export function settlements(d){const body={requestId:id(),accountId:f.AccountId,amount:'0.0001',businessDate:'2026-10-02',description:'Phase6MixedSettlement'};const url=d.root+'/receivables/'+d.doc+'/settlements';let r=send(url,body,d.h);settle.add(r.timings.duration);if(ok(r,'并发收款成功')){const original=r.json('id');r=send(url,body,d.h);check(r,{'结算重试仅一笔':r=>(r.status===200||r.status===201)&&r.json('id')===original});}sleep(.05)}
export function journals(d){const r=send(d.root+'/accounting/journals',{requestId:id(),businessDate:'2026-10-02',currency:'CNY',description:'Phase6MixedJournal',lines:[{ledgerAccountId:d.asset,debit:'0.0001',credit:'0'},{ledgerAccountId:d.equity,debit:'0',credit:'0.0001'}]},d.h);if(ok(r,'并发草稿成功')){const url=d.root+'/accounting/journals/'+r.json('id')+'/post',body={version:r.json('version')};let p=send(url,body,d.h);post.add(p.timings.duration);if(ok(p,'并发过账成功')){const number=p.json('number');p=send(url,body,d.h);check(p,{'重复过账编号相同':p=>p.status===200&&p.json('number')===number});}}sleep(.05)}
export function reports(d){
 let r=http.get(d.root+'/reports/summary?currency=CNY&dateFrom=2026-10-01&dateTo=2026-10-31',{headers:d.h});ok(r,'写入期间报表成功');
 r=http.get(d.root+'/reports/export.xlsx?currency=CNY&dateFrom=2026-10-01&dateTo=2026-10-31',{headers:d.h,responseType:'binary'});exportTime.add(r.timings.duration);check(r,{'并行Excel导出成功':r=>r.status===200&&r.body.byteLength>100});
 r=http.get(d.root+'/accounting/trial-balance?currency=CNY&dateFrom=2026-01-01&dateTo=2026-12-31',{headers:d.h});check(r,{'写入期间试算平衡':r=>r.status===200&&r.json('difference')==='0.0000'});sleep(.25);
}
export function handleSummary(data){const names=['write_latency','settlement_latency','posting_latency','export_latency','http_req_failed','checks','http_reqs','iterations'];const result={duration,scenarios:options.scenarios,metrics:Object.fromEntries(names.map(k=>[k,data.metrics[k]]))};return {'/results/mixed-k6.json':JSON.stringify(result,null,2),stdout:JSON.stringify(result,null,2)}}
