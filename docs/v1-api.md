# v1新增接口与数据规则

沿用 `/api/v1` 前缀和Bearer认证。企业业务接口验证当前成员关系，外企业404，企业内权限不足403。金额为最多4位小数的精确字符串。

## 分类分摊

POST `/organizations/{org}/transactions`，普通收支示例：

```json
{
  "requestId":"非空UUID",
  "accountId":"本企业账户UUID",
  "categoryId":"第一个分摊分类UUID",
  "kind":"Expense",
  "amount":"100.0001",
  "businessDate":"2026-11-01",
  "description":"办公与差旅",
  "allocations":[
    {"categoryId":"第一个分摊分类UUID","amount":"60"},
    {"categoryId":"第二个分摊分类UUID","amount":"40.0001"}
  ]
}
```

仅普通Income/Expense，2–100个唯一有效同方向分类。主categoryId必须属于明细，用于旧客户端摘要兼容；不能用主分类金额替代分摊统计。无allocations或空数组保持旧单分类行为。

幂等请求比较总额、主字段及规范排序后的分摊内容；不一致409。全部明细与资金、审计同一事务提交。数据库延迟约束检查总额，已提交分摊不可改删或后补。GET `/transactions/{id}` 返回allocations，反向流水复制原明细。

只读视图category_transactions以UNION ALL组合旧单分类流水和新分摊明细；所有查询仍必须显式按企业、日期、账户、币种隔离。预算、分类报表使用此视图，资金余额继续直接按原交易计算。过账按每个分类映射生成分录，反向来源精确反转原分录。

## 附件

企业前缀 `/organizations/{org}/attachments`：

|方法|路径|请求与结果|
|---|---|---|
|POST|`/`|multipart/form-data，resourceKind、resourceId、file；返回id/name/sha256|
|GET|`/?resourceKind=Transaction&resourceId=UUID`|业务附件元数据，最多100项，不返回文件字节|
|GET|`/{id}`|需要认证的文件下载，Content-Disposition为attachment|

resourceKind取Transaction、Receivable、Payable、Journal，Journal要求已过账。文件1字节至5MiB，扩展名及文件头一致的PDF/PNG/JPEG，文件名最长200，拒绝控制字符。每项业务最多100个。只追加，不提供更新删除接口。

attachments表内bytea保存内容，SHA256保存大写十六进制摘要。数据库约束验证长度、摘要和业务引用；凭证要求已过账，避免删除草稿造成附件孤立。上传与审计同一事务。运行账号只读和追加，文件不通过公开静态路径暴露。

首版文件头检查不等于完整格式解析或恶意软件扫描。应用以下载方式返回，不能把不可信PDF/图片当作网页执行。

## 演示账套

POST `/demo`，已认证用户，无需指定目标企业。服务为当前用户创建独立合成账套，初始化资金走真实交易服务，凭证走真实会计服务。

每用户固定演示企业标识，初始化使用固定幂等请求和用户级锁。DemoCompleted审计标志存在后，重复调用仅打开原账套，不重新生成草稿或覆盖后续业务。初始化中断后可重试。没有重置或删除其他企业的数据接口。

## 数据库与升级

Phase6原12条迁移后依次增加Attachments、TransactionAllocations、V1Guards、AttachmentPostedReferences，共16条。迁移没有重写旧金额、旧分类或已过账分录。`/health/ready`要求不存在待执行迁移。

恢复摘要v6包含分摊、附件元数据及重新计算的内容摘要；实际文件内容随数据库dump保存。旧摘要只执行兼容一致性检查，不声称与v6相同。
