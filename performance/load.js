import http from 'k6/http';
import {check,sleep} from 'k6';
import {Trend} from 'k6/metrics';
const fixture=JSON.parse(open('/results/context.json'));
const accountLatency=new Trend('account_latency',true),reportLatency=new Trend('report_latency',true),trialLatency=new Trend('trial_latency',true);
export const options={vus:10,duration:'25s',summaryTrendStats:['avg','p(50)','p(95)','max','count'],thresholds:{http_req_failed:['rate<0.01'],checks:['rate==1'],account_latency:['p(95)<1500'],report_latency:['p(95)<1500'],trial_latency:['p(95)<3000']}};
const base='http://cfis-perf-api:8080/api/v1';
export function setup(){
 const login=http.post(base+'/auth/login',JSON.stringify({email:fixture.Email,password:fixture.Password}),{headers:{'Content-Type':'application/json'}});if(!check(login,{'真实登录成功':r=>r.status===200}))throw new Error('Performance login failed');
 const token=login.json('accessToken'),headers={Authorization:'Bearer '+token};const isolation=http.get(base+'/organizations/'+fixture.OtherOrganizationId+'/accounts',{headers,responseCallback:http.expectedStatuses(404)});check(isolation,{'跨企业返回404':r=>r.status===404});
 // Verify the fixture itself before measuring latency.
 const trial=http.get(base+'/organizations/'+fixture.OrganizationId+'/accounting/trial-balance?dateFrom=2026-01-01&dateTo=2026-12-31&currency=CNY',{headers});check(trial,{'十万凭证借贷平衡':r=>r.status===200&&r.json('difference')==='0.0000'&&r.json('profitBeforeClosing')==='250000.0000'});
 return {token};
}
export default function(data){const headers={Authorization:'Bearer '+data.token};const org=base+'/organizations/'+fixture.OrganizationId;let response;
 response=http.get(org+'/transactions?accountId='+fixture.AccountId+'&dateFrom=2026-10-01&dateTo=2026-10-31&pageSize=20',{headers,tags:{endpoint:'transactions'}});accountLatency.add(response.timings.duration);check(response,{'交易分页成功':r=>r.status===200&&r.json('items').length<=20});
 response=http.get(org+'/reports/summary?accountId='+fixture.AccountId+'&dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY',{headers,tags:{endpoint:'report'}});reportLatency.add(response.timings.duration);check(response,{'报表成功且金额为字符串':r=>r.status===200&&typeof r.json('summary.closing')==='string'});
 response=http.get(org+'/accounting/trial-balance?dateFrom=2026-01-01&dateTo=2026-12-31&currency=CNY',{headers,tags:{endpoint:'trial'}});trialLatency.add(response.timings.duration);if(response.status!==200)console.error('Trial failure: '+response.status+' '+response.body.slice(0,300));check(response,{'试算平衡成功':r=>r.status===200&&r.json('difference')==='0.0000'});sleep(0.1);
}
export function handleSummary(data){return {['/results/'+__ENV.PHASE+'-k6.json']:JSON.stringify(data,null,2),stdout:JSON.stringify({phase:__ENV.PHASE,metrics:Object.fromEntries(['account_latency','report_latency','trial_latency','http_req_failed','checks','http_reqs'].map(k=>[k,data.metrics[k]]))},null,2)};}
