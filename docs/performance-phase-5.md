# Phase 5 性能实测与执行计划

日期：2026-11-01（Asia/Shanghai）。结果为本机实际观测，不代表生产SLA。

## 环境和数据

Windows + Docker Desktop Linux引擎；Docker分配12 CPU、约15.50GiB内存；PostgreSQL 18.6，shared_buffers=128MB，work_mem=4MB，/dev/shm=256MB。后端ASP.NET Core 10受限运行账号。k6镜像锁定grafana/k6:1.4.0。

独立cfis_perf_UUID数据库生成：目标企业10万交易、100个资金账户；背景企业额外10万交易，验证租户隔离；目标企业10万已过账凭证、20万分录，借贷均750000、收入500000、费用250000、净额250000。数据明确标记为测试，数据库约束和过账触发器保持启用。夹具直接批量生成合法记录，不用于测量写入API速度；k6读取真实API并验证登录、跨企业404、试算结果、分页和金额类型。

两轮各10VU、25秒，每次迭代顺序调用交易分页、资金报表、试算表，然后休眠100ms。before复原旧账户外键索引并移除新增覆盖索引；after安装新增覆盖/部分索引并移除被替代旧索引；两轮都更新统计信息。除索引外，数据、API、资源配置与请求一致。顺序运行、缓存及JIT仍可能影响数值，不将单轮差异解释为严格统计因果结论。

## 实际HTTP结果（毫秒）

|接口|优化前P50|优化前P95|优化后P50|优化后P95|
|---|---:|---:|---:|---:|
|账户交易分页|9.49|17.83|5.22|10.12|
|账户资金报表|17.84|31.71|14.03|20.35|
|企业试算表|93.19|112.76|86.68|110.28|

before共1099次完整迭代、3300个HTTP请求（含setup）；after共1184次、3555个请求。两轮http_req_failed=0，checks=1。P95交易查询约改善43.25%，资金报表约35.83%，试算表约2.20%；试算表高分位改善有限，不夸大索引收益。

阈值：交易和报表P95<1500ms；试算P95<3000ms；HTTP失败率<1%；所有业务检查必须通过。指标保留avg、P50、P95、max、count，见已提交JSON。该实验为读负载；写操作原子性、并发和幂等由集成测试覆盖，不将读压测当作写吞吐验收。

## 实际执行计划

使用EXPLAIN (ANALYZE,BUFFERS,FORMAT JSON)，原始计划已保存在结果JSON中：

- 账户日期交易查询：4.129ms → 0.120ms。旧索引扫描后排序，读取共享命中块520；优化后ix_transactions_account_date_cover执行Index Only Scan，命中1块，直接返回有序结果。
- 全年试算聚合：70.630ms → 57.363ms。表头改用ix_journal_posted_date_cover的Index Only Scan，共享命中由7860降至2676；分录仍采用顺序扫描及并行哈希聚合，因为查询覆盖大量分录，该选择合理。
- 其他新增索引用于转入账户日期及凭证分录查找。并非每个索引在全年聚合中都被选用；索引也有写入和存储成本，后续应结合具体负载继续评估。

## 真实失败及修复

初次压力运行发现少量试算HTTP500，诊断为PostgreSQL SQLSTATE 53100：无法扩展共享内存段，默认64MB /dev/shm不足。排除并发单元测试影响后独立重跑仍出现同样错误，因此没有将其简单归因于测试资源争用。compose.yaml增加shm_size=256mb后，在统一配置下重新完整执行before/after，两轮全部通过。失败日志与失败指标保留在被忽略的performance/results目录；正式指标使用修复后的完整两轮。

## 复现

    docker compose up -d --wait postgres
    docker compose build api
    docker pull grafana/k6:1.4.0
    ./scripts/test-performance.ps1

脚本会创建隔离数据库和cfis-perf-api容器，不开放额外主机API端口，通过Docker网络访问。结果与测试凭据写入performance/results（已忽略Git），finally清理容器及数据库。-KeepDatabase仅用于调试；中断保留完整容器和context后可使用-Resume重新执行两轮。不可将context.json或runtime.env作为报告上传。

参考官方方法：[PostgreSQL EXPLAIN](https://www.postgresql.org/docs/18/using-explain.html)、[k6统计选项](https://grafana.com/docs/k6/latest/using-k6/k6-options/reference/)、[k6阈值](https://grafana.com/docs/k6/latest/using-k6/thresholds/)。
