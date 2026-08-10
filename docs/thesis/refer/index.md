# 參考文獻庫

論文 PDF 放在本資料夾，**PDF 本身不進版控**（著作權與檔案大小），但這份索引會。

## 使用方式

1. 把 PDF 丟進 `docs/thesis/refer/`，檔名建議 `<作者><年>-<關鍵字>.pdf`
2. 告訴我「新增了 XXX」
3. 我讀 PDF 中繼資料補完 `../main/my_bib.bib` 的條目，並更新下表

⚠️ **請不要手動編輯 `my_bib.bib` 而不更新本表**——兩者不同步時，我無法判斷哪一份是對的。

---

## 已收錄

| 檔名 | BibTeX key | 出處 | 在論文中的角色 |
|---|---|---|---|
| *(尚無 PDF)* | `jiao2026online` | Omega 138 (2026) 103374 | **直接前身**：線上聯合最佳化 OA+PS+TA（不含拆單）。本 repo 即其 EE-RAWSim-O 的延伸 |
| *(尚無 PDF)* | `xie2021split` | EJOR 288(1) (2021) 80–97 | **拆單的來源**：離線 POA+PPS，引入 split-among-stations 與 split-over-time |
| *(尚無 PDF)* | `tadumadze2023assigning` | FSMJ (2023) | 有限庫存 + 拆單的 OA–PS，但**不含 TA**；且明確把 packing 追蹤排除在範圍外 |

---

## 待補清單

- [ ] RAWSimO 模擬框架的原始出處（Merschformann et al.）
- [ ] SOAR（arXiv 2605.03842）— event-driven 即時聯合決策，作為 2026 年研究脈絡佐證
- [ ] RMFS 綜述類文獻（用於第二章開頭鋪陳）
- [ ] 訂單批次／揀貨站指派的更早期文獻（Jiao et al. 更早那篇 OA+PS，即 M2G 的出處）

---

## 🚨 引用時的既有教訓

- **Xie = 拆單論文；Jiao = 線上聯合框架且不拆單。M1G 是 Jiao 的模型。** 曾經對調過一次，據此做出的推論全錯。
- **`online.pdf` 全篇未評估過拆單。** M1G vs M2G 測的是「把 TA 納入聯合決策」，**與拆單無關，不可引用為拆單證據**。
- **不要引用「HADGS ≈ M1G 相差 1%」這個數值**——本研究實測 HADGS 件數比 M1G 高 9.38%（t=13.72），寫進論文會與自己的表格矛盾。引論文確立方法出處即可，數字報自己的。
